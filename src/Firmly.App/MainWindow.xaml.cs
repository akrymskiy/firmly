using Firmly.App.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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

    public void ShowSettings() => NavView.SelectedItem = NavView.SettingsItem;

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
