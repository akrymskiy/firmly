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

    /// <summary>
    /// The charge limit (e.g. 0.8) at which the firmware rescales the battery readings Windows shows, given the
    /// current settings; null when Windows sees real readings. See <see cref="BatteryCharge"/>.
    /// </summary>
    double? GetReportedChargeLimit(IReadOnlyList<FirmwareSetting> settings);
}
