using MagicBuds.Presentation.Headsets;

namespace MagicBuds.Presentation.Shortcuts;

/// <summary>
/// What a global shortcut does: switch the buds to one noise mode. The values are the hotkey IDs registered with Windows.
/// </summary>
public enum ShortcutAction
{
    NoiseCancellation = 1,
    Adaptive = 2,
    Transparency = 3,
    Off = 4,
}

/// <summary>
/// The noise mode, name and default combination of each <see cref="ShortcutAction"/>.
/// </summary>
public static class ShortcutActions
{
    public static IReadOnlyList<ShortcutAction> All { get; } =
    [
        ShortcutAction.NoiseCancellation,
        ShortcutAction.Adaptive,
        ShortcutAction.Transparency,
        ShortcutAction.Off,
    ];

    public static NoiseMode NoiseModeFor(ShortcutAction action) => action switch
    {
        ShortcutAction.Adaptive     => NoiseMode.Adaptive,
        ShortcutAction.Transparency => NoiseMode.Transparency,
        ShortcutAction.Off          => NoiseMode.Off,
        _                           => NoiseMode.NoiseCancellation,
    };

    /// <summary>
    /// The setting's name, as Settings › Shortcuts and the picker's dialog title show it.
    /// </summary>
    public static string NameFor(ShortcutAction action) => action switch
    {
        ShortcutAction.Adaptive     => "Adaptive",
        ShortcutAction.Transparency => "Transparency",
        ShortcutAction.Off          => "Noise control off",
        _                           => "Noise Cancellation",
    };

    public static string DefaultFor(ShortcutAction action) => action switch
    {
        ShortcutAction.Adaptive     => "Win+F11",
        ShortcutAction.Transparency => "Win+F12",
        ShortcutAction.Off          => "Win+F6",
        _                           => "Win+F10",
    };
}
