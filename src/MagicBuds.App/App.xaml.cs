using System.Diagnostics;
using System.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using MagicBuds.App.Interop;
using MagicBuds.App.Views.Settings;
using MagicBuds.Presentation.Devices;
using MagicBuds.Presentation.Shortcuts;
using MagicBuds.Presentation.Headsets;
using MagicBuds.Presentation.Logging;
using MagicBuds.Presentation.Navigation;
using MagicBuds.Presentation.Notifications;
using MagicBuds.Presentation.Settings;
using MagicBuds.Presentation.ViewModels;

namespace MagicBuds.App;

/// <summary>
/// Tray app composition root. No window opens at launch; the tray icon opens the flyout.
/// </summary>
public partial class App : Application, IDisposable
{
    private ILoggerFactory? _loggerFactory;
    private ILogger _logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    private AppSettings? _settings;
    private AppNotificationService? _notifications;
    private HeadsetManager? _manager;
    private FlyoutViewModel? _flyout;
    private SettingsViewModel? _settingsViewModel;
    private FlyoutWindow? _flyoutWindow;
    private SettingsWindow? _settingsWindow;
    private TrayIcon? _trayIcon;
    private TrayMenuWindow? _trayMenu;
    private TrayTooltipWindow? _trayTooltip;
    private GlobalShortcuts? _shortcuts;
    private EventWaitHandle? _quitRequest;
    private RegisteredWaitHandle? _quitWait;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _trimTimer;

