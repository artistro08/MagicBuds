using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MagicBuds.Presentation.Logging;
using MagicBuds.Presentation.Settings;

namespace MagicBuds.Presentation.ViewModels;

/// <summary>
/// The settings window.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly FlyoutViewModel _flyout;
    private readonly AppSettings _settings;
    private readonly LogLevelSwitch _logLevel;
    private readonly IStartupTaskService _startup;
    private readonly Action<AppTheme> _applyTheme;
    private readonly Action<string> _openFolder;
    private readonly ILogger _logger;

    private bool _launchAtSignIn;

    public SettingsViewModel(
        FlyoutViewModel flyout,
        AppSettings settings,
        LogLevelSwitch logLevel,
        IStartupTaskService startup,
        Action<AppTheme> applyTheme,
        Action<string> openFolder,
        string logFolder,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(flyout);

        _flyout     = flyout;
        _settings   = settings;
        _logLevel   = logLevel;
        _startup    = startup;
        _applyTheme = applyTheme;
        _openFolder = openFolder;
        _logger     = logger;
        LogFolder   = logFolder;

        OpenLogFolderCommand = new RelayCommand(() => _openFolder(LogFolder));
        _flyout.Headsets.CollectionChanged += (_, _) => RaiseHeadsetChanged();
        _flyout.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FlyoutViewModel.CurrentHeadset))
            {
                RaiseHeadsetChanged();
            }
        };
    }

    // =========================================================================
    // DEVICES
    // =========================================================================

    public ObservableCollection<HeadsetViewModel> Headsets => _flyout.Headsets;

    public bool NoHeadset => Headsets.Count == 0;

    /// <summary>
    /// The buds the Buds and Controls pages edit: the ones the flyout shows, or the first paired.
    /// </summary>
    public HeadsetViewModel? SelectedHeadset => _flyout.CurrentHeadset ?? Headsets.FirstOrDefault();

    public bool HasHeadset => SelectedHeadset is not null;

    // =========================================================================
    // APP
    // =========================================================================

    public bool LaunchAtSignIn
    {
        get => _launchAtSignIn;
        set
        {
            if (SetProperty(ref _launchAtSignIn, value))
            {
                _ = ApplyLaunchAtSignInAsync(value);
            }
        }
    }

    public bool LowBatteryNotifications
    {
        get => _settings.LowBatteryNotifications;
        set
        {
            if (value == _settings.LowBatteryNotifications)
            {
                return;
            }
            _settings.LowBatteryNotifications = value;
            OnPropertyChanged();
        }
    }

    public bool HideTray
    {
        get => _settings.HideTray;
        set
        {
            if (value == _settings.HideTray)
            {
                return;
            }
            _settings.HideTray = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanHideTrayWhileDisconnected));
        }
    }

    /// <summary>
    /// "Until my buds connect" only matters while the icon isn't hidden for good.
    /// </summary>
    public bool CanHideTrayWhileDisconnected => !HideTray;

    public bool HideTrayWhileDisconnected
    {
        get => _settings.HideTrayWhileDisconnected;
        set
        {
            if (value == _settings.HideTrayWhileDisconnected)
            {
                return;
            }
            _settings.HideTrayWhileDisconnected = value;
            OnPropertyChanged();
        }
    }

    public static IReadOnlyList<string> ThemeOptions { get; } = ["Use system setting", "Light", "Dark"];

    public int ThemeIndex
    {
        get => (int)_settings.Theme;
        set
        {
            if (value < 0 || value == (int)_settings.Theme)
            {
                return;
            }
            _settings.Theme = (AppTheme)value;
            _applyTheme(_settings.Theme);
            OnPropertyChanged();
        }
    }

    public bool DebugLogging
    {
        get => _settings.DebugLogging;
        set
        {
            if (value == _settings.DebugLogging)
            {
                return;
            }
            _settings.DebugLogging = value;
            ApplyLogLevel();
            OnPropertyChanged();
        }
    }

    public string LogFolder { get; }

    public IRelayCommand OpenLogFolderCommand { get; }

    /// <summary>
    /// Reads the startup task state. Called when the window opens.
    /// </summary>
    public async Task LoadAsync()
    {
        try
        {
            _launchAtSignIn = await _startup.IsEnabledAsync().ConfigureAwait(true);
            OnPropertyChanged(nameof(LaunchAtSignIn));
        }
        catch (Exception ex)
        {
            LogMessages.StartupTaskReadFailed(_logger, ex);
        }
    }

    /// <summary>
    /// Applies the saved Debug logging choice to the file logger. Called at startup.
    /// </summary>
    public void ApplyLogLevel() => _logLevel.MinimumLevel = _settings.DebugLogging ? LogLevel.Debug : LogLevel.Information;

    private void RaiseHeadsetChanged()
    {
        OnPropertyChanged(nameof(NoHeadset));
        OnPropertyChanged(nameof(SelectedHeadset));
        OnPropertyChanged(nameof(HasHeadset));
    }

    private async Task ApplyLaunchAtSignInAsync(bool enabled)
    {
        try
        {
            var actual = await _startup.SetEnabledAsync(enabled).ConfigureAwait(true);
            if (actual != _launchAtSignIn)
            {
                _launchAtSignIn = actual;
                OnPropertyChanged(nameof(LaunchAtSignIn));
            }
        }
        catch (Exception ex)
        {
            LogMessages.StartupTaskChangeFailed(_logger, ex);
        }
    }
}
