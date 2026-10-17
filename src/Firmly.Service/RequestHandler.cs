using System.Collections.Concurrent;
using System.Reflection;

using Firmly.Core;
using Firmly.Ipc;

namespace Firmly.Service;

/// <summary>Turns pipe requests into gateway calls. Every firmware access goes through <see cref="FirmwareGateway"/>.</summary>
/// <param name="requireAdminForAllChanges">Reads the machine lockdown policy; called on every request.</param>
internal sealed class RequestHandler(
    ILogger<RequestHandler> logger,
    FirmwareProviderResolver resolver,
    Func<bool> requireAdminForAllChanges,
    TimeProvider time)
{
    // Requests per account: the app makes two at start-up and a few a second at most while someone clicks around.
    // A UI bug once sent 37,000 requests in 90 seconds; the excess is refused before touching firmware.
    public const int RequestBurst = 30;
    public static readonly TimeSpan RequestInterval = TimeSpan.FromMilliseconds(200);

    // Repeats of the same warning within this window are counted rather than logged.
    private static readonly TimeSpan WarningWindow = TimeSpan.FromMinutes(1);

    // Longer names or values than any firmware uses are refused, so they can't bloat the log either.
    private const int MaxTextLength = 256;

    // e.g. "0.1.0+27ce7e2...": the version plus the commit it was built from.
    private static readonly string ServiceVersion =
        typeof(RequestHandler).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    private static readonly string[] Features = [IpcFeatures.Firmware];

    // One firmware operation at a time, so a read can't interleave with another client's write and its read-back.
    private readonly SemaphoreSlim _firmwareLock = new(1, 1);
    private readonly Lazy<IFirmwareProvider?> _provider = new(
        () => resolver.Resolve() is { } provider ? new WriteLimitedProvider(provider, time) : null);
    private readonly ConcurrentDictionary<string, TokenBucket> _requestBudgets = new(StringComparer.OrdinalIgnoreCase);
    private readonly LogThrottle _warnings = new(WarningWindow, time);

    public void LogStartupStatus()
    {
        if (_provider.Value is not { } provider)
        {
            logger.LogWarning("No supported firmware interface found on this machine.");
            return;
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
    }

    /// <param name="callerName">Windows account name, for the audit log and the per-account request budget.</param>
    public async Task<IpcResponse> HandleAsync(IpcRequest request, FirmwareCaller caller, string callerName, CancellationToken ct)
    {
        var budget = _requestBudgets.GetOrAdd(callerName, _ => new TokenBucket(RequestBurst, RequestInterval, time));
        if (!budget.TryTake(out var retryAfter))
        {
            if (_warnings.ShouldLog($"flood|{callerName}", out var repeats))
                logger.LogWarning("{User} is sending requests too quickly; refusing the excess.{Repeats}", callerName, LogThrottle.Note(repeats));
            return TooManyRequests("Too many requests", retryAfter);
        }

        var lockdown = requireAdminForAllChanges();
        var provider = _provider.Value;

        if (request.Command == IpcCommand.GetStatus)
            return new IpcResponse(Status: new ServiceStatus(
                provider?.Vendor, caller.IsAdministrator, lockdown, ServiceVersion, IpcConstants.ProtocolVersion, Features));

        if (provider is null)
            return IpcResponse.Fail(IpcError.Unsupported, "This machine has no supported firmware interface.");

        var gateway = new FirmwareGateway(provider, lockdown);
        await _firmwareLock.WaitAsync(ct);
        try
        {
            return request.Command switch
            {
                IpcCommand.GetSettings => GetSettings(gateway, provider, caller),
                IpcCommand.SetSetting => SetSetting(gateway, request, caller, callerName),
                _ => IpcResponse.Fail(IpcError.BadRequest, $"Unknown command '{request.Command}'."),
            };
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogError(ex, "Firmware access denied to the service itself; it must run as LocalSystem or an administrator.");
            return IpcResponse.Fail(IpcError.ServiceError, "The Firmly service is not running with administrator rights.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Unexpected error handling {Request}.", request);
            return IpcResponse.Fail(IpcError.ServiceError, "Unexpected error in the Firmly service; see its event log.");
        }
        finally
        {
            _firmwareLock.Release();
        }
    }

    private static IpcResponse GetSettings(FirmwareGateway gateway, IFirmwareProvider provider, FirmwareCaller caller)
    {
        var settings = gateway.GetSettings();
        return new IpcResponse(
            Settings: [.. settings.Select(s => new SettingInfo(s, gateway.GetChangeAccess(s, caller)))],
            ReportedChargeLimit: provider.GetReportedChargeLimit(settings));
    }

    private IpcResponse SetSetting(FirmwareGateway gateway, IpcRequest request, FirmwareCaller caller, string callerName)
    {
        if (string.IsNullOrEmpty(request.Name) || request.Value is null)
            return IpcResponse.Fail(IpcError.BadRequest, "SetSetting needs a setting name and value.");
        if (request.Name.Length > MaxTextLength || request.Value.Length > MaxTextLength)
            return IpcResponse.Fail(IpcError.BadRequest, "The setting name or value is too long.");

        try
        {
            var updated = gateway.SetSetting(request.Name, request.Value, caller, request.BiosPassword);
            logger.LogInformation("{User} set '{Name}' to '{Value}'.", callerName, request.Name, updated.CurrentValue);
            return new IpcResponse(Setting: new SettingInfo(updated, gateway.GetChangeAccess(updated, caller)));
        }
        catch (FirmwareAccessDeniedException ex)
        {
            WarnRefused(callerName, request.Name, ex.Message);
            return IpcResponse.Fail(IpcError.AdministratorRequired, ex.Message);
        }
        catch (FirmwarePolicyException ex)
        {
            WarnRefused(callerName, request.Name, ex.Message);
            return IpcResponse.Fail(IpcError.NotAllowed, ex.Message);
        }
        catch (WriteLimitExceededException ex)
        {
            if (_warnings.ShouldLog("write-limit", out var repeats))
                logger.LogWarning("Refused to write '{Name}' for {User}: firmware was written too often recently.{Repeats}",
                    request.Name, callerName, LogThrottle.Note(repeats));
            return TooManyRequests("Too many firmware changes in a short time", ex.RetryAfter);
        }
        catch (FirmwareWriteException ex)
        {
            if (_warnings.ShouldLog($"rejected|{callerName}|{request.Name}", out var repeats))
                logger.LogWarning("Firmware rejected '{Name}' from {User}: {Reason}{Repeats}",
                    request.Name, callerName, ex.Message, LogThrottle.Note(repeats));
            return IpcResponse.Fail(IpcError.FirmwareRejected, ex.Message, ex.VendorCode);
        }
    }

    private void WarnRefused(string callerName, string name, string reason)
    {
        if (_warnings.ShouldLog($"refused|{callerName}|{name}", out var repeats))
            logger.LogWarning("{User} was refused '{Name}': {Reason}{Repeats}", callerName, name, reason, LogThrottle.Note(repeats));
    }

    private static IpcResponse TooManyRequests(string what, TimeSpan retryAfter)
    {
        var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        return IpcResponse.Fail(IpcError.TooManyRequests, $"{what}; try again in {seconds} second{(seconds == 1 ? "" : "s")}.");
    }
}