    public App()
    {
        InitializeComponent();

        // Closing the settings window must not end a tray app.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        UnhandledException += (_, e) => AppLog.Unhandled(_logger, e.Exception);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppIdentity.ClaimClassicAppId();

        // Logging
        var logFolder = Path.Combine(AppIdentity.DataFolder, "Logs");
        var levelSwitch = new LogLevelSwitch();
        _loggerFactory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Trace)
            .AddProvider(new RollingFileLoggerProvider(logFolder, levelSwitch, TimeProvider.System)));
        _logger = _loggerFactory.CreateLogger("MagicBuds");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => AppLog.Unhandled(_logger, e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.UnobservedTask(_logger, e.Exception);
            e.SetObserved();
        };

        // Services
        var time = TimeProvider.System;
        // MSIX keeps settings in its package store; the classic install uses a file
        ISettingsStore settingsStore = AppIdentity.IsPackaged
            ? new LocalSettingsStore()
            : new JsonFileSettingsStore(Path.Combine(AppIdentity.DataFolder, "settings.json"));
        _settings = new AppSettings(settingsStore);
        _notifications = new AppNotificationService(_loggerFactory.CreateLogger<AppNotificationService>());
        _notifications.Register();
        var lowBattery = new LowBatteryMonitor(_settings, _notifications);

        var maestroLogger = _loggerFactory.CreateLogger("MagicBuds.Maestro");
        _manager = new HeadsetManager(
            new BluetoothDeviceWatcher(),
            _ => new PixelBudsHeadset(maestroLogger),
            _settings,
            time,
            _loggerFactory.CreateLogger<HeadsetManager>())
        {
            WindowsConnectSettle = TimeSpan.FromSeconds(1.5),
        };

        // View Models
        var headsetLogger = _loggerFactory.CreateLogger<HeadsetViewModel>();
        _flyout = new FlyoutViewModel(
            _manager,
            new FlyoutNavigator(_settings),
            managed => new HeadsetViewModel(managed, _settings, lowBattery, time, headsetLogger));
        _flyout.SettingsRequested += (_, _) => ShowSettings();
        _flyout.EqualizerRequested += (_, _) => ShowSettings("Buds");
        _flyout.QuitRequested += (_, _) => Quit();

        _settingsViewModel = new SettingsViewModel(
            _flyout,
            _settings,
            levelSwitch,
            AppIdentity.IsPackaged ? new StartupTaskService() : new RegistryStartupService(Environment.ProcessPath!),
            ApplyTheme,
            OpenFolder,
            logFolder,
            _loggerFactory.CreateLogger<SettingsViewModel>());
        _settingsViewModel.ApplyLogLevel();

        // Tray Icon and Flyout
        _flyoutWindow = new FlyoutWindow(_flyout);
        _flyoutWindow.CloseRequested += (_, _) => _flyoutWindow.HideFlyout();

        _trayIcon = new TrayIcon("MagicBuds");
        _trayIcon.Invoked += (_, _) => _flyoutWindow.Toggle(_trayIcon.GetIconRect());
        _trayIcon.ContextMenuRequested += (_, point) => ShowTrayMenu(point.X, point.Y);

        // Memory Trim: once the flyout is hidden, hand what it used back to Windows
        _flyoutWindow.AppWindow.Changed += (sender, e) =>
        {
            if (e.DidVisibilityChange && !sender.IsVisible)
            {
                ScheduleMemoryTrim();
            }
        };

        // Notification Clicks open the flyout on the buds the notification was about
        _notifications.HeadsetClicked += (_, headsetId) => _flyoutWindow.DispatcherQueue.TryEnqueue(() =>
        {
            _flyout.ShowHeadset(headsetId);
            _flyoutWindow.Open(_trayIcon.GetIconRect());
        });

        // Hover Tooltip: connected buds and their batteries; not over the open flyout
        _trayTooltip = new TrayTooltipWindow(_flyout);
        _trayIcon.PopupOpening += (_, _) => ShowTrayTooltip();
        _trayIcon.PopupClosing += (_, _) => _trayTooltip.Hide();
        _trayIcon.Invoked += (_, _) => _trayTooltip.Hide();
        _trayIcon.ContextMenuRequested += (_, _) => _trayTooltip.Hide();
        _trayIcon.Show();

        // Global Shortcuts: registered on the tray icon's window; each switches the shown buds' noise mode
        _shortcuts = new GlobalShortcuts(_loggerFactory.CreateLogger<GlobalShortcuts>());
        _trayIcon.HotkeyPressed += (_, id) => _shortcuts.OnHotkey(id);
        _shortcuts.Pressed += (_, action) => OnShortcutPressed(action);
        _shortcuts.Attach(_trayIcon.Handle, _settings);

        ApplyTheme(_settings.Theme);
        _manager.Start();
        WatchForQuitRequests();
        AppLog.Started(_logger, AppIdentity.Version);

        // Startup itself leaves a lot behind that the idle tray app never needs
        ScheduleMemoryTrim();
    }

    // MagicBuds.exe --quit signals this event (see Program); quit like the tray menu does
    private void WatchForQuitRequests()
    {
        var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        _quitRequest = new EventWaitHandle(false, EventResetMode.AutoReset, Program.QuitEventName);
        _quitWait = ThreadPool.RegisterWaitForSingleObject(
            _quitRequest,
            (_, _) => dispatcher.TryEnqueue(Quit),
            null,
            Timeout.Infinite,
            executeOnlyOnce: true);
    }

    // The buds the flyout shows, or else the first connected ones
    private void OnShortcutPressed(ShortcutAction action)
    {
        var name = action.ToString();

        // Open The Flyout: by the tray icon, or with Windhawk's tray on every taskbar, on the screen the mouse is on
        if (action == ShortcutAction.OpenFlyout)
        {
            AppLog.ShortcutPressed(_logger, name, true);
            if (_flyoutWindow is not null && _trayIcon is not null)
            {
                _flyoutWindow.Toggle(Windhawk.HasTrayOnEveryTaskbar() ? null : _trayIcon.GetIconRect());
            }
            return;
        }

        var connected = _flyout?.ConnectedHeadsets ?? [];
        var headset = _flyout?.CurrentHeadset is { IsConnected: true } current
            ? current
            : connected.Count > 0 ? connected[0] : null;
        AppLog.ShortcutPressed(_logger, name, headset is not null);
        headset?.ApplyNoiseMode(ShortcutActions.NoiseModeFor(action));
    }

    // The right-click menu's window is made on first use, since most sessions never open it
    private void ShowTrayMenu(int pointX, int pointY)
    {
        _flyoutWindow?.HideFlyout();
        if (_trayMenu is null)
        {
            _trayMenu = new TrayMenuWindow();
            _trayMenu.ApplyTheme(_settings?.Theme ?? AppTheme.System);
            _trayMenu.SettingsRequested += (_, _) => ShowSettings();
            _trayMenu.QuitRequested += (_, _) => Quit();
        }

        var (x, y, edge, _, _) = ScreenGeometry.GetMenuAnchor(pointX, pointY);
        _trayMenu.ShowAt(x, y, edge);
    }

    // Waits a few seconds so a quick reopen doesn't pay for it, and so closed windows finish tearing down
    private void ScheduleMemoryTrim()
    {
        if (_trimTimer is null)
        {
            _trimTimer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
            _trimTimer.Interval = TimeSpan.FromSeconds(5);
            _trimTimer.IsRepeating = false;
            _trimTimer.Tick += (_, _) => TrimMemory();
        }
        _trimTimer.Stop();
        _trimTimer.Start();
    }

    // Only while nothing is on screen: a full compacting collection, then pages nothing is
    // using leave the working set (Windows pages them back in if they're needed again)
    private void TrimMemory()
    {
        if (_flyoutWindow?.AppWindow.IsVisible == true || _settingsWindow is not null)
        {
            return;
        }

        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);

        // Finalizers release the WinRT objects behind closed windows; collect what they let go
        GC.WaitForPendingFinalizers();
        GC.Collect();
        _ = NativeMethods.SetProcessWorkingSetSize(NativeMethods.GetCurrentProcess(), -1, -1);
    }

    private void ShowTrayTooltip()
    {
        if (_trayIcon?.GetIconRect() is not { } icon || _trayTooltip is null || _flyoutWindow?.AppWindow.IsVisible == true)
        {
            return;
        }

        // Taskbar side and work area of the icon's monitor; the tooltip itself follows the mouse
        var (_, _, edge, workArea, scale) = ScreenGeometry.GetMenuAnchor((icon.Left + icon.Right) / 2, (icon.Top + icon.Bottom) / 2);
        _trayTooltip.Open(edge, workArea, scale);
    }

    // Opens Settings, on the page with this menu tag when there's one (otherwise where it was)
    private void ShowSettings(string? page = null)
    {
        _flyoutWindow?.HideFlyout();
        if (_settingsViewModel is null || _settings is null)
        {
            return;
        }

        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_settingsViewModel, new ShortcutsContext(_shortcuts!, _settings));
            _settingsWindow.ApplyTheme(_settings.Theme);
            _settingsWindow.Closed += (_, _) =>
            {
                _settingsWindow = null;
                ScheduleMemoryTrim();
            };
        }
        if (page is not null)
        {
            _settingsWindow.ShowPage(page);
        }
        _settingsWindow.Activate();
    }

    private void ApplyTheme(AppTheme theme)
    {
        _flyoutWindow?.ApplyTheme(theme);
        _settingsWindow?.ApplyTheme(theme);
        _trayMenu?.ApplyTheme(theme);
        _trayTooltip?.ApplyTheme(theme);
    }

    private void OpenFolder(string folder)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.OpenFolderFailed(_logger, ex);
        }
    }

    public void Dispose()
    {
        _trimTimer?.Stop();
        _shortcuts?.Suspend();
        _trayIcon?.Dispose();
        _trayMenu?.Close();
        _trayTooltip?.Close();
        _settingsWindow?.Close();
        _flyoutWindow?.Shutdown();
        _flyout?.Dispose();
        _manager?.Dispose();
        _notifications?.Dispose();
        _quitWait?.Unregister(null);
        _quitRequest?.Dispose();
        _loggerFactory?.Dispose();
        GC.SuppressFinalize(this);
    }

    private void Quit()
    {
        AppLog.Quit(_logger);
        Dispose();
        Exit();
    }
}
