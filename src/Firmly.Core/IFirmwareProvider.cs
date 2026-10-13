namespace Firmly.Core;

/// <summary>
/// Vendor-specific access to firmware settings (HP, and later Lenovo, Dell, ...).
/// Implementations typically require administrator rights and run inside Firmly.Service.
/// </summary>
public interface IFirmwareProvider
{
    string Vendor { get; }

    /// <summary>Returns true when this machine exposes the vendor's firmware interface.</summary>
    bool IsSupported();

    IReadOnlyList<FirmwareSetting> GetSettings();
}
