using Firmly.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Firmly.App.Pages;

/// <summary>The battery's charge, its charge mode, and a full charge for a while.</summary>
public sealed partial class BatteryPage : Page
{
    public BatteryPage()
    {
        InitializeComponent();
    }

    public SessionViewModel Session => App.Session;

    public StartupPromptViewModel StartupPrompt => App.StartupPrompt;

    // The binding only ever selects the firmware's current mode (one-way), so another selection is the user's pick.
    private void OnModeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is RadioButtons picker)
            Session.Charge.RequestMode(picker.SelectedIndex);
    }
}
