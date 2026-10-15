using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MagicBuds.Presentation.Common;
using MagicBuds.Presentation.Devices;
using MagicBuds.Presentation.Headsets;
using MagicBuds.Presentation.Logging;
using MagicBuds.Presentation.Notifications;
using MagicBuds.Presentation.Settings;

namespace MagicBuds.Presentation.ViewModels;

/// <summary>
/// One pair of buds' controls for the flyout's device and controls pages.
/// </summary>
/// <remarks>
/// Controls show what <see cref="_shown"/> holds: the buds' confirmed state plus any change
/// still on its way. Using a control changes it right away, then sends the command. When a
/// command fails, every control goes back to what the buds last confirmed and
/// <see cref="ErrorMessage"/> shows for five seconds. Equalizer sliders send at most one
/// command per 150 ms and always send where the drag stops. Updates from the buds are held
/// back while commands are in flight, so an echo of an older value can't move a control the
/// person is still changing.
/// </remarks>
public sealed class HeadsetViewModel : ObservableObject, IDisposable
{
    public static readonly TimeSpan SliderInterval = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan ErrorDuration  = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Model name, used for the device picture.
    /// </summary>
    public const string Model = "Pixel Buds Pro 2";

    private const string NoValue = "—";

    private readonly ManagedHeadset _managed;
    private readonly IHeadset _headset;
    private readonly AppSettings _settings;
    private readonly LowBatteryMonitor _lowBattery;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly UiContext _ui = new();
    private readonly Throttler _equalizerThrottler;

    private HeadsetSnapshot _shown = HeadsetSnapshot.Empty;
    private HeadsetConnectionState _connectionState;
    private string? _errorMessage;
    private ITimer? _errorTimer;
    private int _commandsInFlight;

    public HeadsetViewModel(
        ManagedHeadset managed,
        AppSettings settings,
        LowBatteryMonitor lowBattery,
        TimeProvider timeProvider,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(managed);

        _managed            = managed;
        _headset            = managed.Headset;
        _settings           = settings;
        _lowBattery         = lowBattery;
        _timeProvider       = timeProvider;
        _logger             = logger;
        _equalizerThrottler = new Throttler(SliderInterval, timeProvider);
        _connectionState    = managed.ConnectionState;

        SetNoiseModeCommand = new RelayCommand<string>(mode =>
        {
            if (Enum.TryParse<NoiseMode>(mode, out var parsed))
            {
                SelectNoiseMode(parsed);
            }
        });
        ReconnectCommand  = new RelayCommand(() => ReconnectRequested?.Invoke(this, EventArgs.Empty));

        _headset.StateChanged += OnHeadsetStateChanged;
        ApplySnapshot(_headset.State);
    }

    // =========================================================================
    // IDENTITY
    // =========================================================================

    public string Id => _managed.Id;

    public string DeviceName => _managed.Name;

    /// <summary>
    /// The buds' colors, each with its own pictures in Assets.
    /// </summary>
    public static IReadOnlyList<string> BudsColors { get; } = ["Porcelain", "Green", "Charcoal", "Pink"];

    /// <summary>
    /// This pair's color (Settings › Devices), which picks its pictures.
    /// </summary>
    public int BudsColorIndex
    {
        get => Math.Max(0, BudsColors.ToList().IndexOf(_settings.GetBudsColor(Id)));
        set
        {
            if (value < 0 || value >= BudsColors.Count || value == BudsColorIndex)
            {
                return;
            }
            _settings.SetBudsColor(Id, BudsColors[value]);
            OnPropertyChanged();
            OnPropertyChanged(nameof(PictureName));
            OnPropertyChanged(nameof(LeftPictureName));
            OnPropertyChanged(nameof(RightPictureName));
        }
    }

    /// <summary>
    /// Picture names in Assets for the chosen color: the pair ("Pixel Buds Pro 2 Green") and each bud
    /// ("Pixel Buds Pro 2 Green Left").
    /// </summary>
    public string PictureName => $"{Model} {_settings.GetBudsColor(Id)}";

