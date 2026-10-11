using MagicBuds.Presentation.Common;
using MagicBuds.Presentation.Devices;
using MagicBuds.Presentation.Headsets;
using MagicBuds.Presentation.Maestro;

namespace MagicBuds.Presentation.Tests;

[TestClass]
public sealed class PixelBudsNameFilterTests
{
    [TestMethod]
    [DataRow("Pixel Buds Pro 2")]
    [DataRow("Sam's Pixel Buds Pro 2")]
    [DataRow("pixel buds a-series")]
    public void AcceptsPixelBudsNames(string name) => Assert.IsTrue(PixelBudsNameFilter.IsPixelBuds(name));

    [TestMethod]
    [DataRow("WF-1000XM6")]
    [DataRow("Galaxy Buds2 Pro")]
    [DataRow("")]
    [DataRow(null)]
    public void RejectsOtherNames(string? name) => Assert.IsFalse(PixelBudsNameFilter.IsPixelBuds(name));
}

[TestClass]
public sealed class HeadsetErrorMessagesTests
{
    [TestMethod]
    public void DescribesTimeout() =>
        Assert.AreEqual("Your buds didn't respond. Try again.", HeadsetErrorMessages.Describe(new TimeoutException()));

    [TestMethod]
    public void DescribesRefusedSetting() =>
        Assert.AreEqual("Your buds didn't accept that setting.", HeadsetErrorMessages.Describe(new MaestroException(9, "WriteSetting")));

    [TestMethod]
    public void DescribesDisconnect() =>
        Assert.AreEqual("Your buds disconnected.", HeadsetErrorMessages.Describe(new IOException()));

    [TestMethod]
    public void FallsBackForUnknownErrors() =>
        Assert.AreEqual("Something went wrong talking to your buds.", HeadsetErrorMessages.Describe(new InvalidOperationException()));
}

[TestClass]
public sealed class GlyphsTests
{
    [TestMethod]
    public void PicksBatteryStepByTens()
    {
        Assert.AreEqual("", Glyphs.Battery(5));
        Assert.AreEqual("", Glyphs.Battery(85));
        Assert.AreEqual("", Glyphs.Battery(100));
        Assert.AreEqual(Glyphs.BatteryUnknown, Glyphs.Battery(null));
    }
}

[TestClass]
public sealed class BatteryLevelsTests
{
    [TestMethod]
    public void LowestIgnoresCaseAndUnknownCells()
    {
        Assert.AreEqual(12, new BatteryLevels(null, 12, 40, 5, false).Lowest);
        Assert.AreEqual(60, new BatteryLevels(60, null, null, null, false).Lowest);
        Assert.IsNull(BatteryLevels.Unknown.Lowest);
    }
}
