using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Firmly.App;

public sealed partial class AboutDialog : ContentDialog
{
    private readonly AboutInfo _info;

    internal AboutDialog(AboutInfo info)
    {
        InitializeComponent();
        _info = info;

        VersionText.Text = $"Version {AboutInfo.AppVersion}";
        ServiceText.Text = info.ServiceSummary;
        DeviceText.Text = info.Device;
        BiosText.Text = info.BiosVersion ?? "Unknown";
        WindowsText.Text = info.WindowsVersion;
        SourceLink.NavigateUri = new(AboutInfo.RepositoryUrl);
        ReportLink.NavigateUri = info.NewIssueUri;
        LicenseLink.NavigateUri = new(AboutInfo.LicenseUrl);
        LegalText.Text = $"{AboutInfo.Copyright}. Firmly is not affiliated with HP Inc. or any other hardware vendor. "
            + "Changing firmware settings is at your own risk.";
    }

    private void OnCopyDetailsClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var package = new DataPackage();
        package.SetText(_info.Details);
        Clipboard.SetContent(package);

        // Stay open; just confirm.
        args.Cancel = true;
        SecondaryButtonText = "Copied";
    }
}
