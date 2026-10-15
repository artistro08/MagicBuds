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
    /// Signaled by a second launch; the running copy opens Settings.
    /// </summary>
    public const string ShowEventName = "MagicBuds.Show";

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

        // A second launch exits right away. Opening MagicBuds by hand also asks the running copy
        // to show Settings, since its tray icon may be hidden; the startup task doesn't.
        var instance = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (!instance.IsCurrent)
        {
            if (AppInstance.GetCurrent().GetActivatedEventArgs().Kind == ExtendedActivationKind.Launch
                && EventWaitHandle.TryOpenExisting(ShowEventName, out var show))
            {
                show.Set();
                show.Dispose();
            }
            return;
        }

        Application.Start(callbackParams =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }
}
