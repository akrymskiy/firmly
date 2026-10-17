using Firmly.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Firmly.App.Pages;

/// <summary>Settings for Firmly itself, the service, and About.</summary>
public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => ViewModel.LoadCommand.Execute(null);
    }

    public SettingsViewModel ViewModel { get; } = new(App.Session);
}
