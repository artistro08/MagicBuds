using MagicBuds.Presentation.Headsets;
using MagicBuds.Presentation.Settings;
using MagicBuds.Presentation.Shortcuts;

namespace MagicBuds.Presentation.Tests;

[TestClass]
public sealed class ShortcutTests
{
    [TestMethod]
    public void EveryDefaultParsesAndMapsToItsMode()
    {
        CollectionAssert.AreEqual(
            new[] { "Win+F10", "Win+F11", "Win+F12", "Win+F6" },
            ShortcutActions.All.Select(ShortcutActions.DefaultFor).ToArray());

        foreach (var action in ShortcutActions.All)
        {
            Assert.IsTrue(Hotkey.TryParse(ShortcutActions.DefaultFor(action), out var hotkey), action.ToString());
            Assert.AreEqual(ShortcutActions.DefaultFor(action), hotkey.ToString());
        }

        Assert.AreEqual(NoiseMode.Adaptive, ShortcutActions.NoiseModeFor(ShortcutAction.Adaptive));
        Assert.AreEqual(NoiseMode.Off, ShortcutActions.NoiseModeFor(ShortcutAction.Off));
    }

    [TestMethod]
    public void F12IsAllowedWithAModifier()
    {
        Assert.IsTrue(Hotkey.TryParse("win+f12", out var hotkey));
        Assert.AreEqual(0x7B, hotkey.Key);
        Assert.IsTrue(HotkeySettings.FromHotkey(hotkey).IsValid());
        Assert.IsFalse(Hotkey.TryParse("F12", out _));
    }

    [TestMethod]
    public void ShortcutsUseTheDefaultUntilChangedAndStayClearedOnceCleared()
    {
        var settings = new AppSettings(new InMemorySettingsStore());
        Assert.AreEqual("Win+F10", settings.GetShortcut(ShortcutAction.NoiseCancellation));

        settings.SetShortcut(ShortcutAction.NoiseCancellation, "Ctrl+Alt+N");
        Assert.AreEqual("Ctrl+Alt+N", settings.GetShortcut(ShortcutAction.NoiseCancellation));

        settings.SetShortcut(ShortcutAction.NoiseCancellation, "");
        Assert.AreEqual("", settings.GetShortcut(ShortcutAction.NoiseCancellation));
    }

    [TestMethod]
    public void BudsColorDefaultsToPorcelain()
    {
        var settings = new AppSettings(new InMemorySettingsStore());
        Assert.AreEqual("Porcelain", settings.GetBudsColor("AC:80:0A:00:00:06"));

        settings.SetBudsColor("AC:80:0A:00:00:06", "Pink");
        Assert.AreEqual("Pink", settings.GetBudsColor("AC:80:0A:00:00:06"));
        Assert.AreEqual("Porcelain", settings.GetBudsColor("AC:80:0A:00:00:07"));
    }
}
