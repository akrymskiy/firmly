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
    private bool _reportsOverrideEnd;

    public SessionViewModel()
    {
        Status = Text.Get("Status_NotConnected");
        Charge = new ChargeViewModel(_client, Show, Passwords);
    }

    public FirmlyClient Client => _client;

    /// <summary>Adds the BIOS password to changes when one is set.</summary>
    public BiosPasswordGate Passwords { get; } = new();

    /// <summary>The BIOS has an administrator password, so changes ask for it.</summary>
    [ObservableProperty]
    public partial bool BiosPasswordSet { get; set; }

    /// <summary>The machine policy lets Firmly keep the BIOS password.</summary>
    [ObservableProperty]
    public partial bool AllowSavedBiosPassword { get; set; } = true;

    /// <summary>One line on the connection, e.g. "HP firmware", or why there's no connection.</summary>
    [ObservableProperty]
    public partial string Status { get; set; }

    public ObservableCollection<SettingViewModel> Settings { get; } = [];

    public BatteryViewModel Battery { get; } = new();

    public ChargeViewModel Charge { get; }

    // Read again every so often, so the tray stays current while the window is closed.
    private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromMinutes(2);

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _autoRefresh;

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync(quiet: false);

    /// <summary>Reads everything again without showing "Connecting…" meanwhile.</summary>
    public Task RefreshQuietlyAsync() => LoadAsync(quiet: true);

    /// <summary>Refreshes quietly every couple of minutes from now on. Call on the UI thread.</summary>
    public void StartAutoRefresh()
    {
        _autoRefresh ??= Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
        _autoRefresh.Interval = AutoRefreshInterval;
        _autoRefresh.Tick += (_, _) => _ = RefreshQuietlyAsync();
        _autoRefresh.Start();
    }

    private async Task LoadAsync(bool quiet)
    {
        if (!quiet)
            Status = Text.Get("Status_Connecting");
        try
        {
            var status = await _client.GetStatusAsync();
            _supportsOverride = status.Features?.Contains(IpcFeatures.ChargeOverride) == true;
            _reportsOverrideEnd = status.Features?.Contains(IpcFeatures.ChargeOverrideEnd) == true;
            AllowSavedBiosPassword = Passwords.AllowSaved = status.AllowSavedBiosPassword;
            if (status.Vendor is null)
            {
                Settings.Clear();
                Charge.Clear();
                Status = Text.Get("Status_Unsupported");
                return;
            }

            Show(await _client.GetSettingsAsync());
            Status = status.RequireAdminForAllChanges && !status.IsAdministrator
                ? Text.Format("Status_FirmwareAdminOnly", status.Vendor)
                : Text.Format("Status_Firmware", status.Vendor);
        }
        catch (FirmlyServiceException ex)
        {
            Settings.Clear();
            Charge.Clear();
            Status = Text.ForError(ex);
        }
    }

    /// <summary>Restarts the Firmly Windows service (one UAC prompt), then reconnects.</summary>
    /// <returns>Why it didn't work, or null when it did (or the user declined the prompt).</returns>
    public async Task<string?> RestartServiceAsync()
    {
        Status = Text.Get("Status_Restarting");
        string? problem = null;
        switch (await ServiceControl.RestartAsync())
        {
            case ServiceRestartResult.NotInstalled:
                problem = Text.Get("Service_NotInstalled");
                break;
            case ServiceRestartResult.Failed:
                problem = Text.Get("Service_DidntStart");
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
            Status = Text.ForError(ex);
        }
    }

    // Updates existing cards in place when the list is unchanged. Replacing them makes the list re-bind its
    // recycled ComboBoxes, which blanks their selection.
    private void Show(FirmwareSnapshot snapshot)
    {
        BiosPasswordSet = Passwords.IsSet = snapshot.BiosPasswordSet;
        Passwords.TriesLeft = snapshot.BiosPasswordTriesLeft;
        Battery.SetReportedChargeLimit(snapshot.ReportedChargeLimit);
        Charge.Update(snapshot, _supportsOverride, _reportsOverrideEnd);

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
            Settings.Add(new SettingViewModel(setting, _client, Passwords, ReloadSettingsAsync));
    }
}
