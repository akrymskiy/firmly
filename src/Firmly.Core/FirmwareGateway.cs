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

    /// <summary>The charge modes the firmware offers; null when it offers none Firmly knows.</summary>
    public ChargeModes? GetChargeModes() => provider.GetChargeModes(GetSettings());

    /// <summary>What <paramref name="caller"/> may do with <paramref name="modes"/>: the most limited access among their settings.</summary>
    /// <param name="settings">From <see cref="GetSettings"/>, so firmware isn't read twice.</param>
    public FirmwareChangeAccess GetChargeModeAccess(ChargeModes modes, IReadOnlyList<FirmwareSetting> settings, FirmwareCaller caller)
    {
        var access = FirmwareChangeAccess.Allowed;
        foreach (var name in modes.SettingNames)
        {
            var settingAccess = settings.FirstOrDefault(s => s.Name == name) is { } setting
                ? GetChangeAccess(setting, caller)
                : FirmwareChangeAccess.ReadOnly;
            if (settingAccess == FirmwareChangeAccess.ReadOnly)
                return FirmwareChangeAccess.ReadOnly;
            if (settingAccess == FirmwareChangeAccess.AdministratorRequired)
                access = FirmwareChangeAccess.AdministratorRequired;
        }
        return access;
    }

    /// <summary>Switches to the charge mode <paramref name="id"/>, one setting at a time, each checked like <see cref="SetSetting"/>.</summary>
    /// <returns>The charge modes as read back from firmware after the change.</returns>
    /// <exception cref="FirmwareAccessDeniedException">Only administrators may change this.</exception>
    /// <exception cref="FirmwarePolicyException">There's no such mode, or the change is not allowed.</exception>
    /// <exception cref="FirmwareWriteException">The firmware rejected the change, or did not apply it.</exception>
    public ChargeModes SetChargeMode(string id, FirmwareCaller caller, string? biosPassword = null)
    {
        var mode = GetChargeModes()?.Find(id)
            ?? throw new FirmwarePolicyException($"'{id}' is not a charge mode on this device.");

        foreach (var change in mode.Changes)
            SetSetting(change.Name, change.Value, caller, biosPassword);

        var updated = GetChargeModes()
            ?? throw new FirmwareWriteException("The charge modes disappeared after changing them.", 0);
        if (updated.CurrentId != id)
            throw new FirmwareWriteException($"The firmware doesn't report '{mode.Label}' after choosing it.", 0);

        return updated;
    }

    private bool RequiresAdmin(string name) =>
        requireAdminForAllChanges || provider.Policy.RequiresAdmin(name);

    private FirmwareSetting? Find(string name) =>
        provider.Policy.IsReadable(name)
            ? provider.GetSettings().FirstOrDefault(s => s.Name == name)
            : null;
}
