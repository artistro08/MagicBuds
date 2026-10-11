using MagicBuds.Presentation.Devices;
using MagicBuds.Presentation.Notifications;
using MagicBuds.Presentation.Settings;

namespace MagicBuds.Presentation.Tests.Fakes;

internal sealed class FakeDeviceSource : IBluetoothDeviceSource
{
    public event EventHandler<BluetoothDeviceInfo>? DeviceChanged;

    public event EventHandler<string>? DeviceRemoved;

    public bool IsWatching { get; private set; }

    public void StartWatching() => IsWatching = true;

    public void StopWatching() => IsWatching = false;

    public void Report(BluetoothDeviceInfo device) => DeviceChanged?.Invoke(this, device);

    public void Remove(string deviceId) => DeviceRemoved?.Invoke(this, deviceId);
}

internal sealed class FakeNotificationService : INotificationService
{
    public List<(string DeviceName, int Level)> Shown { get; } = [];

    public List<string> HeadsetIds { get; } = [];

    public void ShowLowBattery(string headsetId, string deviceName, int level)
    {
        HeadsetIds.Add(headsetId);
        Shown.Add((deviceName, level));
    }
}

internal sealed class FakeStartupTaskService : IStartupTaskService
{
    public bool Enabled { get; set; }

    /// <summary>
    /// When true, enabling is refused like a policy-disabled startup task.
    /// </summary>
    public bool RefuseEnable { get; set; }

    public Task<bool> IsEnabledAsync() => Task.FromResult(Enabled);

    public Task<bool> SetEnabledAsync(bool enabled)
    {
        Enabled = enabled && !RefuseEnable;
        return Task.FromResult(Enabled);
    }
}
