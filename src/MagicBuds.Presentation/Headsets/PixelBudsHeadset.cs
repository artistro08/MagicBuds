using System.Globalization;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using MagicBuds.Presentation.Logging;
using MagicBuds.Presentation.Maestro;
using MagicBuds.Presentation.Maestro.Proto;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;

namespace MagicBuds.Presentation.Headsets;

/// <summary>
/// <see cref="IHeadset"/> for Pixel Buds Pro 2, over the Maestro RFCOMM service.
/// </summary>
/// <remarks>
/// Connecting opens the RFCOMM socket, asks both buds for their software info and keeps the
/// channel of whichever answers first, subscribes to battery and settings changes, then reads
/// every setting the flyout shows. A setting the buds won't read keeps its default rather than
/// failing the connect. Message shapes are in docs/protocol/notes.md.
/// </remarks>
/// <author>Devin Green (Artistro08)</author>
/// <seealso href="https://github.com/qzed/pbpctrl"/>
public sealed class PixelBudsHeadset : IHeadset
{
    /// <summary>
    /// RFCOMM service the buds' Maestro protocol runs on.
    /// </summary>
    public static readonly Guid MaestroServiceId = new("25e97ff7-24ce-4c4c-8951-f764a708f7b5");

    private static readonly TimeSpan ChannelProbeTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan AnnouncementTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan CloseTimeout        = TimeSpan.FromSeconds(1);

    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private HeadsetSnapshot _state = HeadsetSnapshot.Empty;
    private Link? _link;

    // Streams a dropped link couldn't cancel, for the next link to cancel
    private readonly List<StreamCall> _leftoverStreams = [];

    /// <param name="logger">Gets every packet at Debug level, and settings the buds won't read.</param>
    public PixelBudsHeadset(ILogger logger)
    {
        _logger = logger;
    }

    public HeadsetSnapshot State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public event EventHandler<HeadsetSnapshot>? StateChanged;

    public event EventHandler? Disconnected;

    // =========================================================================
    // CONNECTION
    // =========================================================================

