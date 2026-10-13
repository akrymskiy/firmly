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

            Show(await _client.GetSettingsAsync());
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

    private async Task ReloadSettingsAsync()
    {
        try
        {
            Show(await _client.GetSettingsAsync());
        }
        catch (FirmlyServiceException ex)
        {
            Status = ex.Message;
        }
    }

    // Updates existing cards in place when the list is unchanged. Replacing them makes the list re-bind its
    // recycled ComboBoxes, which blanks their selection.
    private void Show(IReadOnlyList<SettingInfo> settings)
    {
        var sameCards = Settings.Count == settings.Count
            && Settings.Zip(settings).All(p => p.First.Name == p.Second.Setting.Name
                && p.First.Options.SequenceEqual(p.Second.Setting.PossibleValues));

        if (sameCards)
        {
            for (var i = 0; i < settings.Count; i++)
                Settings[i].Update(settings[i]);
            return;
        }

        Settings.Clear();
        foreach (var setting in settings)
            Settings.Add(new SettingViewModel(setting, _client, ReloadSettingsAsync));
    }
}
