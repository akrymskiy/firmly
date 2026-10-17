using Microsoft.Win32;

namespace Firmly.Service;

/// <summary>
/// Machine-wide settings under HKLM\SOFTWARE\Policies\Firmly, which only administrators or Group Policy can set.
/// Read on every request so changes apply without restarting the service.
/// </summary>
internal static class MachinePolicy
{
    private const string KeyPath = @"SOFTWARE\Policies\Firmly";

    /// <summary>DWORD "RequireAdminForAllChanges" = 1: standard users cannot change any setting.</summary>
    public static bool RequireAdminForAllChanges() => IsSet("RequireAdminForAllChanges");

    /// <summary>
    /// DWORD "DisallowSavedBiosPassword" = 1: the BIOS password may not be kept anywhere, neither by the service for a
    /// timed charge nor by the app (with Windows Hello); it must be typed each time.
    /// </summary>
    public static bool AllowSavedBiosPassword() => !IsSet("DisallowSavedBiosPassword");

    private static bool IsSet(string name)
    {
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = hklm.OpenSubKey(KeyPath);
        return key?.GetValue(name) is int value && value != 0;
    }
}
