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
        using var searcher = new ManagementObjectSearcher(
            new ManagementScope(Namespace),
            new ObjectQuery("SELECT Name, CurrentValue, PossibleValues, IsReadOnly FROM HP_BIOSEnumeration"));

        var settings = new List<FirmwareSetting>();
        try
        {
            foreach (var obj in searcher.Get())
            {
                using (obj)
                {
                    // The BIOS reports a nameless placeholder instance; skip it.
                    if (obj["Name"] is not string name || name.Length == 0)
                        continue;

                    settings.Add(new FirmwareSetting(
                        Name: name,
                        Kind: FirmwareSettingKind.Enumeration,
                        CurrentValue: (string?)obj["CurrentValue"] ?? "",
                        PossibleValues: (string[]?)obj["PossibleValues"] ?? [],
                        IsReadOnly: Convert.ToUInt32(obj["IsReadOnly"]) != 0));
                }
            }
        }
        // Non-admin callers can connect to the namespace but are denied when enumerating.
        catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.AccessDenied)
        {
            throw new UnauthorizedAccessException("Reading HP BIOS settings requires administrator rights.", ex);
        }
        return settings;
    }
}
