using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace MagicBuds.App;

/// <summary>
/// Entry point. Replaces the XAML-generated Main so only one copy of the tray app runs.
/// </summary>
/// <remarks>
/// <c>MagicBuds.exe --quit</c> asks the running copy to quit the same way the tray menu's Quit
/// does, so it closes its link to the buds properly. Build scripts and installers use it
/// instead of ending the process, which would leave the link's subscriptions open on the buds.
/// </remarks>
public static class Program
{
    private const string InstanceKey = "MagicBuds";

    /// <summary>
    /// Signaled by <c>--quit</c>; the running copy waits on it.
    /// </summary>
    public const string QuitEventName = "MagicBuds.Quit";

    /// <summary>
    /// Signaled each time MagicBuds is opened by hand, by the copy that starts up or a second
    /// launch; the running copy does what
    /// <see cref="Presentation.Settings.AppSettings.HiddenTrayLaunch"/> says if its tray icon is hidden.
    /// </summary>
    public const string ShowEventName = "MagicBuds.Show";

    /// <summary>
    /// The running copy's end of <see cref="ShowEventName"/>, made before the single-instance
    /// check, so a second launch while this copy is still starting isn't lost.
    /// </summary>
    public static EventWaitHandle? ShowRequest { get; private set; }

    [STAThread]
    private static void Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        // Quit Request
        if (args.Contains("--quit", StringComparer.OrdinalIgnoreCase))
        {
            if (EventWaitHandle.TryOpenExisting(QuitEventName, out var quit))
            {
                quit.Set();
                quit.Dispose();
            }
            return;
        }

        // Opened By Hand: a plain launch with no arguments. The MSIX startup task and notification
        // clicks are their own activation kinds; the classic install's start at sign-in passes
        // --startup and its notification clicks pass the buds' ID.
        var openedByHand = AppInstance.GetCurrent().GetActivatedEventArgs().Kind == ExtendedActivationKind.Launch
            && args.Length == 0;

        // Every launch makes or opens the show event before checking for a running copy, so it
        // always exists by the time a second launch looks for it
        var showRequest = OpenShowEvent();

        // A second launch exits right away; opened by hand, it tells the running copy first and
        // lets it take the foreground, which Windows would otherwise refuse a background app
        var instance = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (!instance.IsCurrent)
        {
            if (openedByHand && showRequest is not null)
            {
                NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
                showRequest.Set();
            }
            showRequest?.Dispose();
            return;
        }

        // This copy opened by hand asks itself, the same way; the app picks it up once it's watching
        ShowRequest = showRequest;
        if (openedByHand)
        {
            ShowRequest?.Set();
        }
        Application.Start(callbackParams =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
        ShowRequest?.Dispose();
    }

    // Null when another process owns the name (an elevated copy, say); the app runs without it
    private static EventWaitHandle? OpenShowEvent()
    {
        try
        {
            return new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException or IOException)
        {
            return null;
        }
    }
}
