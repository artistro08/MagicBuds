using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using MagicBuds.Presentation.Placement;
using MagicBuds.Presentation.Settings;
using Windows.Foundation;
using Windows.Graphics;
using WinRT.Interop;

namespace MagicBuds.App;

/// <summary>
/// Hosts the tray icon's right-click menu as a WinUI <see cref="MenuFlyout"/>.
/// </summary>
/// <remarks>
/// A MenuFlyout needs a XAML root, so a 1 × 1 frameless, transparent window sits at the click point and
/// the menu opens outside its bounds (<c>ShouldConstrainToRootBounds = false</c>), away from
/// the taskbar. The window hides again when the menu closes or focus moves elsewhere.
/// </remarks>
internal sealed partial class TrayMenuWindow : Window
{
    private readonly Grid _root = new();
    private readonly MenuFlyout _menu = new() { ShouldConstrainToRootBounds = false };
    private readonly IntPtr _hwnd;
    private FlyoutShowOptions? _pendingShow;

    public TrayMenuWindow()
    {
        Content = _root;
        _hwnd = WindowNative.GetWindowHandle(this);

        _menu.Closed += (_, _) => AppWindow?.Hide();

        // Window Chrome
        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(false, false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;

        // Invisible host: frameless and see-through, so only the menu shows
        PopupWindowStyle.Apply(_hwnd);
        SystemBackdrop = new TransparentBackdrop();

        // Behavior
        _root.Loaded += (_, _) => ShowPendingMenu();
        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated)
            {
                _menu.Hide();
            }
        };
    }

    public event EventHandler? SettingsRequested;

    public event EventHandler? QuitRequested;

    /// <summary>
    /// Opens the menu at a screen point (physical pixels), growing away from the taskbar.
    /// </summary>
    public void ShowAt(int x, int y, TaskbarEdge edge)
    {
        BuildItems();

        AppWindow.MoveAndResize(new RectInt32(x, y, 1, 1));
        AppWindow.Show(true);
        Activate();
        NativeMethods.SetForegroundWindow(_hwnd);

        _pendingShow = new FlyoutShowOptions
        {
            Position = new Point(0, 0),
            Placement = edge switch
            {
                TaskbarEdge.Top => FlyoutPlacementMode.BottomEdgeAlignedRight,
                TaskbarEdge.Left => FlyoutPlacementMode.RightEdgeAlignedBottom,
                TaskbarEdge.Right => FlyoutPlacementMode.LeftEdgeAlignedBottom,
                _ => FlyoutPlacementMode.TopEdgeAlignedRight,
            },
        };
        if (_root.IsLoaded)
        {
            ShowPendingMenu();
        }
    }

    /// <summary>
    /// Closes the menu if it's open; the window hides with it.
    /// </summary>
    public void HideMenu() => _menu.Hide();

    public void ApplyTheme(AppTheme theme) => _root.RequestedTheme = theme switch
    {
        AppTheme.Light => ElementTheme.Light,
        AppTheme.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    // Settings and Quit
    private void BuildItems()
    {
        _menu.Items.Clear();
        _menu.Items.Add(CreateItem("Settings", new FontIcon { Glyph = "" }, () => SettingsRequested?.Invoke(this, EventArgs.Empty)));
        _menu.Items.Add(new MenuFlyoutSeparator());
        _menu.Items.Add(CreateItem("Quit", new FontIcon { Glyph = "" }, () => QuitRequested?.Invoke(this, EventArgs.Empty)));
    }

    private static MenuFlyoutItem CreateItem(string text, IconElement icon, Action onClick)
    {
        // Always the mouse-sized padding. WinUI picks touch padding (taller rows) when it can't
        // tell how the menu was opened, which is often, since the host window never gets input.
        var item = new MenuFlyoutItem
        {
            Text = text,
            Icon = icon,
            Padding = (Thickness)Application.Current.Resources["MenuFlyoutItemThemePaddingNarrow"],
        };
        item.Click += (_, _) => onClick();
        return item;
    }

    private void ShowPendingMenu()
    {
        if (_pendingShow is not { } options)
        {
            return;
        }
        _pendingShow = null;
        _menu.ShowAt(_root, options);
    }
}
