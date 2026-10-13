namespace Firmly.Core;

/// <summary>
/// Vendor-specific access to firmware settings (HP, and later Lenovo, Dell, ...).
/// Implementations typically require administrator rights and run inside Firmly.Service.
/// Providers do raw access only; callers go through <see cref="FirmwareGateway"/> to enforce <see cref="Policy"/>.
/// </summary>
public interface IFirmwareProvider
{
    string Vendor { get; }

    /// <summary>Which settings Firmly may read and write on this vendor's hardware.</summary>
    FirmwarePolicy Policy { get; }

    /// <summary>Returns true when this machine exposes the vendor's firmware interface.</summary>
    bool IsSupported();

    /// <summary>Returns all settings except BIOS passwords.</summary>
    IReadOnlyList<FirmwareSetting> GetSettings();

    /// <exception cref="FirmwareWriteException">The firmware rejected the change.</exception>
    void SetSetting(string name, string value, string? biosPassword);
}
