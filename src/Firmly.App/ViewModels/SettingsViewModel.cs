using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Windows.ApplicationModel.DataTransfer;

namespace Firmly.App.ViewModels;

/// <summary>The Settings page: the service, and About.</summary>
public partial class SettingsViewModel(SessionViewModel session) : ObservableObject
{
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
    private async Task LoadAsync() => Info = await AboutInfo.LoadAsync(session.Client);

    [RelayCommand]
    private async Task RestartServiceAsync()
    {
        ServiceProblem = "";
        Info = Info is null ? null : Info with { Service = null, ServiceProblem = "Restarting…" };
        ServiceProblem = await session.RestartServiceAsync() ?? "";
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
