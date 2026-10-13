namespace Firmly.Core;

/// <summary>
/// The only path from Firmly.Service to a provider: applies the provider's <see cref="FirmwarePolicy"/>
/// to every read and write, and verifies writes by reading the setting back.
/// </summary>
/// <param name="requireAdminForAllChanges">
/// Machine-wide lockdown: when true, only administrators may change anything, including user-writable settings.
/// </param>
public sealed class FirmwareGateway(IFirmwareProvider provider, bool requireAdminForAllChanges = false)
{
    public string Vendor => provider.Vendor;

    public IReadOnlyList<FirmwareSetting> GetSettings() =>
        provider.Policy.FilterReadable(provider.GetSettings());

    /// <summary>Whether <paramref name="caller"/> may change <paramref name="name"/>, before checking firmware.</summary>
    public bool CanChange(string name, FirmwareCaller caller) =>
        provider.Policy.IsWritable(name) && (caller.IsAdministrator || !RequiresAdmin(name));

    /// <summary>What <paramref name="caller"/> may do with <paramref name="setting"/>, for showing it in the app.</summary>
    public FirmwareChangeAccess GetChangeAccess(FirmwareSetting setting, FirmwareCaller caller) =>
        !provider.Policy.IsWritable(setting.Name) || setting.IsReadOnly || setting.Kind != FirmwareSettingKind.Enumeration
            ? FirmwareChangeAccess.ReadOnly
            : CanChange(setting.Name, caller)
                ? FirmwareChangeAccess.Allowed
                : FirmwareChangeAccess.AdministratorRequired;

    /// <returns>The setting as read back from firmware after the change.</returns>
    /// <exception cref="FirmwareAccessDeniedException">Only administrators may change this setting.</exception>
    /// <exception cref="FirmwarePolicyException">The change is not allowed.</exception>
    /// <exception cref="FirmwareWriteException">The firmware rejected the change, or did not apply it.</exception>
    public FirmwareSetting SetSetting(string name, string value, FirmwareCaller caller, string? biosPassword = null)
    {
        // Reject before touching firmware at all.
        if (!provider.Policy.IsWritable(name))
            throw new FirmwarePolicyException($"'{name}' is not on the write allowlist.");

        if (!CanChange(name, caller))
            throw new FirmwareAccessDeniedException(requireAdminForAllChanges
                ? $"Changing '{name}' requires an administrator: this machine's policy restricts all changes to administrators."
                : $"Changing '{name}' requires an administrator.");

        var current = Find(name)
            ?? throw new FirmwarePolicyException($"'{name}' is not available on this machine.");

        provider.Policy.EnsureCanWrite(current, value);

        if (current.CurrentValue == value)
            return current;

        provider.SetSetting(name, value, biosPassword);

        var updated = Find(name)
            ?? throw new FirmwareWriteException($"'{name}' disappeared after writing it.", 0);
        if (updated.CurrentValue != value)
            throw new FirmwareWriteException($"'{name}' still reads '{updated.CurrentValue}' after setting '{value}'.", 0);

        return updated;
    }

    private bool RequiresAdmin(string name) =>
        requireAdminForAllChanges || provider.Policy.RequiresAdmin(name);

    private FirmwareSetting? Find(string name) =>
        provider.Policy.IsReadable(name)
            ? provider.GetSettings().FirstOrDefault(s => s.Name == name)
            : null;
}
