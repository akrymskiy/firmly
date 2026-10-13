using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Firmly.Ipc;

namespace Firmly.App.ViewModels;

public partial class MainPageViewModel : ObservableObject
{
    private readonly FirmlyClient _client = new();

    public MainPageViewModel()
    {
        Status = "Not connected to the Firmly service yet.";
    }

    [ObservableProperty]
    public partial string Status { get; set; }

    public ObservableCollection<SettingViewModel> Settings { get; } = [];

    [RelayCommand]
    private async Task RefreshAsync()
    {
        Status = "Connecting to the Firmly service…";
        try
        {
            var status = await _client.GetStatusAsync();
            if (status.Vendor is null)
            {
                Settings.Clear();
                Status = "This device's firmware isn't supported yet.";
                return;
            }

            var settings = await _client.GetSettingsAsync();
            Settings.Clear();
            foreach (var setting in settings)
                Settings.Add(new SettingViewModel(setting, _client));

            Status = status.RequireAdminForAllChanges && !status.IsAdministrator
                ? $"{status.Vendor} firmware · changes are limited to administrators on this device"
                : $"{status.Vendor} firmware";
        }
        catch (FirmlyServiceException ex)
        {
            Settings.Clear();
            Status = ex.Message;
        }
    }
}
