namespace Firmly.Ipc;

/// <summary>
/// Shared contract between Firmly.App (non-elevated) and Firmly.Service (LocalSystem).
/// The service exposes a narrow command set over this named pipe; it never accepts arbitrary WMI.
/// </summary>
public static class IpcConstants
{
    public const string PipeName = "Firmly.Service";
}
