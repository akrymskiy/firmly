using Firmly.Core.Wmi;

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
            using var wmi = WmiNamespace.Connect(Namespace);
            return true;
        }
        catch (WmiException ex) when (ex.HResult == WmiException.InvalidNamespace)
        {
            return false;
        }
    }

    public IReadOnlyList<FirmwareSetting> GetSettings() => WithAccessCheck(() =>
    {
        using var wmi = WmiNamespace.Connect(Namespace);
        // Querying the base class returns every subclass instance (enumeration, string, integer, ...).
        var results = wmi.Query("SELECT * FROM HP_BIOSSetting");
        try
        {
            return results.Select(ToSetting).OfType<FirmwareSetting>().ToList();
        }
        finally
        {
            foreach (var result in results)
                result.Dispose();
        }
    });

    public void SetSetting(string name, string value, string? biosPassword)
    {
        var status = WithAccessCheck(() =>
        {
            using var wmi = WmiNamespace.Connect(Namespace);
            var interfaces = wmi.Query("SELECT * FROM HP_BIOSSettingInterface");
            try
            {
                var iface = interfaces.FirstOrDefault()
                    ?? throw new FirmwareWriteException("HP_BIOSSettingInterface is not available.", 1);

                using var result = wmi.InvokeMethod(iface, "SetBIOSSetting", new Dictionary<string, string>
                {
                    ["Name"] = name,
                    ["Value"] = value,
                    // HP expects the password with an encoding prefix; with no password set this is just the prefix.
                    ["Password"] = "<utf-16/>" + (biosPassword ?? ""),
                });
                return unchecked((uint)(result.GetInt32("Return") ?? 2));
            }
            finally
            {
                foreach (var item in interfaces)
                    item.Dispose();
            }
        });

        // 6 is also what a missing password gives, once one is set.
        if (status != 0)
            throw new FirmwareWriteException($"HP BIOS rejected '{name}': {DescribeStatus(status)}", status, passwordRejected: status == 6);
    }

    // The BIOS administrator password is HP's "Setup Password"; the power-on password doesn't guard settings.
    // When a model can't say, report none: a change then fails with status 6 if there is one, and Firmly asks for it.
    public bool IsBiosPasswordSet() => WithAccessCheck(() =>
    {
        try
        {
            using var wmi = WmiNamespace.Connect(Namespace);
            var passwords = wmi.Query("SELECT Name, IsSet FROM HP_BIOSPassword WHERE Name = 'Setup Password'");
            try
            {
                return passwords.Any(p => p.GetInt32("IsSet") is 1);
            }
            finally
            {
                foreach (var password in passwords)
                    password.Dispose();
            }
        }
        catch (Exception ex) when (ex is WmiException { IsAccessDenied: false } or InvalidCastException)
        {
            return false;
        }
    });

    // Measured on an EliteBook X Flip G2i (BIOS 01.02.02): the third wrong password in a row locks SetBIOSSetting until
    // a restart, after which even the right one gets status 6. One or two wrong ones followed by the right one are fine,
    // and a success starts the count over. Changes sent without a password count too.
    public BiosPasswordLockout PasswordLockout { get; } = new(MaxConsecutiveFailures: 3);

    // Battery Health Manager's values, as HP's BIOS and documentation name them.
    private static readonly EnumerationChargeModes BatteryHealthManager = new("Battery Health Manager",
    [
        new("Let HP Manage My Battery Health", "hp-let-hp-manage", ChargeModeKind.Adaptive),
        new("Maximize Battery Health Management", "hp-maximize", ChargeModeKind.Limited, LimitPercent: 80),
        new("Minimize Battery Health Management", "hp-minimize", ChargeModeKind.Full),
    ]);

    public ChargeModes? GetChargeModes(IReadOnlyList<FirmwareSetting> settings) => BatteryHealthManager.From(settings);

    // Measured: with Maximize, full-charge capacity is reported as exactly 80% of the real one.
    // Let HP Manage and Minimize report real readings.
    public double? GetReportedChargeLimit(IReadOnlyList<FirmwareSetting> settings) =>
        GetChargeModes(settings)?.Current is { Kind: ChargeModeKind.Limited, LimitPercent: { } limit } ? limit / 100.0 : null;

    private static FirmwareSetting? ToSetting(WmiObject obj)
    {
        // The BIOS reports a nameless placeholder instance; skip it.
        if (obj.GetString("Name") is not { Length: > 0 } name)
            return null;

        // Concrete classes are HPBIOS_BIOSEnumeration, HPBIOS_BIOSString, etc.
        var className = obj.ClassName;
        FirmwareSettingKind kind;
        IReadOnlyList<string> possibleValues = [];
        string value;

        if (className.EndsWith("Enumeration", StringComparison.Ordinal))
        {
            kind = FirmwareSettingKind.Enumeration;
            value = obj.GetString("CurrentValue") ?? "";
            possibleValues = obj.GetStrings("PossibleValues") ?? [];
        }
        else if (className.EndsWith("OrderedList", StringComparison.Ordinal))
        {
            kind = FirmwareSettingKind.OrderedList;
            value = obj.GetString("Value") ?? "";
            possibleValues = obj.GetStrings("Elements") ?? [];
        }
        else if (className.EndsWith("Integer", StringComparison.Ordinal))
        {
            kind = FirmwareSettingKind.Integer;
            value = obj.GetString("Value") ?? "";
        }
        else if (className.EndsWith("String", StringComparison.Ordinal))
        {
            kind = FirmwareSettingKind.String;
            value = obj.GetString("Value") ?? "";
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

        return new FirmwareSetting(name, kind, value, possibleValues, obj.GetInt32("IsReadOnly") is { } readOnly and not 0);
    }

    // Non-admin callers can connect to the namespace but are denied on enumeration and method calls.
    private static T WithAccessCheck<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (WmiException ex) when (ex.IsAccessDenied)
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
