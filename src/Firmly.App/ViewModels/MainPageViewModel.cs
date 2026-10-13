using CommunityToolkit.Mvvm.ComponentModel;

namespace Firmly.App.ViewModels;

public partial class MainPageViewModel : ObservableObject
{
    public MainPageViewModel()
    {
        Status = "Not connected to the Firmly service yet.";
    }

    [ObservableProperty]
    public partial string Status { get; set; }
}
