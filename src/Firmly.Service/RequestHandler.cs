using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

using Firmly.Core;
using Firmly.Ipc;

namespace Firmly.Service;

/// <summary>
/// Turns pipe requests into gateway calls, and switches a charge override back when it ends.
/// Every firmware access goes through <see cref="FirmwareGateway"/>.
/// </summary>
/// <param name="requireAdminForAllChanges">Reads the machine lockdown policy; called on every request.</param>
/// <param name="overrides">Where the charge override is kept across restarts.</param>
/// <param name="passwords">Encrypts a BIOS password kept to switch back with; DPAPI by default.</param>
/// <param name="allowSavedBiosPassword">Reads the machine policy on keeping the BIOS password; called on every use.</param>
/// <param name="passwordFailures">Where wrong BIOS passwords in a row are counted across restarts; in memory when null.</param>
internal sealed class RequestHandler(
    ILogger<RequestHandler> logger,
    FirmwareProviderResolver resolver,
    Func<bool> requireAdminForAllChanges,
    TimeProvider time,
    ChargeOverrideStore overrides,
    IPasswordProtector? passwords = null,
    Func<bool>? allowSavedBiosPassword = null,
    PasswordFailureStore? passwordFailures = null)
{
    // Requests per account: the app makes two at start-up and a few a second at most while someone clicks around.
    // A UI bug once sent 37,000 requests in 90 seconds; the excess is refused before touching firmware.
    public const int RequestBurst = 30;
    public static readonly TimeSpan RequestInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>The longest a charge override may last.</summary>
    public static readonly TimeSpan MaxOverride = TimeSpan.FromDays(31);

    /// <summary>After a failed switch back, how long to wait before trying again.</summary>
    public static readonly TimeSpan RevertRetryInterval = TimeSpan.FromMinutes(5);

    // Repeats of the same warning within this window are counted rather than logged.
    private static readonly TimeSpan WarningWindow = TimeSpan.FromMinutes(1);

    // Whether a BIOS password is set changes rarely (in the BIOS setup, so after a restart): read it this often at most.
    private static readonly TimeSpan PasswordCheckInterval = TimeSpan.FromMinutes(5);

    // Longer names or values than any firmware uses are refused, so they can't bloat the log either.
    private const int MaxTextLength = 256;

    // e.g. "0.1.0+27ce7e2...": the version plus the commit it was built from.
    private static readonly string ServiceVersion =
        typeof(RequestHandler).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    private static readonly string[] Features =
    [
        IpcFeatures.Firmware, IpcFeatures.ChargeModes, IpcFeatures.ChargeOverride, IpcFeatures.ChargeOverrideEnd,
        IpcFeatures.BiosPassword, IpcFeatures.BiosPasswordLockout,
    ];

    private readonly IPasswordProtector _passwords = passwords ?? new DpapiPasswordProtector();
    private readonly Func<bool> _allowSavedBiosPassword = allowSavedBiosPassword ?? (() => true);

    // One firmware operation at a time, so a read can't interleave with another client's write and its read-back.
    // Also guards the charge override.
    private readonly SemaphoreSlim _firmwareLock = new(1, 1);
    // The lockout check comes first, so a change it refuses doesn't use up the write limit.
    private readonly Lazy<PasswordLockoutProvider?> _provider = new(() => resolver.Resolve() is { } provider
        ? new PasswordLockoutProvider(new WriteLimitedProvider(provider, time), passwordFailures, logger)
        : null);
    private readonly ConcurrentDictionary<string, TokenBucket> _requestBudgets = new(StringComparer.OrdinalIgnoreCase);
    private readonly LogThrottle _warnings = new(WarningWindow, time);

    private StoredChargeOverride? _override;
    private bool _overrideLoaded;
    private ChargeOverrideEnd? _lastEnd;
    private bool _lastEndLoaded;
    private DateTimeOffset _nextRevertAttempt;
    // The override has ended, but switching back needs the BIOS password, which someone must send. Not retried
    // meanwhile: a BIOS may count every attempt without the right password against it.
    private bool _waitingForPassword;
    private (bool IsSet, long CheckedAt)? _biosPassword;

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
                provider?.Vendor, caller.IsAdministrator, lockdown, ServiceVersion, IpcConstants.ProtocolVersion, Features,
                _allowSavedBiosPassword()));

        if (provider is null)
            return IpcResponse.Fail(IpcError.Unsupported, "This machine has no supported firmware interface.");

        if (request.Name?.Length > MaxTextLength || request.Value?.Length > MaxTextLength)
            return IpcResponse.Fail(IpcError.BadRequest, "The name or value is too long.");

        var gateway = new FirmwareGateway(provider, lockdown);
        await _firmwareLock.WaitAsync(ct);
        try
        {
            return request.Command switch
            {
                IpcCommand.GetSettings => Snapshot(gateway, provider, caller),
                IpcCommand.SetSetting => SetSetting(gateway, request, caller, callerName),
                IpcCommand.SetChargeMode => SetChargeMode(gateway, provider, request, caller, callerName),
                IpcCommand.SetChargeOverride => SetChargeOverride(gateway, provider, request, caller, callerName),
                IpcCommand.CancelChargeOverride => CancelChargeOverride(gateway, provider, request, caller, callerName),
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

    /// <summary>
    /// Switches a charge override back once it has ended, and forgets one whose mode was changed outside Firmly.
    /// Called regularly by <see cref="ChargeOverrideWorker"/>; does nothing while there's no override.
    /// </summary>
    public async Task CheckChargeOverrideAsync(CancellationToken ct)
    {
        if (_provider.Value is not { } provider)
            return;

        await _firmwareLock.WaitAsync(ct);
        try
        {
            if (Override is not { } current)
                return;

            // The service acts here, not a caller: the machine policy applied when the override was chosen.
            var gateway = new FirmwareGateway(provider);
            var modes = gateway.GetChargeModes();
            if (modes?.CurrentId != current.ModeId)
            {
                logger.LogInformation("The charge mode was changed outside Firmly; the timed '{Mode}' is cancelled.", current.ModeId);
                SaveOverride(null);
                return;
            }

            var now = time.GetUtcNow();
            if (now < current.Until || now < _nextRevertAttempt || _waitingForPassword)
                return;

            if (modes.Find(current.RestoreModeId) is not { } restore)
            {
                logger.LogWarning("Can't switch back to '{Mode}': the firmware no longer offers it. Keeping '{Current}'.",
                    current.RestoreModeId, current.ModeId);
                SaveOverride(null);
                SaveLastEnd(new ChargeOverrideEnd(current.ModeId, current.RestoreModeId, now, SwitchedBack: false));
                return;
            }

            // A kept password whose BIOS no longer has one (removed in the BIOS setup) isn't sent: the firmware might
            // count it as a wrong one.
            var passwordSet = IsBiosPasswordSet(gateway);
            var password = passwordSet ? KeptPassword(current) : null;
            if (password is null && passwordSet)
            {
                _waitingForPassword = true;
                logger.LogInformation("The timed '{Mode}' has ended; switching back to '{Restore}' needs the BIOS password, which Firmly will ask for.",
                    current.ModeId, restore.Label);
                return;
            }

            try
            {
                gateway.SetChargeMode(restore.Id, FirmwareCaller.Administrator, password);
                SaveOverride(null);
                SaveLastEnd(new ChargeOverrideEnd(current.ModeId, restore.Id, now));
                logger.LogInformation("Switched the battery back to '{Mode}', as {User} asked.", restore.Label, current.SetBy);
            }
            catch (FirmwareWriteException ex) when (ex.PasswordRejected)
            {
                // The kept password no longer works (changed in the BIOS setup?): forget it rather than try it again.
                _biosPassword = null;
                SaveOverride(current with { ProtectedBiosPassword = null });
                _waitingForPassword = true;
                logger.LogWarning("Couldn't switch the battery back to '{Mode}': the BIOS didn't accept the password. Firmly will ask for it.",
                    restore.Label);
            }
            catch (Exception ex) when (ex is FirmwarePolicyException or FirmwareWriteException or WriteLimitExceededException
                or BiosPasswordLockedException)
            {
                _nextRevertAttempt = now + RevertRetryInterval;
                if (_warnings.ShouldLog("revert", out var repeats))
                    logger.LogWarning("Couldn't switch the battery back to '{Mode}'; trying again in a few minutes: {Reason}{Repeats}",
                        restore.Label, ex.Message, LogThrottle.Note(repeats));
            }
        }
        finally
        {
            _firmwareLock.Release();
        }
    }

    private IpcResponse Snapshot(FirmwareGateway gateway, IFirmwareProvider provider, FirmwareCaller caller)
    {
        var settings = gateway.GetSettings();
        var modes = provider.GetChargeModes(settings);
        return new IpcResponse(
            Settings: [.. settings.Select(s => new SettingInfo(s, gateway.GetChangeAccess(s, caller)))],
            ReportedChargeLimit: provider.GetReportedChargeLimit(settings),
            ChargeModes: modes,
            ChargeModeAccess: modes is null ? null : gateway.GetChargeModeAccess(modes, settings, caller),
            ChargeOverride: Override?.ToIpc(_waitingForPassword),
            BiosPasswordSet: IsBiosPasswordSet(gateway),
            BiosPasswordTriesLeft: _provider.Value?.TriesLeft,
            LastOverrideEnd: LastEnd);
    }

    private IpcResponse SetSetting(FirmwareGateway gateway, IpcRequest request, FirmwareCaller caller, string callerName)
    {
        if (string.IsNullOrEmpty(request.Name) || request.Value is null)
            return IpcResponse.Fail(IpcError.BadRequest, "SetSetting needs a setting name and value.");

        return Change(callerName, request.Name, () =>
        {
            var updated = gateway.SetSetting(request.Name, request.Value, caller, request.BiosPassword);
            logger.LogInformation("{User} set '{Name}' to '{Value}'.", callerName, request.Name, updated.CurrentValue);
            ForgetOverrideIfModeChanged(gateway, callerName);
            return new IpcResponse(Setting: new SettingInfo(updated, gateway.GetChangeAccess(updated, caller)));
        });
    }

    private IpcResponse SetChargeMode(
        FirmwareGateway gateway, IFirmwareProvider provider, IpcRequest request, FirmwareCaller caller, string callerName)
    {
        if (string.IsNullOrEmpty(request.Name))
            return IpcResponse.Fail(IpcError.BadRequest, "SetChargeMode needs a charge mode.");

        return Change(callerName, request.Name, () =>
        {
            var modes = gateway.SetChargeMode(request.Name, caller, request.BiosPassword);
            logger.LogInformation("{User} chose the charge mode '{Mode}'.", callerName, modes.Current?.Label);
            ForgetOverrideIfModeChanged(gateway, callerName);
            return Snapshot(gateway, provider, caller);
        });
    }

    private IpcResponse SetChargeOverride(
        FirmwareGateway gateway, IFirmwareProvider provider, IpcRequest request, FirmwareCaller caller, string callerName)
    {
        if (string.IsNullOrEmpty(request.Name) || string.IsNullOrEmpty(request.Value) || request.Until is not { } until)
            return IpcResponse.Fail(IpcError.BadRequest, "SetChargeOverride needs a charge mode, a mode to switch back to, and an end time.");
        if (request.Name == request.Value)
            return IpcResponse.Fail(IpcError.BadRequest, "The mode to switch back to must differ from the mode chosen for now.");

        var now = time.GetUtcNow();
        if (until <= now || until > now + MaxOverride)
            return IpcResponse.Fail(IpcError.BadRequest, $"The end time must be in the next {MaxOverride.TotalDays:0} days.");
        if (request.KeepBiosPassword && !_allowSavedBiosPassword())
            return IpcResponse.Fail(IpcError.NotAllowed, "This PC's policy doesn't let Firmly keep the BIOS password.");
        if (request.KeepBiosPassword && string.IsNullOrEmpty(request.BiosPassword))
            return IpcResponse.Fail(IpcError.BadRequest, "There's no BIOS password to keep.");

        return Change(callerName, request.Name, () =>
        {
            if (gateway.GetChargeModes()?.Find(request.Value) is null)
                throw new FirmwarePolicyException($"'{request.Value}' is not a charge mode on this device.");

            var modes = gateway.SetChargeMode(request.Name, caller, request.BiosPassword);
            // Kept only once the BIOS has accepted it.
            var kept = request.KeepBiosPassword ? _passwords.Protect(request.BiosPassword!) : null;
            SaveOverride(new StoredChargeOverride(request.Name, request.Value, until, callerName, kept));
            // The new one is what the app shows now; it reports its own end.
            SaveLastEnd(null);
            _nextRevertAttempt = default;
            logger.LogInformation("{User} chose '{Mode}' until {Until:u}, then '{Restore}'{Kept}.",
                callerName, modes.Current?.Label, until, modes.Find(request.Value)?.Label,
                kept is null ? "" : ", keeping the BIOS password to switch back with");
            return Snapshot(gateway, provider, caller);
        });
    }

    private IpcResponse CancelChargeOverride(
        FirmwareGateway gateway, IFirmwareProvider provider, IpcRequest request, FirmwareCaller caller, string callerName)
    {
        if (Override is not { } current)
            return Snapshot(gateway, provider, caller);

        return Change(callerName, current.RestoreModeId, () =>
        {
            if (gateway.GetChargeModes()?.Find(current.RestoreModeId) is not null)
                gateway.SetChargeMode(current.RestoreModeId, caller, request.BiosPassword);
            SaveOverride(null);
            logger.LogInformation("{User} ended the timed '{Mode}' early.", callerName, current.ModeId);
            return Snapshot(gateway, provider, caller);
        });
    }

    // A charge override only lasts while its mode is on: choosing another mode, by any route, ends it.
    private void ForgetOverrideIfModeChanged(FirmwareGateway gateway, string callerName)
    {
        if (Override is { } current && gateway.GetChargeModes()?.CurrentId != current.ModeId)
        {
            logger.LogInformation("{User} changed the charge mode; the timed '{Mode}' is cancelled.", callerName, current.ModeId);
            SaveOverride(null);
        }
    }

    /// <summary>Runs a change, turning refusals into error responses.</summary>
    /// <param name="what">The setting or mode being changed, for the log.</param>
    private IpcResponse Change(string callerName, string what, Func<IpcResponse> change)
    {
        try
        {
            return change();
        }
        catch (FirmwareAccessDeniedException ex)
        {
            WarnRefused(callerName, what, ex.Message);
            return IpcResponse.Fail(IpcError.AdministratorRequired, ex.Message);
        }
        catch (FirmwarePolicyException ex)
        {
            WarnRefused(callerName, what, ex.Message);
            return IpcResponse.Fail(IpcError.NotAllowed, ex.Message);
        }
        catch (WriteLimitExceededException ex)
        {
            if (_warnings.ShouldLog("write-limit", out var repeats))
                logger.LogWarning("Refused to change '{Name}' for {User}: firmware was written too often recently.{Repeats}",
                    what, callerName, LogThrottle.Note(repeats));
            return TooManyRequests("Too many firmware changes in a short time", ex.RetryAfter);
        }
        catch (FirmwareWriteException ex) when (ex.PasswordRejected)
        {
            _biosPassword = null;
            if (_warnings.ShouldLog($"password|{callerName}", out var repeats))
                logger.LogWarning("Firmware rejected '{Name}' from {User}: the BIOS password was missing or wrong.{Repeats}",
                    what, callerName, LogThrottle.Note(repeats));
            return IpcResponse.Fail(IpcError.BiosPasswordRequired, "The BIOS administrator password is missing or wrong.", ex.VendorCode)
                with { BiosPasswordTriesLeft = _provider.Value?.TriesLeft };
        }
        catch (BiosPasswordLockedException ex)
        {
            if (_warnings.ShouldLog($"password-locked|{callerName}", out var repeats))
                logger.LogWarning("Didn't send '{Name}' from {User} to the firmware: {Reason}{Repeats}",
                    what, callerName, ex.Message, LogThrottle.Note(repeats));
            return IpcResponse.Fail(IpcError.BiosPasswordRequired, ex.Message) with { BiosPasswordTriesLeft = 0 };
        }
        catch (FirmwareWriteException ex)
        {
            if (_warnings.ShouldLog($"rejected|{callerName}|{what}", out var repeats))
                logger.LogWarning("Firmware rejected '{Name}' from {User}: {Reason}{Repeats}",
                    what, callerName, ex.Message, LogThrottle.Note(repeats));
            return IpcResponse.Fail(IpcError.FirmwareRejected, ex.Message, ex.VendorCode);
        }
    }

    // Loaded on first use; only touched under _firmwareLock.
    private StoredChargeOverride? Override
    {
        get
        {
            if (!_overrideLoaded)
            {
                _overrideLoaded = true;
                try
                {
                    _override = overrides.Load();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    logger.LogError(ex, "Couldn't read the saved charge override; ignoring it.");
                }
            }
            return _override;
        }
    }

    // Loaded on first use; only touched under _firmwareLock.
    private ChargeOverrideEnd? LastEnd
    {
        get
        {
            if (!_lastEndLoaded)
            {
                _lastEndLoaded = true;
                try
                {
                    _lastEnd = overrides.LoadEnd();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    logger.LogError(ex, "Couldn't read how the last charge override ended; ignoring it.");
                }
            }
            return _lastEnd;
        }
    }

    // Read again after PasswordCheckInterval, or once a password was refused (it may have been set meanwhile).
    private bool IsBiosPasswordSet(FirmwareGateway gateway)
    {
        if (_biosPassword is { } known && time.GetElapsedTime(known.CheckedAt) < PasswordCheckInterval)
            return known.IsSet;

        var isSet = gateway.IsBiosPasswordSet();
        _biosPassword = (isSet, time.GetTimestamp());
        return isSet;
    }

    private string? KeptPassword(StoredChargeOverride current)
    {
        if (current.ProtectedBiosPassword is not { } kept)
            return null;
        try
        {
            return _passwords.Unprotect(kept);
        }
        catch (CryptographicException ex)
        {
            logger.LogError(ex, "Couldn't decrypt the BIOS password kept for switching back.");
            return null;
        }
    }

    private void SaveOverride(StoredChargeOverride? value)
    {
        _override = value;
        _overrideLoaded = true;
        _waitingForPassword = false;
        try
        {
            overrides.Save(value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Couldn't save the charge override; a restart of the service would lose it.");
        }
    }

    private void SaveLastEnd(ChargeOverrideEnd? value)
    {
        if (value is null && LastEnd is null)
            return;

        _lastEnd = value;
        _lastEndLoaded = true;
        try
        {
            overrides.SaveEnd(value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Couldn't save how the charge override ended; the app may not be able to say so.");
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
