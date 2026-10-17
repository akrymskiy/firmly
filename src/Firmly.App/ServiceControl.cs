using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;

namespace Firmly.App;

public enum ServiceRestartResult
{
    Restarted,
    /// <summary>The user declined the UAC prompt.</summary>
    Cancelled,
    /// <summary>No Firmly Windows service on this PC, e.g. a dev build with the service in a console window.</summary>
    NotInstalled,
    Failed,
}

/// <summary>Starts and stops the Firmly Windows service. The app isn't elevated, so each action asks for UAC.</summary>
internal static class ServiceControl
{
    private const string ServiceName = "Firmly";

    public static bool IsInstalled()
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{ServiceName}");
        return key is not null;
    }

    /// <summary>Restarts the service, or starts it if it's stopped.</summary>
    public static async Task<ServiceRestartResult> RestartAsync()
    {
        if (!IsInstalled())
            return ServiceRestartResult.NotInstalled;

        // net waits for each step to finish. Stopping fails harmlessly when the service isn't running, and the
        // exit code is then that of starting it. Full paths, since this runs elevated.
        var net = Path.Combine(Environment.SystemDirectory, "net.exe");
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            $"/d /c \"\"{net}\" stop {ServiceName} >nul 2>&1 & \"{net}\" start {ServiceName} >nul 2>&1\"")
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        try
        {
            using var process = Process.Start(start)!;
            await process.WaitForExitAsync();
            return process.ExitCode == 0 ? ServiceRestartResult.Restarted : ServiceRestartResult.Failed;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED
        {
            return ServiceRestartResult.Cancelled;
        }
    }
}
