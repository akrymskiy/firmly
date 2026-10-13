using System.IO.Pipes;
using System.Security.Principal;

namespace Firmly.Ipc;

/// <summary>Used by Firmly.App to talk to Firmly.Service. Each call opens its own connection for one request.</summary>
public sealed class FirmlyClient
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);

    // Generous: a firmware write plus read-back can take several seconds.
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(30);

    /// <exception cref="FirmlyServiceException">The service is unavailable or returned an error.</exception>
    public async Task<ServiceStatus> GetStatusAsync(CancellationToken ct = default) =>
        (await SendAsync(new IpcRequest(IpcCommand.GetStatus), ct)).Status ?? throw MissingResult();

    /// <exception cref="FirmlyServiceException">The service is unavailable or returned an error.</exception>
    public async Task<FirmwareSnapshot> GetSettingsAsync(CancellationToken ct = default)
    {
        var response = await SendAsync(new IpcRequest(IpcCommand.GetSettings), ct);
        return new FirmwareSnapshot(response.Settings ?? throw MissingResult(), response.ReportedChargeLimit);
    }

    /// <returns>The setting as read back from firmware after the change.</returns>
    /// <exception cref="FirmlyServiceException">The service is unavailable or refused the change.</exception>
    public async Task<SettingInfo> SetSettingAsync(string name, string value, string? biosPassword = null, CancellationToken ct = default) =>
        (await SendAsync(new IpcRequest(IpcCommand.SetSetting, name, value, biosPassword), ct)).Setting ?? throw MissingResult();

    private static async Task<IpcResponse> SendAsync(IpcRequest request, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(CallTimeout);

        // Identification lets the service check who we are (for admin-only settings) without being able to act as us.
        await using var pipe = new NamedPipeClientStream(
            ".", IpcConstants.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        try
        {
            await pipe.ConnectAsync(ConnectTimeout, timeout.Token);
            EnsureServerIsTrusted(pipe);
            await IpcProtocol.WriteRequestAsync(pipe, request, timeout.Token);
            var response = await IpcProtocol.ReadResponseAsync(pipe, timeout.Token);
            if (response.Error != IpcError.None)
                throw new FirmlyServiceException(response.Error, response.Message ?? response.Error.ToString(), response.VendorCode);
            return response;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException or InvalidDataException
            || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            throw new FirmlyServiceException(IpcError.ServiceUnavailable, "The Firmly service is not running or did not respond.", inner: ex);
        }
    }

    // Only LocalSystem or an elevated administrator can own the pipe. This stops another program from creating
    // the pipe first and posing as the service, for example to collect a BIOS password.
    private static void EnsureServerIsTrusted(NamedPipeClientStream pipe)
    {
        var owner = pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        if (owner is not null
            && (owner.IsWellKnown(WellKnownSidType.LocalSystemSid) || owner.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid)))
            return;

        throw new FirmlyServiceException(IpcError.ServiceError,
            $"The Firmly pipe is owned by {owner?.Value ?? "nobody"}, not the Firmly service; refusing to use it.");
    }

    private static FirmlyServiceException MissingResult() =>
        new(IpcError.ServiceError, "The Firmly service sent an incomplete response.");
}

public sealed class FirmlyServiceException(IpcError error, string message, uint? vendorCode = null, Exception? inner = null)
    : Exception(message, inner)
{
    public IpcError Error { get; } = error;

    /// <summary>Raw firmware status code when <see cref="Error"/> is <see cref="IpcError.FirmwareRejected"/>.</summary>
    public uint? VendorCode { get; } = vendorCode;
}
