using Firmly.Ipc;
using Microsoft.UI.Xaml;
using Windows.System;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Firmly.App;

/// <summary>
/// The application window. This hosts a Frame that displays pages. Add your
/// UI and logic to MainPage.xaml / MainPage.xaml.cs instead of here so you
/// can use Page features such as navigation events and the Loaded lifecycle.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly FirmlyClient _client = new();
    private bool _aboutOpen;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");

        // Navigate the root frame to the main page on startup.
        RootFrame.Navigate(typeof(MainPage));
    }

    private async void OnHelpClick(object sender, RoutedEventArgs e) =>
        await Launcher.LaunchUriAsync(new Uri(AboutInfo.HelpUrl));

    private async void OnReportProblemClick(object sender, RoutedEventArgs e) =>
        await Launcher.LaunchUriAsync((await AboutInfo.LoadAsync(_client)).NewIssueUri);

    private async void OnRestartServiceClick(object sender, RoutedEventArgs e)
    {
        if (RootFrame.Content is MainPage page)
            await page.ViewModel.RestartServiceAsync();
    }

    private async void OnAboutClick(object sender, RoutedEventArgs e)
    {
        // Only one ContentDialog can be open at a time.
        if (_aboutOpen)
            return;

        _aboutOpen = true;
        try
        {
            var dialog = new AboutDialog(await AboutInfo.LoadAsync(_client)) { XamlRoot = Content.XamlRoot };
            await dialog.ShowAsync();
        }
        finally
        {
            _aboutOpen = false;
        }
    }
}
