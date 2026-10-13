using System.IO.Pipes;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using Firmly.Core;
using Firmly.Ipc;

namespace Firmly.Service;

/// <summary>
/// Serves Firmly.App over a local named pipe, one request and one response per connection.
/// Only signed-in (interactive) users can connect; network clients are denied.
/// </summary>
internal sealed class PipeServer(ILogger<PipeServer> logger, RequestHandler handler) : BackgroundService
{
    private const int MaxConcurrentClients = 4;

    // A client that connects but doesn't send (or doesn't read the reply) is dropped after this.
    private static readonly TimeSpan IoTimeout = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let host startup finish before touching WMI.
        await Task.Yield();
        handler.LogStartupStatus();

        using var slots = new SemaphoreSlim(MaxConcurrentClients);
        var firstInstance = true;
        while (true)
        {
            await slots.WaitAsync(stoppingToken);
            NamedPipeServerStream pipe;
            try
            {
                pipe = CreatePipe(firstInstance);
            }
            catch (IOException ex) when (firstInstance)
            {
                slots.Release();
                logger.LogCritical(ex, "The pipe '{Pipe}' already exists: another Firmly service, or a program posing as it, is running.", IpcConstants.PipeName);
                throw;
            }
            firstInstance = false;

            try
            {
                await pipe.WaitForConnectionAsync(stoppingToken);
            }
            catch
            {
                await pipe.DisposeAsync();
                slots.Release();
                throw;
            }

            // Serve in the background; this loop immediately opens the next instance, so the pipe always exists.
            _ = ServeAsync(pipe, slots, stoppingToken);
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, SemaphoreSlim slots, CancellationToken stoppingToken)
    {
        try
        {
            await using (pipe)
            {
                IpcResponse response;
                using (var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken))
                {
                    readTimeout.CancelAfter(IoTimeout);
                    try
                    {
                        var request = await IpcProtocol.ReadRequestAsync(pipe, readTimeout.Token);
                        var (caller, callerName) = IdentifyCaller(pipe);
                        response = await handler.HandleAsync(request, caller, callerName, stoppingToken);
                    }
                    catch (InvalidDataException ex)
                    {
                        logger.LogWarning("Rejected a malformed request: {Reason}", ex.Message);
                        response = IpcResponse.Fail(IpcError.BadRequest, "Malformed request.");
                    }
                }

                using var writeTimeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                writeTimeout.CancelAfter(IoTimeout);
                await IpcProtocol.WriteResponseAsync(pipe, response, writeTimeout.Token);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            logger.LogDebug("Client disconnected or timed out: {Reason}", ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error serving a client.");
        }
        finally
        {
            slots.Release();
        }
    }

    // The client connects at Identification level: enough to read its token, not to act as the user.
    // If the token can't be read, the caller is treated as a standard user.
    private (FirmwareCaller Caller, string Name) IdentifyCaller(NamedPipeServerStream pipe)
    {
        try
        {
            var result = (FirmwareCaller.StandardUser, "unknown user");
            pipe.RunAsClient(() =>
            {
                using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
                result = (FirmwareCaller.FromIdentity(identity), identity.Name);
            });
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            logger.LogWarning("Could not identify the pipe client; treating it as a standard user: {Reason}", ex.Message);
            return (FirmwareCaller.StandardUser, "unknown user");
        }
    }

    private static NamedPipeServerStream CreatePipe(bool firstInstance)
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        // Signed-in users (console or Remote Desktop) may talk to the service, but not create pipe instances of their own.
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.InteractiveSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));

        // FirstPipeInstance on startup fails if anything already owns the name, rather than sharing it.
        var options = PipeOptions.Asynchronous | (firstInstance ? PipeOptions.FirstPipeInstance : PipeOptions.None);
        return NamedPipeServerStreamAcl.Create(
            IpcConstants.PipeName, PipeDirection.InOut, MaxConcurrentClients, PipeTransmissionMode.Byte, options,
            inBufferSize: 0, outBufferSize: 0, security);
    }
}
