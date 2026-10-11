using System.Globalization;
using MagicBuds.Presentation.Shortcuts;

namespace MagicBuds.Presentation.Settings;

/// <summary>
/// App theme choice.
/// </summary>
public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>
/// Typed access to every app setting.
/// </summary>
public sealed class AppSettings
{
    private const string RememberedHeadsetKey = "RememberedHeadset";
    private const string LowBatteryNotificationsKey = "LowBatteryNotifications";
    private const string ThemeKey = "Theme";
    private const string DebugLoggingKey = "DebugLogging";
    private const string AutoConnectKeyPrefix = "AutoConnect.";
    private const string KnownHeadsetKeyPrefix = "KnownHeadset.";
    private const string ShortcutKeyPrefix = "Shortcut.";
    private const string BudsColorKeyPrefix = "BudsColor.";

    private readonly ISettingsStore _store;

    public AppSettings(ISettingsStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Headset the flyout reopens on, by <see cref="Devices.ManagedHeadset.Id"/>. Null shows the picker.
    /// </summary>
    public string? RememberedHeadsetId
    {
        get => _store.GetString(RememberedHeadsetKey);
        set => _store.SetString(RememberedHeadsetKey, value);
    }

    public bool LowBatteryNotifications
    {
        get => GetBool(LowBatteryNotificationsKey, true);
        set => SetBool(LowBatteryNotificationsKey, value);
    }

    public AppTheme Theme
    {
        get => Enum.TryParse<AppTheme>(_store.GetString(ThemeKey), out var theme) ? theme : AppTheme.System;
        set => _store.SetString(ThemeKey, value.ToString());
    }

    public bool DebugLogging
    {
        get => GetBool(DebugLoggingKey, false);
        set => SetBool(DebugLoggingKey, value);
    }

    /// <summary>
    /// Device name the buds had when first recognized as Pixel Buds, so they keep working after
    /// they're renamed in Windows. Null for addresses never seen.
    /// </summary>
    public string? GetKnownHeadsetName(string headsetId) => _store.GetString(KnownHeadsetKeyPrefix + headsetId);

    public void SetKnownHeadsetName(string headsetId, string name) => _store.SetString(KnownHeadsetKeyPrefix + headsetId, name);

    /// <summary>
    /// Last level a part of the headset reported: "Left", "Right" or "Case". The case only reports
    /// while an earbud sits in it, and nothing reports while disconnected, so this fills in the rest.
    /// </summary>
    public int? GetLastBattery(string headsetId, string part) =>
        int.TryParse(_store.GetString(LastBatteryKey(headsetId, part)), NumberStyles.Integer, CultureInfo.InvariantCulture, out var level) ? level : null;

    public void SetLastBattery(string headsetId, string part, int level) =>
        _store.SetString(LastBatteryKey(headsetId, part), level.ToString(CultureInfo.InvariantCulture));

    // "LastCaseBattery.<id>", the key the case level has always been saved under
    private static string LastBatteryKey(string headsetId, string part) => $"Last{part}Battery.{headsetId}";

    /// <summary>
    /// The combination for a global shortcut, like "Win+F10": the default until changed, "" once cleared.
    /// </summary>
    public string GetShortcut(ShortcutAction action) => _store.GetString(ShortcutKeyPrefix + action) ?? ShortcutActions.DefaultFor(action);

    public void SetShortcut(ShortcutAction action, string shortcut) => _store.SetString(ShortcutKeyPrefix + action, shortcut);

    /// <summary>
    /// A pair of buds' color, for their pictures: Porcelain, Green, Charcoal or Pink.
    /// </summary>
    public string GetBudsColor(string headsetId) => _store.GetString(BudsColorKeyPrefix + headsetId) ?? "Porcelain";

    public void SetBudsColor(string headsetId, string color) => _store.SetString(BudsColorKeyPrefix + headsetId, color);

    public bool IsAutoConnectEnabled(string headsetId) => GetBool(AutoConnectKeyPrefix + headsetId, true);

    public void SetAutoConnectEnabled(string headsetId, bool enabled) => SetBool(AutoConnectKeyPrefix + headsetId, enabled);

    private bool GetBool(string key, bool fallback) =>
        bool.TryParse(_store.GetString(key), out var value) ? value : fallback;

    private void SetBool(string key, bool value) => _store.SetString(key, value.ToString(CultureInfo.InvariantCulture));
}
