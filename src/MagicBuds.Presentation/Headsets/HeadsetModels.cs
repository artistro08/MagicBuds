namespace MagicBuds.Presentation.Headsets;

/// <summary>
/// Active Noise Control mode on the buds.
/// </summary>
public enum NoiseMode
{
    NoiseCancellation,
    Off,
    Adaptive,
    Transparency,
}

/// <summary>
/// What pressing and holding an earbud does.
/// </summary>
public enum HoldAction
{
    ToggleNoiseControl,
    DigitalAssistant,
}

/// <summary>
/// Left or right earbud.
/// </summary>
public enum EarbudSide
{
    Left,
    Right,
}

/// <summary>
/// State of the app's Bluetooth control link, separate from the Windows audio connection.
/// </summary>
public enum HeadsetConnectionState
{
    Disconnected,
    Connecting,
    Connected,
}

/// <summary>
/// Battery levels in percent. Null means the buds didn't report that cell. Main is always null
/// on earbuds; it's kept so the shared battery views read one shape.
/// </summary>
public sealed record BatteryLevels(int? Main, int? Left, int? Right, int? Case, bool Charging)
{
    public static BatteryLevels Unknown { get; } = new(null, null, null, null, false);

    /// <summary>
    /// Lowest level across the earbuds (not the case). Null when none are known.
    /// </summary>
    public int? Lowest => new[] { Main, Left, Right }.Min();
}

/// <summary>
/// Noise modes that press and hold cycles through. The buds need at least two.
/// </summary>
public sealed record NoiseModeCycle(bool NoiseCancellation, bool Off, bool Adaptive, bool Transparency)
{
    public int Count => new[] { NoiseCancellation, Off, Adaptive, Transparency }.Count(on => on);
}

/// <summary>
/// Equalizer gains in dB, -6 to 6 in steps of 0.5.
/// </summary>
public sealed record EqualizerBands(double UpperTreble, double Treble, double Mid, double Bass, double LowBass)
{
    public const double Minimum = -6;
    public const double Maximum = 6;
    public const double Step    = 0.5;

    public static EqualizerBands Flat { get; } = new(0, 0, 0, 0, 0);
}

/// <summary>
/// An equalizer preset. Presets live in the app, like on mypixelbuds.google.com; picking one
/// writes its bands to the buds.
/// </summary>
public sealed record EqualizerPreset(string Name, EqualizerBands Bands)
{
    /// <summary>
    /// The web app's presets, in its order.
    /// </summary>
    public static IReadOnlyList<EqualizerPreset> All { get; } =
    [
        new("Default", EqualizerBands.Flat),
        new("Heavy bass", new(0, 0, 0, 3, 5)),
        new("Light bass", new(0, 0, 0, -1.5, -5)),
        new("Balanced", new(2.5, -1, 1, 0.5, -3.5)),
        new("Vocal boost", new(0, 2, 4, 0, -1)),
        new("Clarity", new(5, 3, 2, 0, -2)),
    ];
}

/// <summary>
/// Everything the buds last confirmed. <see cref="NoiseMode"/> is null while the buds report
/// none (seen while they're out of the ears).
/// </summary>
public sealed record HeadsetSnapshot(
    BatteryLevels Battery,
    NoiseMode? NoiseMode,
    bool ConversationDetection,
    bool Multipoint,
    EqualizerBands Equalizer,
    bool TouchControls,
    bool HeadGestures,
    HoldAction LeftHold,
    HoldAction RightHold,
    NoiseModeCycle NoiseCycle,
    string Firmware)
{
    public static HeadsetSnapshot Empty { get; } = new(
        BatteryLevels.Unknown,
        null,
        false,
        false,
        EqualizerBands.Flat,
        true,
        false,
        HoldAction.ToggleNoiseControl,
        HoldAction.ToggleNoiseControl,
        new NoiseModeCycle(true, false, false, true),
        "");
}
