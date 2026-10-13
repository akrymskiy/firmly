#if DEBUG
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

        var gateway = new FirmwareGateway(provider);
        try
        {
            if (args[0] == "--dev-set")
            {
                var updated = gateway.SetSetting(args[1], args[2]);
                Console.WriteLine($"OK: {updated.Name} = {updated.CurrentValue}");
                return 0;
            }

            var settings = gateway.GetSettings();
            Console.WriteLine($"{gateway.Vendor} settings exposed by policy:");
            foreach (var s in settings)
            {
                var options = s.PossibleValues.Count > 0 ? $"  [{string.Join(" | ", s.PossibleValues)}]" : "";
                var access = provider.Policy.IsWritable(s.Name) ? "rw" : "r ";
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
