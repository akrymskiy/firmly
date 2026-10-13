namespace Firmly.Core;

/// <summary>
/// The only path from Firmly.Service to a provider: applies the provider's <see cref="FirmwarePolicy"/>
/// to every read and write, and verifies writes by reading the setting back.
/// </summary>
public sealed class FirmwareGateway(IFirmwareProvider provider)
{
    public string Vendor => provider.Vendor;

    public IReadOnlyList<FirmwareSetting> GetSettings() =>
        provider.Policy.FilterReadable(provider.GetSettings());

    /// <returns>The setting as read back from firmware after the change.</returns>
    /// <exception cref="FirmwarePolicyException">The change is not allowed.</exception>
    /// <exception cref="FirmwareWriteException">The firmware rejected the change, or did not apply it.</exception>
    public FirmwareSetting SetSetting(string name, string value, string? biosPassword = null)
    {
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

    private FirmwareSetting? Find(string name) =>
        provider.Policy.IsReadable(name)
            ? provider.GetSettings().FirstOrDefault(s => s.Name == name)
            : null;
}
