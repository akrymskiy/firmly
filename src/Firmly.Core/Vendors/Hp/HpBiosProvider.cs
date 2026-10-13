using System.Management;

namespace Firmly.Core.Vendors.Hp;

/// <summary>
/// HP business notebooks expose BIOS settings through WMI in root\hp\instrumentedBIOS.
/// Both reading and writing require administrator rights.
/// </summary>
public sealed class HpBiosProvider : IFirmwareProvider
{
    private const string Namespace = @"root\hp\instrumentedBIOS";

    public string Vendor => "HP";

    public FirmwarePolicy Policy => HpFirmwarePolicy.Default;

    public bool IsSupported()
    {
        try
        {
            var scope = new ManagementScope(Namespace);
            scope.Connect();
            return true;
        }
        catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.InvalidNamespace)
        {
            return false;
        }
    }

    public IReadOnlyList<FirmwareSetting> GetSettings()
    {
        // Querying the base class returns every subclass instance (enumeration, string, integer, ...).
        using var searcher = new ManagementObjectSearcher(
            new ManagementScope(Namespace),
            new ObjectQuery("SELECT * FROM HP_BIOSSetting"));

        var settings = new List<FirmwareSetting>();
        WithAccessCheck(() =>
        {
            using var results = searcher.Get();
            foreach (var obj in results)
            {
                using (obj)
                {
                    if (ToSetting(obj) is { } setting)
                        settings.Add(setting);
                }
            }
        });
        return settings;
    }

    public void SetSetting(string name, string value, string? biosPassword)
    {
        using var searcher = new ManagementObjectSearcher(
            new ManagementScope(Namespace),
            new ObjectQuery("SELECT * FROM HP_BIOSSettingInterface"));

        uint status = 0;
        WithAccessCheck(() =>
        {
            using var results = searcher.Get();
            using var iface = results.Cast<ManagementObject>().FirstOrDefault()
                ?? throw new FirmwareWriteException("HP_BIOSSettingInterface is not available.", 1);

            using var args = iface.GetMethodParameters("SetBIOSSetting");
            args["Name"] = name;
            args["Value"] = value;
            // HP expects the password with an encoding prefix; with no password set this is just the prefix.
            args["Password"] = "<utf-16/>" + (biosPassword ?? "");

            using var result = iface.InvokeMethod("SetBIOSSetting", args, null);
            status = Convert.ToUInt32(result["Return"]);
        });

        if (status != 0)
            throw new FirmwareWriteException($"HP BIOS rejected '{name}': {DescribeStatus(status)}", status);
    }

    // Measured: with Maximize, full-charge capacity is reported as exactly 80% of the real one.
    // Let HP Manage and Minimize report real readings.
    public double? GetReportedChargeLimit(IReadOnlyList<FirmwareSetting> settings) =>
        settings.Any(s => s.Name == "Battery Health Manager" && s.CurrentValue == "Maximize Battery Health Management")
            ? 0.8
            : null;

    private static FirmwareSetting? ToSetting(ManagementBaseObject obj)
    {
        // The BIOS reports a nameless placeholder instance; skip it.
        if (obj["Name"] is not string name || name.Length == 0)
            return null;

        // Concrete classes are HPBIOS_BIOSEnumeration, HPBIOS_BIOSString, etc.
        var className = obj.ClassPath.ClassName;
        FirmwareSettingKind kind;
        IReadOnlyList<string> possibleValues = [];
        string value;

        if (className.EndsWith("Enumeration", StringComparison.Ordinal))
        {
            kind = FirmwareSettingKind.Enumeration;
            value = (string?)obj["CurrentValue"] ?? "";
            possibleValues = (string[]?)obj["PossibleValues"] ?? [];
        }
        else if (className.EndsWith("OrderedList", StringComparison.Ordinal))
        {
            kind = FirmwareSettingKind.OrderedList;
            value = (string?)obj["Value"] ?? "";
            possibleValues = (string[]?)obj["Elements"] ?? [];
        }
        else if (className.EndsWith("Integer", StringComparison.Ordinal))
        {
            kind = FirmwareSettingKind.Integer;
            value = (string?)obj["Value"] ?? "";
        }
        else if (className.EndsWith("String", StringComparison.Ordinal))
        {
            kind = FirmwareSettingKind.String;
            value = (string?)obj["Value"] ?? "";
        }
        else
        {
            // Passwords and unknown types are never exposed.
            return null;
        }

        // HP's placeholder for a setting that doesn't apply right now, e.g. Adaptive Battery Optimizer
        // while Battery Health Manager is on Minimize.
        if (value == "<-Unknown->")
            value = FirmwareSetting.NotApplicable;

        return new FirmwareSetting(name, kind, value, possibleValues, Convert.ToUInt32(obj["IsReadOnly"]) != 0);
    }

    // Non-admin callers can connect to the namespace but are denied on enumeration and method calls.
    private static void WithAccessCheck(Action action)
    {
        try
        {
            action();
        }
        catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.AccessDenied)
        {
            throw new UnauthorizedAccessException("Accessing HP BIOS settings requires administrator rights.", ex);
        }
    }

    // Codes as documented by HP Client Management Script Library.
    private static string DescribeStatus(uint status) => status switch
    {
        1 => "not supported",
        2 => "unspecified error",
        3 => "operation timed out",
        4 => "operation failed or setting name is invalid",
        5 => "invalid parameter",
        6 => "access denied or incorrect BIOS password",
        32768 => "security or password policy not met",
        _ => $"unknown error {status}",
    };
}
