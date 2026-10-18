using Firmly.Core;

namespace Firmly.Ipc;

public enum IpcCommand
{
    GetStatus,
    GetSettings,
    SetSetting,

    /// <summary>Switches to the charge mode <see cref="IpcRequest.Name"/>. Answers like <see cref="GetSettings"/>.</summary>
    SetChargeMode,

    /// <summary>
    /// Switches to the charge mode <see cref="IpcRequest.Name"/> until <see cref="IpcRequest.Until"/>, then to
    /// <see cref="IpcRequest.Value"/>. Answers like <see cref="GetSettings"/>.
    /// </summary>
    SetChargeOverride,

    /// <summary>
    /// Ends the charge override now, switching to the mode it would have switched back to.
    /// Answers like <see cref="GetSettings"/>.
    /// </summary>
    CancelChargeOverride,
}

/// <param name="Name">Setting name for <see cref="IpcCommand.SetSetting"/>; charge mode id for the charge mode commands.</param>
/// <param name="Value">
/// New value for <see cref="IpcCommand.SetSetting"/>; the mode to switch back to for <see cref="IpcCommand.SetChargeOverride"/>.
/// </param>
/// <param name="BiosPassword">
/// Only needed when a BIOS administrator password is set. Never logged; kept only as <paramref name="KeepBiosPassword"/> says.
/// </param>
/// <param name="Until">When a <see cref="IpcCommand.SetChargeOverride"/> ends.</param>
/// <param name="KeepBiosPassword">
/// For <see cref="IpcCommand.SetChargeOverride"/>: keep <paramref name="BiosPassword"/>, encrypted for the service alone,
/// until the override ends, so the service can switch back unattended. Refused when the machine policy forbids it.
/// </param>
public sealed record IpcRequest(
    IpcCommand Command,
    string? Name = null,
    string? Value = null,
    string? BiosPassword = null,
    DateTimeOffset? Until = null,
    bool KeepBiosPassword = false)
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
    /// <summary>The caller sent too many requests, or firmware was changed too often; try again shortly.</summary>
    TooManyRequests,
    ServiceError,
    /// <summary>Client side only: the service is not running or did not answer.</summary>
    ServiceUnavailable,
    /// <summary>Client side only: the service speaks a different <see cref="IpcConstants.ProtocolVersion"/>.</summary>
    IncompatibleService,
    /// <summary>
    /// The BIOS administrator password was missing or wrong; or, with <see cref="IpcResponse.BiosPasswordTriesLeft"/> 0,
    /// the BIOS refuses changes until the PC restarts after too many wrong ones, and the service didn't send it this one.
    /// </summary>
    BiosPasswordRequired,
}

/// <param name="Vendor">Null when this machine has no supported firmware interface.</param>
/// <param name="IsAdministrator">Whether the service treats the caller as an administrator.</param>
/// <param name="RequireAdminForAllChanges">Machine policy restricting every change to administrators.</param>
/// <param name="ServiceVersion">The service's build, e.g. "0.1.0+27ce7e2"; null from services that predate it.</param>
/// <param name="ProtocolVersion">The service's <see cref="IpcConstants.ProtocolVersion"/>; 0 from services that predate it.</param>
/// <param name="Features">What the service can do, from <see cref="IpcFeatures"/>. The app shows only what is listed.</param>
/// <param name="AllowSavedBiosPassword">
/// False when the machine policy forbids keeping the BIOS password anywhere: neither the service nor the app may.
/// </param>
public sealed record ServiceStatus(
    string? Vendor,
    bool IsAdministrator,
    bool RequireAdminForAllChanges,
    string? ServiceVersion = null,
    int ProtocolVersion = 0,
    IReadOnlyList<string>? Features = null,
    bool AllowSavedBiosPassword = true);

/// <param name="Access">What the caller may do with this setting.</param>
public sealed record SettingInfo(FirmwareSetting Setting, FirmwareChangeAccess Access);

