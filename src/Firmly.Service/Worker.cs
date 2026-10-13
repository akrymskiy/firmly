using Firmly.Core;

namespace Firmly.Service;

// Placeholder: detects the firmware provider on startup. The named-pipe server
// (see Firmly.Ipc) that serves Firmly.App will live here.
public class Worker(ILogger<Worker> logger, FirmwareProviderResolver resolver) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var provider = resolver.Resolve();
        if (provider is null)
        {
            logger.LogWarning("No supported firmware interface found on this machine.");
            return Task.CompletedTask;
        }

        try
        {
            var settings = new FirmwareGateway(provider).GetSettings();
            logger.LogInformation("{Vendor} firmware interface detected; {Count} settings exposed.", provider.Vendor, settings.Count);
        }
        catch (UnauthorizedAccessException)
        {
            logger.LogError("Reading {Vendor} firmware settings requires administrator rights.", provider.Vendor);
        }

        return Task.CompletedTask;
    }
}