    /// <summary>
    /// Opens the control link and loads the buds' current state.
    /// </summary>
    /// <param name="bluetoothAddress">Address like "AC:80:0A:12:34:56".</param>
    /// <exception cref="IOException">The buds or their Maestro service couldn't be reached.</exception>
    public async Task ConnectAsync(string bluetoothAddress)
    {
        Disconnect();

        // Socket
        var device = await BluetoothDevice.FromBluetoothAddressAsync(ParseAddress(bluetoothAddress))
            ?? throw new IOException("Windows doesn't know these buds.");
        var link = new Link(device);
        try
        {
            var serviceId = RfcommServiceId.FromUuid(MaestroServiceId);
            var services  = await device.GetRfcommServicesForIdAsync(serviceId, BluetoothCacheMode.Uncached);
            if (services.Services.Count == 0)
            {
                services = await device.GetRfcommServicesForIdAsync(serviceId, BluetoothCacheMode.Cached);
            }
            if (services.Services.Count == 0)
            {
                throw new IOException("The buds don't offer their control service right now.");
            }
            var service = services.Services[0];
            foreach (var offered in services.Services)
            {
                var description = $"{offered.ConnectionHostName} {offered.ConnectionServiceName} {offered.ServiceId.Uuid}";
                LogMessages.ServiceOffered(_logger, description);
            }

            await link.Socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName);
            link.Client = new MaestroClient(link.Socket.InputStream.AsStreamForRead(0), link.Socket.OutputStream.AsStreamForWrite(0), _logger);
            link.Client.Closed += OnClientClosed;

            lock (_gate)
            {
                _link  = link;
                _state = HeadsetSnapshot.Empty;
            }

            // Channel and Firmware
            var (channel, software) = await ResolveChannelAsync(link.Client).ConfigureAwait(false);
            link.Channel = channel;
            Update(state => state with { Firmware = software.Firmware?.Left?.VersionString ?? "" });

            // Leftovers: the buds keep a dropped link's streams and play their mode-change tone
            // once more for each, so cancel them before opening new ones
            StreamCall[] leftover;
            lock (_gate)
            {
                leftover = [.. _leftoverStreams];
                _leftoverStreams.Clear();
            }
            foreach (var stream in leftover)
            {
                await link.Client.CancelLeftoverAsync(stream).ConfigureAwait(false);
            }

            // Subscriptions: battery for both buds, and changes made on the buds. Both stay open
            // until the link closes, which cancels them on the buds (see MaestroClient.DisposeAsync).
            await link.Client.SubscribeAsync(channel, "SubscribeRuntimeInfo", payload =>
                OnRuntimeInfo(RuntimeInfo.Parser.ParseFrom(payload))).ConfigureAwait(false);
            await link.Client.SubscribeAsync(channel, "SubscribeToSettingsChanges", payload =>
                Update(state => Apply(state, SettingsRsp.Parser.ParseFrom(payload).Value))).ConfigureAwait(false);

            // Settings
            foreach (var setting in ReadOnStart)
            {
                await ReadSettingAsync(link, setting).ConfigureAwait(false);
            }
        }
        catch
        {
            // The link may not be published yet (the socket failed to connect), so close it here too
            Disconnect();
            _ = link.Close(OnClientClosed);
            throw;
        }
    }

    /// <summary>
    /// Closes the control link. Doesn't raise <see cref="Disconnected"/>.
    /// </summary>
    public void Disconnect() => _ = Close();

    /// <summary>
    /// Closes the link and waits (up to a second) for its subscriptions to be cancelled on the
    /// buds, since the app may exit straight after.
    /// </summary>
    public void Dispose() => Close().Wait(CloseTimeout);

    private Task Close()
    {
        Link? link;
        lock (_gate)
        {
            link  = _link;
            _link = null;
        }
        return link?.Close(OnClientClosed) ?? Task.CompletedTask;
    }

    // =========================================================================
    // SETTINGS
    // =========================================================================

    public Task SetNoiseModeAsync(NoiseMode mode) =>
        WriteAsync(new SettingValue { CurrentAncrState = ToAncState(mode) }, state => state with { NoiseMode = mode });

    public Task SetConversationDetectionAsync(bool enabled) =>
        WriteAsync(new SettingValue { SpeechDetection = enabled }, state => state with { ConversationDetection = enabled });

    public Task SetMultipointAsync(bool enabled) =>
        WriteAsync(new SettingValue { MultipointEnable = enabled }, state => state with { Multipoint = enabled });

    public Task SetEqualizerAsync(EqualizerBands bands)
    {
        ArgumentNullException.ThrowIfNull(bands);

        var value = new SettingValue
        {
            CurrentUserEq = new EqBands
            {
                LowBass     = (float)bands.LowBass,
                Bass        = (float)bands.Bass,
                Mid         = (float)bands.Mid,
                Treble      = (float)bands.Treble,
                UpperTreble = (float)bands.UpperTreble,
            },
        };
        return WriteAsync(value, state => state with { Equalizer = bands });
    }

    public Task SetTouchControlsAsync(bool enabled) =>
        WriteAsync(new SettingValue { GestureEnable = enabled }, state => state with { TouchControls = enabled });

    public Task SetHeadGesturesAsync(bool enabled) =>
        WriteAsync(
            new SettingValue { HeadGestures = enabled ? HeadGestureState.HeadGesturesOn : HeadGestureState.HeadGesturesOff },
            state => state with { HeadGestures = enabled });

    /// <summary>
    /// Changes one earbud's press and hold. Only that side is sent, like the web app does.
    /// </summary>
    public Task SetHoldActionAsync(EarbudSide side, HoldAction action)
    {
        var target = new DeviceGestureControl { Type = new GestureControlType { Value = ToActionTarget(action) } };
        var control = side == EarbudSide.Left
            ? new GestureControl { Left = target }
            : new GestureControl { Right = target };

        return WriteAsync(
            new SettingValue { GestureControl = control },
            state => side == EarbudSide.Left ? state with { LeftHold = action } : state with { RightHold = action });
    }

    public Task SetNoiseModeCycleAsync(NoiseModeCycle cycle)
    {
        ArgumentNullException.ThrowIfNull(cycle);

        var loop = new AncrGestureLoop
        {
            Active   = cycle.NoiseCancellation,
            Off      = cycle.Off,
            Aware    = cycle.Transparency,
            Adaptive = cycle.Adaptive,
        };
        return WriteAsync(new SettingValue { AncrGestureLoop = loop }, state => state with { NoiseCycle = cycle });
    }

    // =========================================================================
    // MESSAGE MAPPING
    // =========================================================================

    private static readonly SettingId[] ReadOnStart =
    [
        SettingId.CurrentAncrState,
        SettingId.SpeechDetection,
        SettingId.MultipointEnable,
        SettingId.CurrentUserEq,
        SettingId.GestureEnable,
        SettingId.HeadGestures,
        SettingId.GestureControl,
        SettingId.AncrGestureLoop,
    ];

    /// <summary>
    /// Applies one setting the buds reported to the snapshot. Settings the app doesn't show
    /// are ignored.
    /// </summary>
    internal static HeadsetSnapshot Apply(HeadsetSnapshot state, SettingValue? value) => value?.ValueOneofCase switch
    {
        SettingValue.ValueOneofOneofCase.CurrentAncrState => state with { NoiseMode = FromAncState(value.CurrentAncrState) },
        SettingValue.ValueOneofOneofCase.SpeechDetection => state with { ConversationDetection = value.SpeechDetection },
        SettingValue.ValueOneofOneofCase.MultipointEnable => state with { Multipoint = value.MultipointEnable },
        SettingValue.ValueOneofOneofCase.GestureEnable => state with { TouchControls = value.GestureEnable },
        SettingValue.ValueOneofOneofCase.HeadGestures => state with { HeadGestures = value.HeadGestures == HeadGestureState.HeadGesturesOn },
        SettingValue.ValueOneofOneofCase.CurrentUserEq => state with
        {
            Equalizer = new EqualizerBands(
                value.CurrentUserEq.UpperTreble,
                value.CurrentUserEq.Treble,
                value.CurrentUserEq.Mid,
                value.CurrentUserEq.Bass,
                value.CurrentUserEq.LowBass),
        },
        SettingValue.ValueOneofOneofCase.GestureControl => state with
        {
            LeftHold  = FromActionTarget(value.GestureControl.Left) ?? state.LeftHold,
            RightHold = FromActionTarget(value.GestureControl.Right) ?? state.RightHold,
        },
        SettingValue.ValueOneofOneofCase.AncrGestureLoop => state with
        {
            NoiseCycle = new NoiseModeCycle(
                value.AncrGestureLoop.Active,
                value.AncrGestureLoop.Off,
                value.AncrGestureLoop.Adaptive,
                value.AncrGestureLoop.Aware),
        },
        _ => state,
    };

    /// <summary>
    /// Battery levels from a runtime info message. A bud or the case without a report shows
    /// as null.
    /// </summary>
    internal static BatteryLevels ToBattery(RuntimeInfo info)
    {
        var battery = info.BatteryInfo;
        if (battery is null)
        {
            return BatteryLevels.Unknown;
        }

        static int? Level(DeviceBatteryInfo? cell) => cell?.Level;

        var charging = new[] { battery.Left, battery.Right }.Any(cell => cell?.State == BatteryState.BatteryCharging);
        return new BatteryLevels(null, Level(battery.Left), Level(battery.Right), Level(battery.Case), charging);
    }

    private static AncState ToAncState(NoiseMode mode) => mode switch
    {
        NoiseMode.NoiseCancellation => AncState.Active,
        NoiseMode.Transparency      => AncState.Aware,
        NoiseMode.Adaptive          => AncState.Adaptive,
        _                           => AncState.Off,
    };

    private static NoiseMode? FromAncState(AncState state) => state switch
    {
        AncState.Active   => NoiseMode.NoiseCancellation,
        AncState.Aware    => NoiseMode.Transparency,
        AncState.Adaptive => NoiseMode.Adaptive,
        AncState.Off      => NoiseMode.Off,
        _                 => null,
    };

    private static RegularActionTarget ToActionTarget(HoldAction action) => action == HoldAction.DigitalAssistant
        ? RegularActionTarget.ActionTargetAssistantQuery
        : RegularActionTarget.ActionTargetAncControl;

    private static HoldAction? FromActionTarget(DeviceGestureControl? control) => control?.Type?.Value switch
    {
        RegularActionTarget.ActionTargetAncControl     => HoldAction.ToggleNoiseControl,
        RegularActionTarget.ActionTargetAssistantQuery => HoldAction.DigitalAssistant,
        _                                              => null,
    };

    private static ulong ParseAddress(string address) =>
        ulong.Parse(address.Replace(":", "", StringComparison.Ordinal), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    // =========================================================================
    // LINK
    // =========================================================================

    // Uses the channel the buds announce when the link opens, like the web app, so no calls
    // are left waiting on channels the buds don't serve this session. Every channel is only
    // tried when no announcement comes. The buds only serve one pair per session, and either
    // bud can be in
    // the case.
    private static async Task<(uint Channel, SoftwareInfo Info)> ResolveChannelAsync(MaestroClient client)
    {
        try
        {
            var announced = await client.AnnouncedChannel.WaitAsync(AnnouncementTimeout).ConfigureAwait(false);
            return await AskAsync(announced).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // No announcement, or no answer on the announced channel; try them all
        }

        var attempts = MaestroClient.Channels.Select(AskAsync).ToList();
        while (attempts.Count > 0)
        {
            var done = await Task.WhenAny(attempts).ConfigureAwait(false);
            attempts.Remove(done);
            if (done.IsCompletedSuccessfully)
            {
                // The other channels' timeouts are expected; observe them so they aren't reported
                foreach (var other in attempts)
                {
                    _ = other.ContinueWith(task => task.Exception, TaskScheduler.Default);
                }
                return done.Result;
            }
        }
        throw new IOException("No bud answered on a control channel.");

        async Task<(uint, SoftwareInfo)> AskAsync(uint channel) =>
            (channel, SoftwareInfo.Parser.ParseFrom(await client.CallAsync(channel, "GetSoftwareInfo", timeout: ChannelProbeTimeout).ConfigureAwait(false)));
    }

    private async Task ReadSettingAsync(Link link, SettingId setting)
    {
        try
        {
            var reply = await link.Client!.CallAsync(link.Channel, "ReadSetting", new ReadSettingMsg { SettingsId = setting }).ConfigureAwait(false);
            Update(state => Apply(state, SettingsRsp.Parser.ParseFrom(reply).Value));
        }
        catch (MaestroException ex)
        {
            // The buds don't have this setting; keep the default
            var name = setting.ToString();
            LogMessages.SettingUnreadable(_logger, name, ex.Status);
        }
        catch (InvalidProtocolBufferException ex)
        {
            var name = setting.ToString();
            LogMessages.SettingMalformed(_logger, ex, name);
        }
    }

    private async Task WriteAsync(SettingValue value, Func<HeadsetSnapshot, HeadsetSnapshot> apply)
    {
        Link link;
        lock (_gate)
        {
            link = _link ?? throw new IOException("The buds aren't connected.");
        }

        await link.Client!.CallAsync(link.Channel, "WriteSetting", new WriteSettingMsg { Setting = value }).ConfigureAwait(false);
        Update(apply);
    }

    private void OnRuntimeInfo(RuntimeInfo info)
    {
        var battery = ToBattery(info);
        Update(state => state with
        {
            // A message without battery keeps the last levels
            Battery = battery == BatteryLevels.Unknown ? state.Battery : battery,
        });
    }

    private void Update(Func<HeadsetSnapshot, HeadsetSnapshot> change)
    {
        HeadsetSnapshot state;
        lock (_gate)
        {
            if (_link is null)
            {
                return;
            }
            state = change(_state);
            if (state == _state)
            {
                return;
            }
            _state = state;
        }
        StateChanged?.Invoke(this, state);
    }

    private void OnClientClosed(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            // Only the current link's close counts; an old one closing after a reconnect doesn't
            if (sender is not MaestroClient client || _link?.Client != client)
            {
                return;
            }
            _leftoverStreams.AddRange(client.DroppedStreams);
        }
        Disconnect();
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The open socket and everything riding on it.
    /// </summary>
    private sealed class Link(BluetoothDevice device)
    {
        public BluetoothDevice Device { get; } = device;

        public StreamSocket Socket { get; } = new();

        public MaestroClient? Client { get; set; }

        public uint Channel { get; set; }

        public Task Close(EventHandler onClosed)
        {
            if (Client is not null)
            {
                Client.Closed -= onClosed;
            }
            return CloseAsync();
        }

        // The client cancels its streams on the buds first; the socket closes after, or after
        // a second if the buds have stopped listening
        private async Task CloseAsync()
        {
            try
            {
                if (Client is not null)
                {
                    await Client.DisposeAsync().AsTask().WaitAsync(CloseTimeout).ConfigureAwait(false);
                }
            }
            catch (TimeoutException)
            {
                // Close anyway
            }
            finally
            {
                Socket.Dispose();
                Device.Dispose();
            }
        }
    }
}
