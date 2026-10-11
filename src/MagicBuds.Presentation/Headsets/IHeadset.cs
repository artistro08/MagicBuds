namespace MagicBuds.Presentation.Headsets;

/// <summary>
/// The control link to one pair of Pixel Buds.
/// </summary>
/// <remarks>
/// Every command completes once the buds acknowledge it, and <see cref="State"/> only changes
/// after that, so callers can revert their UI to <see cref="State"/> when a command fails.
/// Events are raised on a background thread.
/// </remarks>
public interface IHeadset : IDisposable
{
    HeadsetSnapshot State { get; }

    event EventHandler<HeadsetSnapshot>? StateChanged;

    event EventHandler? Disconnected;

    Task ConnectAsync(string bluetoothAddress);

    void Disconnect();

    Task SetNoiseModeAsync(NoiseMode mode);

    Task SetConversationDetectionAsync(bool enabled);

    Task SetMultipointAsync(bool enabled);

    Task SetEqualizerAsync(EqualizerBands bands);

    Task SetTouchControlsAsync(bool enabled);

    Task SetHeadGesturesAsync(bool enabled);

    Task SetHoldActionAsync(EarbudSide side, HoldAction action);

    Task SetNoiseModeCycleAsync(NoiseModeCycle cycle);
}
