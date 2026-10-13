using Firmly.Core;
using Firmly.Ipc;

namespace Firmly.Service;

/// <summary>Turns pipe requests into gateway calls. Every firmware access goes through <see cref="FirmwareGateway"/>.</summary>
/// <param name="requireAdminForAllChanges">Reads the machine lockdown policy; called on every request.</param>
internal sealed class RequestHandler(
    ILogger<RequestHandler> logger,
    FirmwareProviderResolver resolver,
    Func<bool> requireAdminForAllChanges)
{
    // One firmware operation at a time, so a read can't interleave with another client's write and its read-back.
    private readonly SemaphoreSlim _firmwareLock = new(1, 1);
    private readonly Lazy<IFirmwareProvider?> _provider = new(resolver.Resolve);

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

    /// <param name="callerName">Windows account name, for the audit log.</param>
    public async Task<IpcResponse> HandleAsync(IpcRequest request, FirmwareCaller caller, string callerName, CancellationToken ct)
    {
        var lockdown = requireAdminForAllChanges();
        var provider = _provider.Value;

        if (request.Command == IpcCommand.GetStatus)
            return new IpcResponse(Status: new ServiceStatus(provider?.Vendor, caller.IsAdministrator, lockdown));

        if (provider is null)
            return IpcResponse.Fail(IpcError.Unsupported, "This machine has no supported firmware interface.");

        var gateway = new FirmwareGateway(provider, lockdown);
        await _firmwareLock.WaitAsync(ct);
        try
        {
            return request.Command switch
            {
                IpcCommand.GetSettings => new IpcResponse(Settings:
                    [.. gateway.GetSettings().Select(s => new SettingInfo(s, gateway.GetChangeAccess(s, caller)))]),
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

    private IpcResponse SetSetting(FirmwareGateway gateway, IpcRequest request, FirmwareCaller caller, string callerName)
    {
        if (string.IsNullOrEmpty(request.Name) || request.Value is null)
            return IpcResponse.Fail(IpcError.BadRequest, "SetSetting needs a setting name and value.");

        try
        {
            var updated = gateway.SetSetting(request.Name, request.Value, caller, request.BiosPassword);
            logger.LogInformation("{User} set '{Name}' to '{Value}'.", callerName, request.Name, updated.CurrentValue);
            return new IpcResponse(Setting: new SettingInfo(updated, gateway.GetChangeAccess(updated, caller)));
        }
        catch (FirmwareAccessDeniedException ex)
        {
            logger.LogWarning("{User} was refused '{Name}': {Reason}", callerName, request.Name, ex.Message);
            return IpcResponse.Fail(IpcError.AdministratorRequired, ex.Message);
        }
        catch (FirmwarePolicyException ex)
        {
            logger.LogWarning("{User} was refused '{Name}': {Reason}", callerName, request.Name, ex.Message);
            return IpcResponse.Fail(IpcError.NotAllowed, ex.Message);
        }
        catch (FirmwareWriteException ex)
        {
            logger.LogWarning("Firmware rejected '{Name}' from {User}: {Reason}", request.Name, callerName, ex.Message);
            return IpcResponse.Fail(IpcError.FirmwareRejected, ex.Message, ex.VendorCode);
        }
    }
}