/// <summary>A charge mode chosen for a while, after which the service switches back by itself.</summary>
/// <param name="ModeId">The mode on until <paramref name="Until"/>.</param>
/// <param name="RestoreModeId">The mode the service switches to at <paramref name="Until"/>.</param>
/// <param name="KeepsBiosPassword">The service holds the BIOS password to switch back with.</param>
/// <param name="WaitingForPassword">
/// The time is up, but switching back needs the BIOS password: send <see cref="IpcCommand.CancelChargeOverride"/> with it.
/// </param>
public sealed record ChargeOverride(
    string ModeId, string RestoreModeId, DateTimeOffset Until, bool KeepsBiosPassword = false, bool WaitingForPassword = false);

/// <summary>
/// How the last charge override ended, when the service ended it by itself; kept until another override starts, so
/// the app can say so even if it wasn't running then.
/// </summary>
/// <param name="ModeId">The mode that was on for a while.</param>
/// <param name="RestoreModeId">The mode the service switched back to, or meant to.</param>
/// <param name="EndedAt">When the service switched back, or found it couldn't.</param>
/// <param name="SwitchedBack">False when the firmware no longer offered <paramref name="RestoreModeId"/>, so <paramref name="ModeId"/> stayed on.</param>
public sealed record ChargeOverrideEnd(string ModeId, string RestoreModeId, DateTimeOffset EndedAt, bool SwitchedBack = true);

/// <param name="ReportedChargeLimit">See <see cref="BatteryCharge"/>; null when Windows sees real battery readings.</param>
/// <param name="ChargeModes">Null when the firmware offers none Firmly knows, or the service predates them.</param>
/// <param name="ChargeModeAccess">What the caller may do with <paramref name="ChargeModes"/>.</param>
/// <param name="ChargeOverride">The charge mode chosen for a while, if any.</param>
/// <param name="LastOverrideEnd">How the last one ended, if the service ended it by itself.</param>
/// <param name="BiosPasswordSet">A BIOS administrator password is set, so changes need it.</param>
/// <param name="BiosPasswordTriesLeft">See <see cref="IpcResponse.BiosPasswordTriesLeft"/>.</param>
public sealed record FirmwareSnapshot(
    IReadOnlyList<SettingInfo> Settings,
    double? ReportedChargeLimit,
    ChargeModes? ChargeModes = null,
    FirmwareChangeAccess ChargeModeAccess = FirmwareChangeAccess.ReadOnly,
    ChargeOverride? ChargeOverride = null,
    bool BiosPasswordSet = false,
    int? BiosPasswordTriesLeft = null,
    ChargeOverrideEnd? LastOverrideEnd = null);

/// <summary>Carries the result for the command that was sent, or an <see cref="Error"/>.</summary>
/// <param name="ReportedChargeLimit">
/// With <see cref="Settings"/>: the charge limit at which the firmware rescales Windows' battery readings, if any.
/// </param>
/// <param name="ChargeModes">With <see cref="Settings"/>, as in <see cref="FirmwareSnapshot"/>, like the three after it and <paramref name="LastOverrideEnd"/>.</param>
/// <param name="BiosPasswordTriesLeft">
/// How many more wrong BIOS passwords in a row the firmware takes before it refuses changes until the PC restarts; 0 once
/// it does. Null when the service doesn't know how this firmware locks. Sent with settings and with
/// <see cref="IpcError.BiosPasswordRequired"/>.
/// </param>
public sealed record IpcResponse(
    IpcError Error = IpcError.None,
    string? Message = null,
    ServiceStatus? Status = null,
    IReadOnlyList<SettingInfo>? Settings = null,
    SettingInfo? Setting = null,
    uint? VendorCode = null,
    double? ReportedChargeLimit = null,
    ChargeModes? ChargeModes = null,
    FirmwareChangeAccess? ChargeModeAccess = null,
    ChargeOverride? ChargeOverride = null,
    bool BiosPasswordSet = false,
    int? BiosPasswordTriesLeft = null,
    ChargeOverrideEnd? LastOverrideEnd = null)
{
    public static IpcResponse Fail(IpcError error, string message, uint? vendorCode = null) =>
        new(error, message, VendorCode: vendorCode);
}
