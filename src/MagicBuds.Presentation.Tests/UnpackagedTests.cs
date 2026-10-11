using Microsoft.Win32;
using MagicBuds.Presentation.Settings;

namespace MagicBuds.Presentation.Tests;

/// <summary>
/// The pieces the classic (MSI) install uses in place of the MSIX package's settings store and
/// startup task.
/// </summary>
[TestClass]
public sealed class JsonFileSettingsStoreTests
{
    private string _folder = null!;

    [TestInitialize]
    public void CreateFolder() => _folder = Directory.CreateTempSubdirectory("MagicBudsTests").FullName;

    [TestCleanup]
    public void DeleteFolder() => Directory.Delete(_folder, true);

    [TestMethod]
    public void ValuesSurviveANewStore()
    {
        var path = Path.Combine(_folder, "settings.json");
        new JsonFileSettingsStore(path).SetString("Theme", "Dark");

        Assert.AreEqual("Dark", new JsonFileSettingsStore(path).GetString("Theme"));
    }

    [TestMethod]
    public void NullRemovesTheValue()
    {
        var path = Path.Combine(_folder, "settings.json");
        var store = new JsonFileSettingsStore(path);
        store.SetString("Theme", "Dark");

        store.SetString("Theme", null);

        Assert.IsNull(new JsonFileSettingsStore(path).GetString("Theme"));
    }

    [TestMethod]
    public void CorruptFileStartsEmpty()
    {
        var path = Path.Combine(_folder, "settings.json");
        File.WriteAllText(path, "{ not json");

        Assert.IsNull(new JsonFileSettingsStore(path).GetString("Theme"));
    }

    [TestMethod]
    public void CreatesItsFolder()
    {
        var path = Path.Combine(_folder, "nested", "settings.json");

        new JsonFileSettingsStore(path).SetString("Theme", "Light");

        Assert.IsTrue(File.Exists(path));
    }
}

[TestClass]
public sealed class RegistryStartupServiceTests
{
    // A scratch key under the current user, standing in for ...\CurrentVersion\Run
    private const string TestKey = @"Software\MagicBudsTests\Run";

    [TestCleanup]
    public void DeleteKey() => Registry.CurrentUser.DeleteSubKeyTree(@"Software\MagicBudsTests", false);

    [TestMethod]
    public async Task EnableWritesTheCommandAndDisableRemovesIt()
    {
        var service = new RegistryStartupService(@"C:\Apps\MagicBuds.exe", TestKey);

        Assert.IsTrue(await service.SetEnabledAsync(true));
        Assert.IsTrue(await service.IsEnabledAsync());
        using (var key = Registry.CurrentUser.OpenSubKey(TestKey))
        {
            Assert.AreEqual("\"C:\\Apps\\MagicBuds.exe\"",key?.GetValue(RegistryStartupService.ValueName));
        }

        Assert.IsFalse(await service.SetEnabledAsync(false));
        Assert.IsFalse(await service.IsEnabledAsync());
    }
}
