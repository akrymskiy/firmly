using Firmly.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Firmly.App.Pages;

/// <summary>The battery's charge and how the firmware manages it.</summary>
public sealed partial class BatteryPage : Page
{
    public BatteryPage()
    {
        InitializeComponent();
    }

    public SessionViewModel Session => App.Session;
}