    public string LeftPictureName => $"{PictureName} Left";

    public string RightPictureName => $"{PictureName} Right";

    public string Firmware => string.IsNullOrEmpty(_shown.Firmware) ? NoValue : _shown.Firmware;

    // =========================================================================
    // CONNECTION
    // =========================================================================

    public HeadsetConnectionState ConnectionState => _connectionState;

    public bool IsConnected => _connectionState == HeadsetConnectionState.Connected;

    public bool IsConnecting => _connectionState == HeadsetConnectionState.Connecting;

    /// <summary>
    /// Whether Windows has the buds connected (audio), apart from the app's control link.
    /// </summary>
    public bool IsWindowsConnected => _managed.IsWindowsConnected;

    /// <summary>
    /// The app let go of the buds (Disconnect) and stays off them until Reconnect.
    /// </summary>
    public bool IsReleased => !AutoConnect;

    /// <summary>
    /// Worth showing as a destination: Windows has them and the app isn't holding off.
    /// </summary>
    public bool IsAvailable => IsWindowsConnected && !IsReleased;

    /// <summary>
    /// The warning bar and disabled controls show while there's no control link and none is
    /// on the way.
    /// </summary>
    public bool ShowDisconnectedWarning => _connectionState == HeadsetConnectionState.Disconnected;

    /// <summary>
    /// Reconnect shows while disconnected, but only while Windows has the buds; without that
    /// there's nothing to reconnect to.
    /// </summary>
    public bool ShowReconnect => _connectionState == HeadsetConnectionState.Disconnected && IsWindowsConnected;

    public string StatusText => _connectionState switch
    {
        HeadsetConnectionState.Connected  => "Connected",
        HeadsetConnectionState.Connecting => "Connecting…",
        // Windows still has them but the app doesn't (let go, or the control link is down)
        _ when IsWindowsConnected => "App Disconnected",
        _ => "Disconnected",
    };

    /// <summary>
    /// The picker's status line: a failed command, or the connection.
    /// </summary>
    public string PickerStatusText => _errorMessage ?? StatusText;

    /// <summary>
    /// The settings pages' warning bar: the app's side or the whole device.
    /// </summary>
    public string DisconnectedMessage => IsWindowsConnected ? "App Disconnected" : "Your buds are disconnected.";

    /// <summary>
    /// The status line only shows while not connected; once connected the header shows the
    /// battery instead.
    /// </summary>
    public bool ShowStatus => !IsConnected;

