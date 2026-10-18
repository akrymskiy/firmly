namespace Firmly.Ipc;

/// <summary>
/// Shared contract between Firmly.App (non-elevated) and Firmly.Service (LocalSystem).
/// The service exposes a narrow command set (<see cref="IpcCommand"/>) over this named pipe; it never accepts arbitrary WMI.
/// </summary>
public static class IpcConstants
{
    public const string PipeName = "Firmly.Service";

    /// <summary>
    /// Bumped only for changes an older app or service would misread. Adding a command, or a field that may be
    /// missing, needs a new <see cref="IpcFeatures"/> entry instead.
    /// </summary>
    public const int ProtocolVersion = 1;
}

/// <summary>Names a service lists in <see cref="ServiceStatus.Features"/>.</summary>
public static class IpcFeatures
{
    /// <summary>Reading and changing firmware settings: <see cref="IpcCommand.GetSettings"/> and <see cref="IpcCommand.SetSetting"/>.</summary>
    public const string Firmware = "firmware";

    /// <summary>Charge modes in settings responses, and <see cref="IpcCommand.SetChargeMode"/>.</summary>
    public const string ChargeModes = "chargeModes";

    /// <summary><see cref="IpcCommand.SetChargeOverride"/> and <see cref="IpcCommand.CancelChargeOverride"/>.</summary>
    public const string ChargeOverride = "chargeOverride";

    /// <summary>
    /// <see cref="FirmwareSnapshot.BiosPasswordSet"/>, <see cref="IpcError.BiosPasswordRequired"/>, and the BIOS password
    /// with every change, including keeping it for a charge override (<see cref="IpcRequest.KeepBiosPassword"/>).
    /// </summary>
    public const string BiosPassword = "biosPassword";

    /// <summary>
    /// <see cref="IpcResponse.BiosPasswordTriesLeft"/>, for firmware whose lockout after wrong passwords Firmly knows.
    /// </summary>
    public const string BiosPasswordLockout = "biosPasswordLockout";
}
