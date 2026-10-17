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
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        var main = AppInstance.FindOrRegisterForKey("main");
        if (main.IsCurrent)
        {
            main.Activated += (_, _) => App.DispatcherQueue?.TryEnqueue(App.ShowMainWindow);
            return false;
        }

        // Redirect from another thread: blocking this UI (STA) thread on it can deadlock.
        Task.Run(() => main.RedirectActivationToAsync(activation).AsTask()).Wait();
        return true;
    }
}