    public bool AutoConnect
    {
        get => _settings.IsAutoConnectEnabled(Id);
        set
        {
            if (value == AutoConnect)
            {
                return;
            }
            _settings.SetAutoConnectEnabled(Id, value);
            RaiseAutoConnectChanged();
            AutoConnectChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? AutoConnectChanged;

    /// <summary>
    /// Raised by <see cref="ReconnectCommand"/>; the flyout asks the headset manager to reconnect.
    /// </summary>
    public event EventHandler? ReconnectRequested;

    /// <summary>
    /// Opens the control link again after it dropped.
    /// </summary>
    public IRelayCommand ReconnectCommand { get; }

    // =========================================================================
    // BATTERY
    // =========================================================================

    /// <summary>
    /// The battery row hides while Windows doesn't have the buds; the levels would only be stale.
    /// </summary>
    public bool ShowBattery => IsWindowsConnected;

    public bool ShowDualBattery => ShowLeftBattery || ShowRightBattery;

    /// <summary>
    /// False while that earbud isn't reporting, so its badge hides.
    /// </summary>
    public bool ShowLeftBattery => _shown.Battery.Left is not null;

    public bool ShowRightBattery => _shown.Battery.Right is not null;

    /// <summary>
    /// Levels for the header's battery rings, 0 while unknown.
    /// </summary>
    public double LeftBatteryLevel => _shown.Battery.Left ?? 0;

    public double RightBatteryLevel => _shown.Battery.Right ?? 0;

    public double CaseBatteryLevel => _shown.Battery.Case ?? 0;

    /// <summary>
    /// Battery icons for the compact battery rows, stepped by level.
    /// </summary>
    public string LeftBatteryGlyph => Glyphs.Battery(_shown.Battery.Left);

    public string RightBatteryGlyph => Glyphs.Battery(_shown.Battery.Right);

    public string CaseBatteryGlyph => Glyphs.Battery(_shown.Battery.Case);

    public string LeftBatteryText => Percent(_shown.Battery.Left);

    public string RightBatteryText => Percent(_shown.Battery.Right);

    public string CaseBatteryText => Percent(_shown.Battery.Case);

    public bool ShowCaseBattery => _shown.Battery.Case is not null;

    // =========================================================================
    // NOISE CONTROL
    // =========================================================================

    public IRelayCommand<string> SetNoiseModeCommand { get; }

    /// <summary>
    /// Null while the buds report no mode (out of the ears); no tile shows selected then.
    /// </summary>
    public NoiseMode? NoiseMode => _shown.NoiseMode;

    public bool IsNoiseCancellation => _shown.NoiseMode == Headsets.NoiseMode.NoiseCancellation;

    public bool IsNoiseOff => _shown.NoiseMode == Headsets.NoiseMode.Off;

    public bool IsAdaptive => _shown.NoiseMode == Headsets.NoiseMode.Adaptive;

    public bool IsTransparency => _shown.NoiseMode == Headsets.NoiseMode.Transparency;

    public bool ConversationDetection
    {
        get => _shown.ConversationDetection;
        set => Change(_shown with { ConversationDetection = value }, () => _headset.SetConversationDetectionAsync(value), "conversation detection");
    }

    public bool Multipoint
    {
        get => _shown.Multipoint;
        set => Change(_shown with { Multipoint = value }, () => _headset.SetMultipointAsync(value), "multipoint");
    }

    // =========================================================================
    // EQUALIZER
    // =========================================================================

    public static IReadOnlyList<string> EqualizerPresetNames { get; } = [.. EqualizerPreset.All.Select(preset => preset.Name)];

    /// <summary>
    /// The preset whose bands match the buds', or -1 for a custom curve.
    /// </summary>
    public int SelectedPresetIndex
    {
        get => EqualizerPreset.All.ToList().FindIndex(preset => preset.Bands == _shown.Equalizer);
        set
        {
            if (value < 0 || value >= EqualizerPreset.All.Count || value == SelectedPresetIndex)
            {
                return;
            }
            SetEqualizer(EqualizerPreset.All[value].Bands);
        }
    }

    public double UpperTreble
    {
        get => _shown.Equalizer.UpperTreble;
        set => SetEqualizer(_shown.Equalizer with { UpperTreble = Snap(value) });
    }

    public double Treble
    {
        get => _shown.Equalizer.Treble;
        set => SetEqualizer(_shown.Equalizer with { Treble = Snap(value) });
    }

    public double Mid
    {
        get => _shown.Equalizer.Mid;
        set => SetEqualizer(_shown.Equalizer with { Mid = Snap(value) });
    }

    public double Bass
    {
        get => _shown.Equalizer.Bass;
        set => SetEqualizer(_shown.Equalizer with { Bass = Snap(value) });
    }

    public double LowBass
    {
        get => _shown.Equalizer.LowBass;
        set => SetEqualizer(_shown.Equalizer with { LowBass = Snap(value) });
    }

    // =========================================================================
    // CONTROLS AND GESTURES
    // =========================================================================

    public bool TouchControls
    {
        get => _shown.TouchControls;
        set => Change(_shown with { TouchControls = value }, () => _headset.SetTouchControlsAsync(value), "touch controls");
    }

    public bool HeadGestures
    {
        get => _shown.HeadGestures;
        set => Change(_shown with { HeadGestures = value }, () => _headset.SetHeadGesturesAsync(value), "head gestures");
    }

    /// <summary>
    /// Left press and hold: 0 Toggle Active Noise Control, 1 Digital assistant.
    /// </summary>
    public int LeftHoldIndex
    {
        get => (int)_shown.LeftHold;
        set => SetHold(EarbudSide.Left, value);
    }

    /// <summary>
    /// Right press and hold: 0 Toggle Active Noise Control, 1 Digital assistant.
    /// </summary>
    public int RightHoldIndex
    {
        get => (int)_shown.RightHold;
        set => SetHold(EarbudSide.Right, value);
    }

    public bool CycleNoiseCancellation
    {
        get => _shown.NoiseCycle.NoiseCancellation;
        set => SetCycle(_shown.NoiseCycle with { NoiseCancellation = value });
    }

    public bool CycleOff
    {
        get => _shown.NoiseCycle.Off;
        set => SetCycle(_shown.NoiseCycle with { Off = value });
    }

    public bool CycleAdaptive
    {
        get => _shown.NoiseCycle.Adaptive;
        set => SetCycle(_shown.NoiseCycle with { Adaptive = value });
    }

    public bool CycleTransparency
    {
        get => _shown.NoiseCycle.Transparency;
        set => SetCycle(_shown.NoiseCycle with { Transparency = value });
    }

    // Press and hold needs two modes to switch between, so the last two can't be unchecked
    public bool CanToggleCycleNoiseCancellation => !CycleNoiseCancellation || _shown.NoiseCycle.Count > 2;

    public bool CanToggleCycleOff => !CycleOff || _shown.NoiseCycle.Count > 2;

    public bool CanToggleCycleAdaptive => !CycleAdaptive || _shown.NoiseCycle.Count > 2;

    public bool CanToggleCycleTransparency => !CycleTransparency || _shown.NoiseCycle.Count > 2;

    // =========================================================================
    // ERRORS
    // =========================================================================

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
                OnPropertyChanged(nameof(PickerStatusText));
            }
        }
    }

    public bool HasError => _errorMessage is not null;

    // =========================================================================
    // METHODS
    // =========================================================================

    /// <summary>
    /// Switches the buds to <paramref name="mode"/>.
    /// </summary>
    /// <param name="resend">
    /// Sends a mode that's already on again, so the buds still play their tone (shortcuts use
    /// this so a press never goes unanswered). A resend that fails only gets logged, since
    /// nothing changed.
    /// </param>
    public void SelectNoiseMode(NoiseMode mode, bool resend = false)
    {
        if (mode != _shown.NoiseMode)
        {
            Change(_shown with { NoiseMode = mode }, () => _headset.SetNoiseModeAsync(mode), "noise control");
            return;
        }
        if (resend)
        {
            _ = RunCommandAsync(() => _headset.SetNoiseModeAsync(mode), "noise control", showError: false);
        }
    }

    /// <summary>
    /// Settings changed outside this view model (the manager's Reconnect turns auto-connect back on).
    /// </summary>
    internal void RaiseAutoConnectChanged()
    {
        OnPropertyChanged(nameof(AutoConnect));
        OnPropertyChanged(nameof(IsReleased));
        OnPropertyChanged(nameof(IsAvailable));
    }

    internal void UpdateConnectionState(HeadsetConnectionState state)
    {
        // Windows connection and name changes arrive on the same event
        OnPropertyChanged(nameof(DeviceName));
        OnPropertyChanged(nameof(IsWindowsConnected));
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(ShowBattery));
        OnPropertyChanged(nameof(ShowReconnect));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(PickerStatusText));
        OnPropertyChanged(nameof(DisconnectedMessage));
        if (!SetProperty(ref _connectionState, state, nameof(ConnectionState)))
        {
            return;
        }
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsConnecting));
        OnPropertyChanged(nameof(ShowStatus));
        OnPropertyChanged(nameof(ShowDisconnectedWarning));
        if (state == HeadsetConnectionState.Connected)
        {
            ApplySnapshot(_headset.State);
        }
    }

    public void Dispose()
    {
        _headset.StateChanged -= OnHeadsetStateChanged;
        _equalizerThrottler.Dispose();
        _errorTimer?.Dispose();
    }

    private static string Percent(int? level) => level is null ? NoValue : $"{level}%";

    // Sliders land on the buds' 0.5 dB steps
    private static double Snap(double value) =>
        Math.Clamp(Math.Round(value / EqualizerBands.Step) * EqualizerBands.Step, EqualizerBands.Minimum, EqualizerBands.Maximum);

    private void OnHeadsetStateChanged(object? sender, HeadsetSnapshot snapshot) => _ui.Post(() => ApplySnapshot(snapshot));

    private void ApplySnapshot(HeadsetSnapshot snapshot)
    {
        // Nothing reports while disconnected, and the case only while a bud sits in it; show the
        // last level each part reported then, even from an earlier run, like the phone does
        var battery = snapshot.Battery;
        snapshot = snapshot with
        {
            Battery = battery with
            {
                Left  = Remember("Left", battery.Left),
                Right = Remember("Right", battery.Right),
                Case  = Remember("Case", battery.Case),
            },
        };

        // Battery always follows the buds; everything else waits for commands in flight
        var holding = _equalizerThrottler.HasPending || Volatile.Read(ref _commandsInFlight) > 0;
        Show(holding ? _shown with { Battery = snapshot.Battery, Firmware = snapshot.Firmware } : snapshot);
        // Only what the buds just reported, so a remembered level never raises a low-battery alert
        _lowBattery.Update(Id, DeviceName, battery);
    }

    // Saves a reported level, or fills in the last one saved when there's none
    private int? Remember(string part, int? level)
    {
        if (level is { } reported)
        {
            _settings.SetLastBattery(Id, part, reported);
            return reported;
        }

        return _settings.GetLastBattery(Id, part);
    }

    private void Show(HeadsetSnapshot snapshot)
    {
        _shown = snapshot;

        // Every control reads from _shown, so all of them refresh
        OnPropertyChanged(string.Empty);
    }

    private void Change(HeadsetSnapshot shown, Func<Task> command, string setting)
    {
        if (shown == _shown)
        {
            return;
        }
        Show(shown);
        _ = RunCommandAsync(command, setting);
    }

    private void SetEqualizer(EqualizerBands bands)
    {
        if (bands == _shown.Equalizer)
        {
            return;
        }
        Show(_shown with { Equalizer = bands });
        _equalizerThrottler.Run(() => RunCommandAsync(() => _headset.SetEqualizerAsync(bands), "equalizer"));
    }

    private void SetHold(EarbudSide side, int index)
    {
        if (index is < 0 or > 1)
        {
            return;
        }
        var action = (HoldAction)index;
        var shown = side == EarbudSide.Left ? _shown with { LeftHold = action } : _shown with { RightHold = action };
        Change(shown, () => _headset.SetHoldActionAsync(side, action), "press and hold");
    }

    private void SetCycle(NoiseModeCycle cycle)
    {
        if (cycle.Count < 2)
        {
            // Put the checkbox back
            OnPropertyChanged(string.Empty);
            return;
        }
        Change(_shown with { NoiseCycle = cycle }, () => _headset.SetNoiseModeCycleAsync(cycle), "noise control modes");
    }

    // Once the last command finishes, the controls take the buds' confirmed state
    private async Task RunCommandAsync(Func<Task> command, string setting, bool showError = true)
    {
        Interlocked.Increment(ref _commandsInFlight);
        try
        {
            await command().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogMessages.CommandFailed(_logger, ex, setting, DeviceName);
            if (showError)
            {
                _ui.Post(() => ShowError(HeadsetErrorMessages.Describe(ex)));
            }
        }
        finally
        {
            if (Interlocked.Decrement(ref _commandsInFlight) == 0)
            {
                _ui.Post(() => ApplySnapshot(_headset.State));
            }
        }
    }

    private void ShowError(string message)
    {
        ErrorMessage = message;
        _errorTimer?.Dispose();
        _errorTimer = _timeProvider.CreateTimer(_ => _ui.Post(() => ErrorMessage = null), null, ErrorDuration, Timeout.InfiniteTimeSpan);
    }
}
