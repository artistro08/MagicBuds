using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using MagicBuds.Presentation.Devices;
using MagicBuds.Presentation.Headsets;
using MagicBuds.Presentation.Notifications;
using MagicBuds.Presentation.Settings;
using MagicBuds.Presentation.Tests.Fakes;
using MagicBuds.Presentation.ViewModels;

namespace MagicBuds.Presentation.Tests;

[TestClass]
public sealed class HeadsetViewModelTests
{
    private readonly FakeHeadset _headset = new();
    private readonly FakeTimeProvider _time = new();
    private readonly AppSettings _settings = new(new InMemorySettingsStore());
    private readonly FakeNotificationService _notifications = new();
    private HeadsetViewModel _viewModel = null!;

    [TestInitialize]
    public void CreateViewModel()
    {
        var managed = new ManagedHeadset("device-buds", "AC:80:0A:00:00:06", "Pixel Buds Pro 2", _headset)
        {
            IsWindowsConnected = true,
            ConnectionState    = HeadsetConnectionState.Connected,
        };
        _viewModel = new HeadsetViewModel(
            managed,
            _settings,
            new LowBatteryMonitor(_settings, _notifications),
            _time,
            NullLogger.Instance);
    }

    [TestCleanup]
    public void DisposeViewModel() => _viewModel.Dispose();

    [TestMethod]
    public void ShowsTheBudsBattery()
    {
        Assert.AreEqual("75%", _viewModel.LeftBatteryText);
        Assert.AreEqual("70%", _viewModel.RightBatteryText);
        Assert.AreEqual("100%", _viewModel.CaseBatteryText);
        Assert.IsTrue(_viewModel.ShowDualBattery);
    }

    [TestMethod]
    public void CaseKeepsItsLastLevelWhileNoBudIsInIt()
    {
        _headset.RaiseStateChanged(_headset.State with { Battery = new BatteryLevels(null, 75, 70, null, false) });

        Assert.AreEqual("100%", _viewModel.CaseBatteryText);
        Assert.AreEqual(100, _settings.GetLastCaseBattery(_viewModel.Id));
    }

    [TestMethod]
    public void NoiseModeTileSendsTheMode()
    {
        _viewModel.SetNoiseModeCommand.Execute("Adaptive");

        Assert.IsTrue(_viewModel.IsAdaptive);
        CollectionAssert.AreEqual(new object[] { NoiseMode.Adaptive }, _headset.Commands);
    }

    [TestMethod]
    public void FailedCommandPutsTheControlBackAndShowsWhy()
    {
        _headset.FailNextCommandWith = new TimeoutException();

        _viewModel.Multipoint = true;

        Assert.IsFalse(_viewModel.Multipoint);
        Assert.AreEqual("Your buds didn't respond. Try again.", _viewModel.ErrorMessage);

        _time.Advance(HeadsetViewModel.ErrorDuration);
        Assert.IsNull(_viewModel.ErrorMessage);
    }

    [TestMethod]
    public void EchoOfAnOlderModeDoesNotMoveTheTilesWhileACommandIsInFlight()
    {
        _headset.HoldCommands = true;
        _viewModel.SelectNoiseMode(NoiseMode.Transparency);

        _headset.RaiseEcho(_headset.State with { NoiseMode = NoiseMode.Off });
        Assert.IsTrue(_viewModel.IsTransparency);

        _headset.CompleteHeldCommands();
        Assert.IsTrue(_viewModel.IsTransparency);
    }

    [TestMethod]
    public void PickingAPresetWritesItsBands()
    {
        _viewModel.SelectedPresetIndex = 5;

        Assert.AreEqual(EqualizerPreset.All[5].Bands, _headset.State.Equalizer);
        Assert.AreEqual(5, _viewModel.UpperTreble);
        Assert.AreEqual(5, _viewModel.SelectedPresetIndex);
    }

    [TestMethod]
    public void MovingASliderSnapsToHalfStepsAndBecomesCustom()
    {
        _viewModel.Bass = 1.3;

        Assert.AreEqual(1.5, _viewModel.Bass);
        Assert.AreEqual(-1, _viewModel.SelectedPresetIndex);
        Assert.AreEqual(1.5, _headset.State.Equalizer.Bass);
    }

    [TestMethod]
    public void SliderDragSendsAtMostOneCommandPerInterval()
    {
        _viewModel.Mid = 1;
        _viewModel.Mid = 2;
        _viewModel.Mid = 3;

        Assert.AreEqual(1, _headset.Commands.Count);

        _time.Advance(HeadsetViewModel.SliderInterval);
        Assert.AreEqual(2, _headset.Commands.Count);
        Assert.AreEqual(3, _headset.State.Equalizer.Mid);
    }

    [TestMethod]
    public void HoldActionGoesToThatEarbudOnly()
    {
        _viewModel.RightHoldIndex = 1;

        CollectionAssert.AreEqual(new object[] { (EarbudSide.Right, HoldAction.DigitalAssistant) }, _headset.Commands);
        Assert.AreEqual(0, _viewModel.LeftHoldIndex);
    }

    [TestMethod]
    public void LastTwoCycleModesCannotBeUnchecked()
    {
        // Starts with Noise Cancellation and Transparency
        Assert.IsFalse(_viewModel.CanToggleCycleTransparency);
        Assert.IsTrue(_viewModel.CanToggleCycleOff);

        _viewModel.CycleTransparency = false;

        Assert.IsTrue(_viewModel.CycleTransparency);
        Assert.AreEqual(0, _headset.Commands.Count);
    }

    [TestMethod]
    public void LowBatteryNotifiesOnce()
    {
        _headset.RaiseStateChanged(_headset.State with { Battery = new BatteryLevels(null, 15, 70, 100, false) });
        _headset.RaiseStateChanged(_headset.State with { Battery = new BatteryLevels(null, 14, 70, 100, false) });

        Assert.AreEqual(1, _notifications.Shown.Count);
        Assert.AreEqual(15, _notifications.Shown[0].Level);
    }
}
