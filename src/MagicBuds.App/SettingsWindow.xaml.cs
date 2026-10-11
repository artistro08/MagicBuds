using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MagicBuds.App.Views.Settings;
using MagicBuds.Presentation.Settings;
using MagicBuds.Presentation.ViewModels;

namespace MagicBuds.App;

/// <summary>
/// Settings window with a left menu. One page per menu item, all sharing one view model.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private static readonly Dictionary<string, Type> Pages = new()
    {
        ["Devices"]  = typeof(DevicesPage),
        ["Buds"]     = typeof(BudsPage),
        ["Controls"]  = typeof(ControlsPage),
        ["Shortcuts"] = typeof(ShortcutsPage),
        ["General"]   = typeof(AppPage),
        ["About"]     = typeof(AboutPage),
    };

    private readonly SettingsViewModel _viewModel;
    private readonly ShortcutsContext _shortcuts;

    public SettingsWindow(SettingsViewModel viewModel, ShortcutsContext shortcuts)
    {
        _viewModel = viewModel;
        _shortcuts = shortcuts;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 720));

        // Opening size is also the smallest it can get
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = AppWindow.Size.Width;
            presenter.PreferredMinimumHeight = AppWindow.Size.Height;
        }
        // Taskbar and title bar icon; SetIcon only takes .ico files (a PNG leaves the blank default)
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        Navigation.SelectedItem = Navigation.MenuItems[0];
        _ = _viewModel.LoadAsync();

        // A shortcut dialog open as the window closes would keep its keyboard hook (it swallows every key)
        Closed += (_, _) => (ContentFrame.Content as ShortcutsPage)?.CloseDialogs();
    }

    public void ApplyTheme(AppTheme theme) => RootGrid.RequestedTheme = theme switch
    {
        AppTheme.Light => ElementTheme.Light,
        AppTheme.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    /// <summary>
    /// Selects the menu item with this tag ("Devices", "Buds", …), which opens its page.
    /// </summary>
    public void ShowPage(string tag) => Navigation.SelectedItem = Navigation.MenuItems
        .OfType<NavigationViewItem>()
        .FirstOrDefault(item => item.Tag as string == tag) ?? Navigation.SelectedItem;

    private void OnPaneToggleRequested(TitleBar sender, object args) => Navigation.IsPaneOpen = !Navigation.IsPaneOpen;

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag } && Pages.TryGetValue(tag, out var page))
        {
            // The Shortcuts page works with the registered shortcuts; every other page with the view model
            ContentFrame.Navigate(page, page == typeof(ShortcutsPage) ? _shortcuts : _viewModel);
        }
    }
}
