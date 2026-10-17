using Firmly.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Firmly.App.Pages;

/// <summary>Every firmware (BIOS/UEFI) setting Firmly may show, as the firmware names them.</summary>
public sealed partial class FirmwarePage : Page
{
    public FirmwarePage()
    {
        InitializeComponent();
    }

    public SessionViewModel Session => App.Session;
}
