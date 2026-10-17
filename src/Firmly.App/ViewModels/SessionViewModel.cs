using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Firmly.Ipc;

namespace Firmly.App.ViewModels;

/// <summary>The app's connection to the Firmly service, shared by every page (<see cref="App.Session"/>).</summary>
public partial class SessionViewModel : ObservableObject
{
    private readonly FirmlyClient _client = new();
    private bool _supportsOverride;

    public SessionViewModel()
    {
        Status = "Not connected to the Firmly service yet.";
        Charge = new ChargeViewModel(_client, Show);
    }

    public FirmlyClient Client => _client;

    /// <summary>One line on the connection, e.g. "HP firmware", or why there's no connection.</summary>
    [ObservableProperty]
    public partial string Status { get; set; }

    public ObservableCollection<SettingViewModel> Settings { get; } = [];

    public BatteryViewModel Battery { get; } = new();

    public ChargeViewModel Charge { get; }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        Status = "Connecting to the Firmly service…";
        try
        {
            var status = await _client.GetStatusAsync();
            _supportsOverride = status.Features?.Contains(IpcFeatures.ChargeOverride) == true;
            if (status.Vendor is null)
            {
                Settings.Clear();
                Charge.Clear();
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
            Charge.Clear();
            Status = ex.Message;
        }
    }

    /// <summary>Restarts the Firmly Windows service (one UAC prompt), then reconnects.</summary>
    /// <returns>Why it didn't work, or null when it did (or the user declined the prompt).</returns>
    public async Task<string?> RestartServiceAsync()
    {
        Status = "Restarting the Firmly service…";
        string? problem = null;
        switch (await ServiceControl.RestartAsync())
        {
            case ServiceRestartResult.NotInstalled:
                problem = "The Firmly service isn't installed on this PC. Reinstall Firmly to add it.";
                break;
            case ServiceRestartResult.Failed:
                problem = "The Firmly service didn't start again. Its entries in the Windows event log may say why.";
                break;
        }

        // Show where things stand, whatever happened.
        await RefreshAsync();
        return problem;
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
    private void Show(FirmwareSnapshot snapshot)
    {
        Battery.SetReportedChargeLimit(snapshot.ReportedChargeLimit);
        Charge.Update(snapshot, _supportsOverride);

        var settings = snapshot.Settings;
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
