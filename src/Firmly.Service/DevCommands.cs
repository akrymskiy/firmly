#if DEBUG
using System.Security.Principal;
using Firmly.Core;

namespace Firmly.Service;

/// <summary>Debug-build-only commands that exercise the gateway directly, bypassing the pipe.</summary>
internal static class DevCommands
{
    public static int Run(string[] args, FirmwareProviderResolver resolver)
    {
        var provider = resolver.Resolve();
        if (provider is null)
        {
            Console.Error.WriteLine("No supported firmware interface found.");
            return 1;
        }

        var gateway = new FirmwareGateway(provider, MachinePolicy.RequireAdminForAllChanges());
        var caller = new FirmwareCaller(new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator));
        try
        {
            if (args[0] == "--dev-set")
            {
                var updated = gateway.SetSetting(args[1], args[2], caller);
                Console.WriteLine($"OK: {updated.Name} = {updated.CurrentValue}");
                return 0;
            }

            var settings = gateway.GetSettings();
            Console.WriteLine($"{gateway.Vendor} settings exposed by policy (rw(A) = administrators only):");
            foreach (var s in settings)
            {
                var options = s.PossibleValues.Count > 0 ? $"  [{string.Join(" | ", s.PossibleValues)}]" : "";
                var access = gateway.CanChange(s.Name, FirmwareCaller.StandardUser) ? "rw   "
                    : provider.Policy.IsWritable(s.Name) ? "rw(A)"
                    : "r    ";
                Console.WriteLine($"  {access} {s.Name} = {s.CurrentValue}{options}");
            }
            return 0;
        }
        catch (Exception ex) when (ex is FirmwarePolicyException or FirmwareWriteException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }
}
#endif
