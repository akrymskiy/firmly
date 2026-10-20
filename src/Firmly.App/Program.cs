using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Firmly.App;

/// <summary>Starts Firmly, or hands the launch to the copy that's already running.</summary>
public static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        if (RedirectToRunningInstance())
            return 0;

        Application.Start(init =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
        return 0;
    }

    // One Firmly per user: launching it again (from Start, or a second sign-in start) shows the running copy.
    private static bool RedirectToRunningInstance()
    {
        var main = AppInstance.FindOrRegisterForKey("main");
        if (main.IsCurrent)
        {
            // The sign-in start can come after the user already opened Firmly; it shouldn't bring the window back.
            main.Activated += (_, e) =>
            {
                if (e.Kind != ExtendedActivationKind.StartupTask)
                    App.DispatcherQueue?.TryEnqueue(App.ShowMainWindow);
            };
            // Before anything reads the activation: when a notification click starts Firmly, the Windows App SDK fails
            // fast in GetActivatedEventArgs unless the notification COM class is registered by then.
            Notifications.Register();
            return false;
        }

        // Not started by a notification click: those go to the running copy's COM class.
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        // Redirect from another thread: blocking this UI (STA) thread on it can deadlock.
        Task.Run(() => main.RedirectActivationToAsync(activation).AsTask()).Wait();
        return true;
    }
}
