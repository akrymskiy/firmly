using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Firmly.App;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// The main application window. Use <c>App.Window</c> from any class that needs
    /// the window reference (for dialogs, pickers, interop, etc.).
    /// </summary>
    public static Window Window { get; private set; } = null!;

    /// <summary>
    /// The UI thread dispatcher. Use <c>App.DispatcherQueue</c> to marshal calls
    /// to the UI thread. Fully qualified to avoid CS0104 ambiguity with
    /// <see cref="Windows.System.DispatcherQueue"/>.
    /// </summary>
    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;

    /// <summary>The connection to the Firmly service, shared by every page.</summary>
    public static ViewModels.SessionViewModel Session { get; private set; } = null!;

    /// <summary>
    /// The native window handle (HWND). Use for file pickers,
    /// <c>DataTransferManager</c>, and any WinRT interop that requires
    /// <c>InitializeWithWindow</c>.
    /// </summary>
    public static nint WindowHandle =>
        WinRT.Interop.WindowNative.GetWindowHandle(Window);

    /// <summary>
    /// Initializes the singleton application object.
    /// </summary>
    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => LogCrash(e.Exception);
    }

    /// <summary>Unhandled errors, newest last, in the package's local folder: %LocalAppData%\Packages\…\LocalState.</summary>
    public static string ErrorLogPath => Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "errors.log");

    private static void LogCrash(Exception exception)
    {
        try
        {
            File.AppendAllText(ErrorLogPath, $"{DateTimeOffset.Now:O} {AboutInfo.AppVersion}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // Nothing more to do while crashing.
        }
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        Session = new ViewModels.SessionViewModel();
        Window = new MainWindow();

        try
        {
            s_tray = new Tray.TrayController(Session);
        }
        catch (InvalidOperationException ex)
        {
            // Without a tray icon, Firmly is an ordinary window that closes for good.
            System.Diagnostics.Debug.WriteLine(ex);
        }

        // Started at sign-in: stay in the notification area until asked.
        var kind = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs().Kind;
        if (kind != Microsoft.Windows.AppLifecycle.ExtendedActivationKind.StartupTask || !HasTray)
            Window.Activate();

        Session.RefreshCommand.Execute(null);
        Session.StartAutoRefresh();
    }

    private static Tray.TrayController? s_tray;

    /// <summary>Whether Firmly has an icon in the notification area to keep running from.</summary>
    public static bool HasTray => s_tray is not null;

    /// <summary>Set once the user chose to exit, so closing the window really ends Firmly.</summary>
    public static bool IsExiting { get; private set; }

    public static bool IsMainWindowVisible => Window.AppWindow.IsVisible;

    /// <summary>Brings the window back, e.g. from the tray or when Firmly is launched again.</summary>
    public static void ShowMainWindow()
    {
        Window.AppWindow.Show();
        Window.Activate();
        _ = Session.RefreshQuietlyAsync();
    }

    /// <summary>Ends Firmly, tray icon and all.</summary>
    public static void ExitApp()
    {
        IsExiting = true;
        Window.Close();
        Current.Exit();
    }

    /// <summary>Removes the tray icon once the window is gone; a dead process would leave it behind until hovered.</summary>
    public static void OnMainWindowClosed()
    {
        s_tray?.Dispose();
        s_tray = null;
    }
}
