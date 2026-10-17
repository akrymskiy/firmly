using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Windows.ApplicationModel;
using Windows.ApplicationModel.DataTransfer;

namespace Firmly.App.ViewModels;

/// <summary>The Settings page: Firmly's own preferences, the BIOS password, the service, and About.</summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly SessionViewModel _session;

    public SettingsViewModel(SessionViewModel session)
    {
        _session = session;
        _session.PropertyChanged += OnSessionChanged;
        BiosPasswordVault.Changed += OnVaultChanged;
    }

    /// <summary>Stops following the session and the saved password; call when the page goes away.</summary>
    public void Detach()
    {
        _session.PropertyChanged -= OnSessionChanged;
        BiosPasswordVault.Changed -= OnVaultChanged;
    }

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SessionViewModel.BiosPasswordSet) or nameof(SessionViewModel.AllowSavedBiosPassword))
        {
            OnPropertyChanged(nameof(ShowsBiosPassword));
            OnPropertyChanged(nameof(AllowSavedPassword));
            OnPropertyChanged(nameof(SavedPasswordText));
        }
    }

    private void OnVaultChanged() => App.DispatcherQueue.TryEnqueue(() => HasSavedPassword = BiosPasswordVault.HasSaved);

    // --- BIOS password ---

    /// <summary>Shown when the BIOS has an administrator password, or one is still saved from before.</summary>
    public bool ShowsBiosPassword => _session.BiosPasswordSet || HasSavedPassword;

    public bool AllowSavedPassword => _session.AllowSavedBiosPassword;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsBiosPassword), nameof(SavedPasswordText))]
    public partial bool HasSavedPassword { get; set; } = BiosPasswordVault.HasSaved;

    public string SavedPasswordText =>
        HasSavedPassword ? "Saved on this PC, unlocked with Windows Hello."
        : !_session.AllowSavedBiosPassword ? "Your organization doesn't allow saving it, so Firmly asks for it each time."
        : "Not saved. To save it, choose \u201cRemember it\u201d when Firmly asks for it.";

    [RelayCommand]
    private Task ForgetPasswordAsync() => BiosPasswordVault.ForgetAsync();

    /// <summary>When a timed full charge ends: 0 asks for the password, 1 lets the service keep it and switch back by itself.</summary>
    [ObservableProperty]
    public partial int SwitchBackChoice { get; set; } = AppSettings.SwitchBackByItself ? 1 : 0;

    partial void OnSwitchBackChoiceChanged(int value) => AppSettings.SwitchBackByItself = value == 1;

    // --- General ---
    // Set while showing the startup task's state, so that doesn't count as the user flipping the switch.
    private bool _showingStartup;

    /// <summary>Whether there's a notification area icon to close to.</summary>
    public bool HasTray => App.HasTray;

    [ObservableProperty]
    public partial bool CloseToTray { get; set; } = AppSettings.CloseToTray;

    partial void OnCloseToTrayChanged(bool value) => AppSettings.CloseToTray = value;

    [ObservableProperty]
    public partial bool StartAtSignIn { get; set; }

    partial void OnStartAtSignInChanged(bool value)
    {
        if (!_showingStartup)
            _ = ApplyStartAtSignInAsync(value);
    }

    /// <summary>False when Task Manager or a policy decides, not Firmly.</summary>
    [ObservableProperty]
    public partial bool CanChangeStartAtSignIn { get; set; } = true;

    /// <summary>Who turned starting at sign-in on or off, when it wasn't Firmly; empty otherwise.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStartupNote))]
    public partial string StartupNote { get; set; } = "";

    public bool HasStartupNote => StartupNote.Length > 0;

    public string AppVersion => $"Version {AboutInfo.AppVersion}";

    public string Legal => $"{AboutInfo.Copyright}. Firmly is not affiliated with HP Inc. or any other hardware vendor. "
        + "Changing firmware settings is at your own risk.";

    public Uri HelpUri { get; } = new(AboutInfo.HelpUrl);
    public Uri SourceUri { get; } = new(AboutInfo.RepositoryUrl);
    public Uri LicenseUri { get; } = new(AboutInfo.LicenseUrl);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ServiceSummary), nameof(Device), nameof(Bios), nameof(WindowsVersion), nameof(NewIssueUri))]
    internal partial AboutInfo? Info { get; set; }

    public string ServiceSummary => Info?.ServiceSummary ?? "Checking…";
    public string Device => Info?.Device ?? "";
    public string Bios => Info?.BiosVersion ?? "Unknown";
    public string WindowsVersion => Info?.WindowsVersion ?? "";
    public Uri NewIssueUri => Info?.NewIssueUri ?? new($"{AboutInfo.RepositoryUrl}/issues/new");

    /// <summary>Why the last restart didn't work; empty otherwise.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasServiceProblem))]
    public partial string ServiceProblem { get; set; } = "";

    public bool HasServiceProblem => ServiceProblem.Length > 0;

    [ObservableProperty]
    public partial string CopyLabel { get; set; } = "Copy details";

    [RelayCommand]
    private async Task LoadAsync()
    {
        // Out of the page's Loaded event first: StartupTask makes a cross-process call that pumps messages, and
        // XAML re-entered from inside Loaded ends the process (0xc000027b in Microsoft.UI.Xaml.dll).
        await Task.Yield();
        ShowStartAtSignIn((await StartupTask.GetAsync(AppSettings.StartupTaskId)).State);
        Info = await AboutInfo.LoadAsync(_session.Client);
    }

    private async Task ApplyStartAtSignInAsync(bool on)
    {
        var task = await StartupTask.GetAsync(AppSettings.StartupTaskId);
        if (on)
        {
            ShowStartAtSignIn(await task.RequestEnableAsync());
            return;
        }

        task.Disable();
        ShowStartAtSignIn(task.State);
    }

    private void ShowStartAtSignIn(StartupTaskState state)
    {
        _showingStartup = true;
        StartAtSignIn = state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        _showingStartup = false;
        CanChangeStartAtSignIn = state is StartupTaskState.Enabled or StartupTaskState.Disabled;
        StartupNote = state switch
        {
            StartupTaskState.DisabledByUser => "Turned off in Task Manager under Startup apps. Turn it on there.",
            StartupTaskState.DisabledByPolicy => "Turned off by your organization.",
            StartupTaskState.EnabledByPolicy => "Turned on by your organization.",
            _ => "",
        };
    }

    [RelayCommand]
    private async Task RestartServiceAsync()
    {
        ServiceProblem = "";
        Info = Info is null ? null : Info with { Service = null, ServiceProblem = "Restarting…" };
        ServiceProblem = await _session.RestartServiceAsync() ?? "";
        await LoadAsync();
    }

    [RelayCommand]
    private void CopyDetails()
    {
        if (Info is null)
            return;

        var package = new DataPackage();
        package.SetText(Info.Details);
        Clipboard.SetContent(package);
        CopyLabel = "Copied";
    }
}
