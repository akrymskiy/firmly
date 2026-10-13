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
    public static bool RequireAdminForAllChanges()
    {
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = hklm.OpenSubKey(KeyPath);
        return key?.GetValue("RequireAdminForAllChanges") is int value && value != 0;
    }
}
