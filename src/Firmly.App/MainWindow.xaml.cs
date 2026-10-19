using Firmly.App.Pages;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.System;

namespace Firmly.App;

/// <summary>The application window: title bar, navigation pane, and a frame for the selected page.</summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");
        OpenCompact();

        // Closing keeps Firmly running in the notification area, unless turned off in Settings or exiting.
        AppWindow.Closing += (sender, args) =>
        {
            if (!App.IsExiting && App.HasTray && AppSettings.CloseToTray)
            {
                args.Cancel = true;
                sender.Hide();
            }
        };
        Closed += (_, _) => App.OnMainWindowClosed();

        NavView.SelectedItem = NavView.MenuItems[0];
    }

    // In view pixels: enough for the pages without the empty space of Windows' default size.
    private const int OpenWidth = 760, OpenHeight = 680;

    /// <summary>Sizes the window for its pages, within the screen's work area, and centres it there.</summary>
    private void OpenCompact()
    {
        var scale = Tray.NativeMethods.GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var width = Math.Min((int)(OpenWidth * scale), area.Width);
        var height = Math.Min((int)(OpenHeight * scale), area.Height);
        AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height));
    }

    public void ShowSettings() => NavView.SelectedItem = NavView.SettingsItem;

    public void ShowBattery() => NavView.SelectedItem = NavView.MenuItems[0];

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var page = args.IsSettingsSelected
            ? typeof(SettingsPage)
            : (args.SelectedItem as NavigationViewItem)?.Tag switch
            {
                nameof(BatteryPage) => typeof(BatteryPage),
                nameof(FirmwarePage) => typeof(FirmwarePage),
                _ => null,
            };

        if (page is not null && RootFrame.CurrentSourcePageType != page)
            RootFrame.Navigate(page, null, new EntranceNavigationTransitionInfo());
    }

    private void OnPaneToggleRequested(TitleBar sender, object args) => NavView.IsPaneOpen = !NavView.IsPaneOpen;

    private async void OnHelpInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await Launcher.LaunchUriAsync(new Uri(AboutInfo.HelpUrl));
    }
}
