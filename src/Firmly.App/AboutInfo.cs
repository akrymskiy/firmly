using System.Reflection;
using System.Text;
using Firmly.Ipc;
using Microsoft.Win32;

namespace Firmly.App;

/// <summary>What About and bug reports say about this copy of Firmly, its service and the device.</summary>
/// <param name="Service">Null when the service couldn't be reached; see <paramref name="ServiceProblem"/>.</param>
internal sealed record AboutInfo(
    string? Manufacturer, string? Model, string? BiosVersion, string WindowsVersion, ServiceStatus? Service, string? ServiceProblem)
{
    public const string RepositoryUrl = "https://github.com/akrymskiy/firmly";
    public const string HelpUrl = RepositoryUrl + "#readme";
    public const string LicenseUrl = RepositoryUrl + "/blob/main/LICENSE";

    private static readonly Assembly AppAssembly = typeof(AboutInfo).Assembly;

    /// <summary>e.g. "0.1.0 (27ce7e2)".</summary>
    public static string AppVersion { get; } =
        FormatVersion(AppAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    public static string Copyright { get; } = AppAssembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";

    /// <summary>Reads the device from the registry, which works for every vendor and without the service.</summary>
    public static async Task<AboutInfo> LoadAsync(FirmlyClient client)
    {
        ServiceStatus? status = null;
        string? problem = null;
        try
        {
            status = await client.GetStatusAsync();
        }
        catch (FirmlyServiceException ex)
        {
            problem = ex.Message;
        }

        using var bios = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
        using var windows = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var os = Environment.OSVersion.Version;
        var windowsVersion = windows?.GetValue("UBR") is int ubr ? $"{os.Major}.{os.Minor}.{os.Build}.{ubr}" : os.ToString();

        return new(Read(bios, "SystemManufacturer"), Read(bios, "SystemProductName"), Read(bios, "BIOSVersion"),
            windowsVersion, status, problem);
    }

    /// <summary>e.g. "HP EliteBook 840 G10 Notebook PC".</summary>
    public string Device => (Manufacturer, Model) switch
    {
        (null, null) => Text.Get("About_UnknownDevice"),
        (_, null) => Manufacturer!,
        (null, _) => Model!,
        // HP's product names already start with "HP".
        _ when Model!.StartsWith(Manufacturer!, StringComparison.OrdinalIgnoreCase) => Model,
        _ => $"{Manufacturer} {Model}",
    };

    /// <summary>e.g. "0.1.0 (27ce7e2) · HP firmware", or why the service couldn't be reached.</summary>
    public string ServiceSummary => Service switch
    {
        null => ServiceProblem ?? Text.Get("About_NotConnected"),
        { Vendor: null } s => Text.Format("About_FirmwareNotSupported", FormatVersion(s.ServiceVersion)),
        var s => Text.Format("About_Service", FormatVersion(s.ServiceVersion), s.Vendor),
    };

    /// <summary>The details a bug report needs, as plain text.</summary>
    public string Details
    {
        get
        {
            var text = new StringBuilder();
            text.AppendLine($"Firmly {AppVersion}");
            text.AppendLine($"Service: {ServiceSummary}");
            text.AppendLine($"Device: {Device}");
            text.AppendLine($"BIOS: {BiosVersion ?? "unknown"}");
            text.Append($"Windows {WindowsVersion}");
            return text.ToString();
        }
    }

    /// <summary>A new GitHub issue with <see cref="Details"/> filled in.</summary>
    public Uri NewIssueUri
    {
        get
        {
            var body = $"""
                **What happened?**


                **What did you expect to happen?**


                **Details**
                ```
                {Details}
                ```
                """;
            return new($"{RepositoryUrl}/issues/new?body={Uri.EscapeDataString(body)}");
        }
    }

    /// <summary>Turns an informational version such as "0.1.0+27ce7e2b4f..." into "0.1.0 (27ce7e2)".</summary>
    public static string FormatVersion(string? informational)
    {
        if (string.IsNullOrEmpty(informational))
            return "unknown version";

        var plus = informational.IndexOf('+');
        if (plus < 0)
            return informational;

        var commit = informational[(plus + 1)..];
        return $"{informational[..plus]} ({commit[..Math.Min(7, commit.Length)]})";
    }

    private static string? Read(RegistryKey? key, string name) =>
        (key?.GetValue(name) as string)?.Trim() is { Length: > 0 } value ? value : null;
}
