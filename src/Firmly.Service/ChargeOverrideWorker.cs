namespace Firmly.Service;

/// <summary>Regularly asks <see cref="RequestHandler"/> to switch a charge override back once it has ended.</summary>
internal sealed class ChargeOverrideWorker(ILogger<ChargeOverrideWorker> logger, RequestHandler handler, TimeProvider time)
    : BackgroundService
{
    // Comparing wall-clock time each round means a laptop that slept through the deadline switches back
    // within this long of waking. Firmware is only read while an override is active.
    public static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let host startup finish before touching WMI.
        await Task.Yield();
        while (true)
        {
            try
            {
                await handler.CheckChargeOverrideAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Never stop the service over this; try again next round.
                logger.LogError(ex, "Unexpected error checking the charge override.");
            }

            await Task.Delay(CheckInterval, time, stoppingToken);
        }
    }
}
