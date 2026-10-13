using Firmly.Core;

namespace Firmly.Ipc;

public enum IpcCommand
{
    GetStatus,
    GetSettings,
    SetSetting,
}

/// <param name="Name">Setting name, for <see cref="IpcCommand.SetSetting"/>.</param>
/// <param name="Value">New value, for <see cref="IpcCommand.SetSetting"/>.</param>
/// <param name="BiosPassword">Only needed when a BIOS administrator password is set. Never logged or stored.</param>
public sealed record IpcRequest(IpcCommand Command, string? Name = null, string? Value = null, string? BiosPassword = null)
{
    // Replaces the generated ToString so the password can't end up in a log.
    public override string ToString() => Name is null ? $"{Command}" : $"{Command} '{Name}'";
}

public enum IpcError
{
    None,
    /// <summary>The request was malformed or incomplete.</summary>
    BadRequest,
    /// <summary>This machine has no supported firmware interface.</summary>
    Unsupported,
    /// <summary>Blocked by the allowlist, or the value is not one the firmware offers.</summary>
    NotAllowed,
    /// <summary>Only administrators may make this change.</summary>
    AdministratorRequired,
    /// <summary>The firmware refused the change, for example because of a wrong BIOS password.</summary>
    FirmwareRejected,
    ServiceError,
    /// <summary>Client side only: the service is not running or did not answer.</summary>
    ServiceUnavailable,
}

/// <param name="Vendor">Null when this machine has no supported firmware interface.</param>
/// <param name="IsAdministrator">Whether the service treats the caller as an administrator.</param>
/// <param name="RequireAdminForAllChanges">Machine policy restricting every change to administrators.</param>
public sealed record ServiceStatus(string? Vendor, bool IsAdministrator, bool RequireAdminForAllChanges);

/// <param name="Access">What the caller may do with this setting.</param>
public sealed record SettingInfo(FirmwareSetting Setting, FirmwareChangeAccess Access);

/// <param name="ReportedChargeLimit">See <see cref="BatteryCharge"/>; null when Windows sees real battery readings.</param>
public sealed record FirmwareSnapshot(IReadOnlyList<SettingInfo> Settings, double? ReportedChargeLimit);

/// <summary>Carries the result for the command that was sent, or an <see cref="Error"/>.</summary>
/// <param name="ReportedChargeLimit">
/// With <see cref="Settings"/>: the charge limit at which the firmware rescales Windows' battery readings, if any.
/// </param>
public sealed record IpcResponse(
    IpcError Error = IpcError.None,
    string? Message = null,
    ServiceStatus? Status = null,
    IReadOnlyList<SettingInfo>? Settings = null,
    SettingInfo? Setting = null,
    uint? VendorCode = null,
    double? ReportedChargeLimit = null)
{
    public static IpcResponse Fail(IpcError error, string message, uint? vendorCode = null) =>
        new(error, message, VendorCode: vendorCode);
}
