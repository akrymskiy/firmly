using System.Text.Json;
using Firmly.Core;

namespace Firmly.Service;

/// <summary>
/// Keeps Firmly from locking the firmware, or from talking to it once locked, for firmware whose lockout is known
/// (<see cref="IFirmwareProvider.PasswordLockout"/>). Counts wrong BIOS passwords in a row; at the limit, refuses changes
/// without sending them until the PC restarts. Also refuses changes sent without a password while one is set, since
/// those count as wrong. For other firmware it only passes calls through.
/// </summary>
/// <param name="store">Where the count survives a restart of the service; null keeps it in memory only.</param>
internal sealed class PasswordLockoutProvider(IFirmwareProvider inner, PasswordFailureStore? store, ILogger logger) : IFirmwareProvider
{
    private int? _failures;

    public string Vendor => inner.Vendor;

    public FirmwarePolicy Policy => inner.Policy;

    public BiosPasswordLockout? PasswordLockout => inner.PasswordLockout;

    /// <summary>Changes the firmware will still accept the password for before locking; null when it isn't known.</summary>
    public int? TriesLeft => PasswordLockout is { } lockout ? Math.Max(0, lockout.MaxConsecutiveFailures - Failures) : null;

    public bool IsSupported() => inner.IsSupported();

    public IReadOnlyList<FirmwareSetting> GetSettings() => inner.GetSettings();

    /// <exception cref="BiosPasswordLockedException">The firmware is locked until the PC restarts; nothing was sent.</exception>
    /// <exception cref="FirmwareWriteException">
    /// The firmware rejected the change; or, with <see cref="FirmwareWriteException.PasswordRejected"/>, there was no
    /// password and one is set, and nothing was sent.
    /// </exception>
    public void SetSetting(string name, string value, string? biosPassword)
    {
        if (PasswordLockout is not { } lockout)
        {
            inner.SetSetting(name, value, biosPassword);
            return;
        }

        if (Failures >= lockout.MaxConsecutiveFailures)
            throw new BiosPasswordLockedException(lockout.MaxConsecutiveFailures);

        if (string.IsNullOrEmpty(biosPassword) && inner.IsBiosPasswordSet())
            throw new FirmwareWriteException($"'{name}' needs the BIOS administrator password.", 0, passwordRejected: true);

        try
        {
            inner.SetSetting(name, value, biosPassword);
        }
        catch (FirmwareWriteException ex) when (ex.PasswordRejected)
        {
            SaveFailures(Failures + 1);
            if (Failures >= lockout.MaxConsecutiveFailures)
                logger.LogWarning("The firmware refused {Count} BIOS passwords in a row, so it refuses changes until the PC restarts. " +
                    "Firmly won't send it any until then.", Failures);
            throw;
        }
        if (Failures > 0)
            SaveFailures(0);
    }

    public bool IsBiosPasswordSet() => inner.IsBiosPasswordSet();

    public ChargeModes? GetChargeModes(IReadOnlyList<FirmwareSetting> settings) => inner.GetChargeModes(settings);

    public double? GetReportedChargeLimit(IReadOnlyList<FirmwareSetting> settings) => inner.GetReportedChargeLimit(settings);

    // Loaded on first use.
    private int Failures
    {
        get
        {
            if (_failures is null)
            {
                try
                {
                    _failures = store?.Load() ?? 0;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    logger.LogError(ex, "Couldn't read the count of wrong BIOS passwords; starting from 0.");
                    _failures = 0;
                }
            }
            return _failures.Value;
        }
    }

    private void SaveFailures(int count)
    {
        _failures = count;
        try
        {
            store?.Save(count);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Couldn't save the count of wrong BIOS passwords; a restart of the service would lose it.");
        }
    }
}

/// <summary>The firmware refuses changes until the PC restarts, after too many wrong BIOS passwords in a row.</summary>
internal sealed class BiosPasswordLockedException(int failures)
    : Exception($"The BIOS refused {failures} passwords in a row, so it refuses changes until the PC restarts.");
