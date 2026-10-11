using MagicBuds.Presentation.Headsets;

namespace MagicBuds.Presentation.Tests.Fakes;

/// <summary>
/// Scriptable buds: records commands, applies them to <see cref="State"/>, and can fail or
/// hold them.
/// </summary>
internal sealed class FakeHeadset : IHeadset
{
    private readonly Lock _gate = new();

    public event EventHandler<HeadsetSnapshot>? StateChanged;

    public event EventHandler? Disconnected;

    public HeadsetSnapshot State { get; set; } = HeadsetSnapshot.Empty with
    {
        Battery = new BatteryLevels(null, 75, 70, 100, false),
    };

    public Queue<Exception> ConnectFailures { get; } = new();

    public int ConnectAttempts { get; private set; }

    public int DisconnectCalls { get; private set; }

    public bool IsDisposed { get; private set; }

    public Exception? FailNextCommandWith { get; set; }

    public List<object> Commands { get; } = [];

    /// <summary>
    /// When set, the next connect attempt waits on this until the test completes it.
    /// </summary>
    public TaskCompletionSource? ConnectGate { get; set; }

    /// <summary>
    /// When true, commands stay in flight until <see cref="CompleteHeldCommands"/>.
    /// </summary>
    public bool HoldCommands { get; set; }

    private List<TaskCompletionSource> HeldCommands { get; } = [];

    public Task ConnectAsync(string bluetoothAddress)
    {
        lock (_gate)
        {
            ConnectAttempts++;
            if (ConnectGate is { } gate)
            {
                ConnectGate = null;
                return gate.Task;
            }
            if (ConnectFailures.TryDequeue(out var failure))
            {
                return Task.FromException(failure);
            }
        }
        return Task.CompletedTask;
    }

    public void CompleteHeldCommands()
    {
        List<TaskCompletionSource> held;
        lock (_gate)
        {
            held = [.. HeldCommands];
            HeldCommands.Clear();
        }
        foreach (var command in held)
        {
            command.SetResult();
        }
    }

    /// <summary>
    /// Raises StateChanged without changing <see cref="State"/>, like buds echoing an older
    /// setting while a newer one is still in flight.
    /// </summary>
    public void RaiseEcho(HeadsetSnapshot snapshot) => StateChanged?.Invoke(this, snapshot);

    public void Disconnect() => DisconnectCalls++;

    public Task SetNoiseModeAsync(NoiseMode mode) => Command(mode, state => state with { NoiseMode = mode });

    public Task SetConversationDetectionAsync(bool enabled) =>
        Command(("conversationDetection", enabled), state => state with { ConversationDetection = enabled });

    public Task SetMultipointAsync(bool enabled) => Command(("multipoint", enabled), state => state with { Multipoint = enabled });

    public Task SetEqualizerAsync(EqualizerBands bands) => Command(bands, state => state with { Equalizer = bands });

    public Task SetTouchControlsAsync(bool enabled) => Command(("touchControls", enabled), state => state with { TouchControls = enabled });

    public Task SetHeadGesturesAsync(bool enabled) => Command(("headGestures", enabled), state => state with { HeadGestures = enabled });

    public Task SetHoldActionAsync(EarbudSide side, HoldAction action) => Command(
        (side, action),
        state => side == EarbudSide.Left ? state with { LeftHold = action } : state with { RightHold = action });

    public Task SetNoiseModeCycleAsync(NoiseModeCycle cycle) => Command(cycle, state => state with { NoiseCycle = cycle });

    public void RaiseStateChanged(HeadsetSnapshot snapshot)
    {
        State = snapshot;
        StateChanged?.Invoke(this, snapshot);
    }

    public void RaiseDisconnected() => Disconnected?.Invoke(this, EventArgs.Empty);

    public void Dispose() => IsDisposed = true;

    private Task Command(object command, Func<HeadsetSnapshot, HeadsetSnapshot> apply)
    {
        lock (_gate)
        {
            Commands.Add(command);
            if (FailNextCommandWith is { } failure)
            {
                FailNextCommandWith = null;
                return Task.FromException(failure);
            }
            State = apply(State);
            if (HoldCommands)
            {
                var held = new TaskCompletionSource();
                HeldCommands.Add(held);
                return held.Task;
            }
        }
        return Task.CompletedTask;
    }
}
