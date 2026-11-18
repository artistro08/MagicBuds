using MagicBuds.Presentation.Headsets;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;

namespace MagicBuds.Presentation.Devices;

/// <summary>
/// <see cref="IBluetoothDeviceSource"/> over a Windows <see cref="DeviceWatcher"/> for paired
/// Bluetooth Classic devices.
/// </summary>
/// <remarks>
/// Devices whose name doesn't say Pixel Buds are checked for the Maestro control service in
/// Windows' cached service list, so buds renamed before the app first saw them are still found.
/// </remarks>
public sealed class BluetoothDeviceWatcher : IBluetoothDeviceSource, IDisposable
{
    private const string IsConnectedProperty = "System.Devices.Aep.IsConnected";
    private const string AddressProperty = "System.Devices.Aep.DeviceAddress";
    private const string NameProperty = "System.ItemNameDisplay";

    private readonly Lock _gate = new();
    private readonly Dictionary<string, BluetoothDeviceInfo> _devices = [];
    private readonly List<Task> _serviceChecks = [];

    // Keeps a service check's report and a Windows update from arriving out of order
    private readonly Lock _raiseGate = new();
    private DeviceWatcher? _watcher;

    public event EventHandler<BluetoothDeviceInfo>? DeviceChanged;

    public event EventHandler<string>? DeviceRemoved;

    public event EventHandler? EnumerationCompleted;

    public void StartWatching()
    {
        if (_watcher is not null)
        {
            return;
        }

        _watcher = DeviceInformation.CreateWatcher(
            BluetoothDevice.GetDeviceSelectorFromPairingState(true),
            [IsConnectedProperty, AddressProperty, NameProperty],
            DeviceInformationKind.AssociationEndpoint);
        _watcher.Added += OnAdded;
        _watcher.Updated += OnUpdated;
        _watcher.Removed += OnRemoved;
        _watcher.EnumerationCompleted += OnEnumerationCompleted;
        _watcher.Stopped += OnStopped;
        _watcher.Start();
    }

    public void StopWatching()
    {
        if (_watcher is null)
        {
            return;
        }

        _watcher.Added -= OnAdded;
        _watcher.Updated -= OnUpdated;
        _watcher.Removed -= OnRemoved;
        _watcher.EnumerationCompleted -= OnEnumerationCompleted;
        _watcher.Stopped -= OnStopped;
        if (_watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
        {
            _watcher.Stop();
        }
        _watcher = null;
    }

    public void Dispose() => StopWatching();

    private static bool ReadBool(IReadOnlyDictionary<string, object> properties, string key) =>
        properties.TryGetValue(key, out var value) && value is bool flag && flag;

    private static string ReadString(IReadOnlyDictionary<string, object> properties, string key) =>
        properties.TryGetValue(key, out var value) && value is string text ? text : "";

    private void OnAdded(DeviceWatcher sender, DeviceInformation info)
    {
        var device = new BluetoothDeviceInfo(
            info.Id,
            info.Name,
            ReadString(info.Properties, AddressProperty),
            ReadBool(info.Properties, IsConnectedProperty));

        lock (_gate)
        {
            _devices[info.Id] = device;
        }
        DeviceChanged?.Invoke(this, device);

        // Started outside the lock: a check that finishes at once reports straight away
        if (!PixelBudsNameFilter.IsPixelBuds(device.Name))
        {
            var check = CheckForMaestroAsync(info.Id);
            lock (_gate)
            {
                _serviceChecks.RemoveAll(task => task.IsCompleted);
                _serviceChecks.Add(check);
            }
        }
    }

    // Reports the device again once Windows' cached service list shows it's Pixel Buds.
    // ponytail: cached list only, so buds Windows never read the services of stay unrecognized
    private async Task CheckForMaestroAsync(string deviceId)
    {
        try
        {
            using var bluetooth = await BluetoothDevice.FromIdAsync(deviceId);
            if (bluetooth is null)
            {
                return;
            }
            var services = await bluetooth.GetRfcommServicesForIdAsync(
                RfcommServiceId.FromUuid(PixelBudsHeadset.MaestroServiceId),
                BluetoothCacheMode.Cached);
            if (services.Services.Count == 0)
            {
                return;
            }
        }
        catch (Exception)
        {
            // Not reachable or not ours: treat it like any other device
            return;
        }

        lock (_raiseGate)
        {
            BluetoothDeviceInfo? device;
            lock (_gate)
            {
                if (!_devices.TryGetValue(deviceId, out device))
                {
                    return;
                }
                device = device with { OffersMaestro = true };
                _devices[deviceId] = device;
            }
            DeviceChanged?.Invoke(this, device);
        }
    }

    // Done listing once every service check started during the listing has finished too
    private async void RaiseEnumerationCompleted()
    {
        Task[] checks;
        lock (_gate)
        {
            checks = [.. _serviceChecks];
            _serviceChecks.Clear();
        }
        // A failed check must not crash the app or hold up the listing
        await Task.WhenAll(checks).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        EnumerationCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void OnUpdated(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        lock (_raiseGate)
        {
            BluetoothDeviceInfo? device;
            lock (_gate)
            {
                if (!_devices.TryGetValue(update.Id, out device))
                {
                    return;
                }
                if (update.Properties.ContainsKey(IsConnectedProperty))
                {
                    device = device with { IsConnected = ReadBool(update.Properties, IsConnectedProperty) };
                }

                // Renamed in Windows' Bluetooth settings
                if (ReadString(update.Properties, NameProperty) is { Length: > 0 } name)
                {
                    device = device with { Name = name };
                }
                _devices[update.Id] = device;
            }
            DeviceChanged?.Invoke(this, device);
        }
    }

    private void OnEnumerationCompleted(DeviceWatcher sender, object args) => RaiseEnumerationCompleted();

    // A watcher that stops or fails before finishing has listed all it ever will
    private void OnStopped(DeviceWatcher sender, object args) => RaiseEnumerationCompleted();

    private void OnRemoved(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        lock (_gate)
        {
            if (!_devices.Remove(update.Id))
            {
                return;
            }
        }
        DeviceRemoved?.Invoke(this, update.Id);
    }
}
