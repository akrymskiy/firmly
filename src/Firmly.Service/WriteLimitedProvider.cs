using Firmly.Core;

namespace Firmly.Service;

/// <summary>
/// Caps how often firmware is actually written, machine-wide. Firmware settings live in flash memory, which wears
/// with every write, so a client stuck in a loop must not be able to write without limit.
/// </summary>
internal sealed class WriteLimitedProvider(IFirmwareProvider inner, TimeProvider time) : IFirmwareProvider
{
    // Plenty for someone trying settings out; a runaway client gets at most 120 writes an hour.
    public const int Burst = 10;
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly TokenBucket _writes = new(Burst, Interval, time);

    public string Vendor => inner.Vendor;

    public FirmwarePolicy Policy => inner.Policy;

    public bool IsSupported() => inner.IsSupported();

    public IReadOnlyList<FirmwareSetting> GetSettings() => inner.GetSettings();

    /// <exception cref="WriteLimitExceededException">Too many writes recently; nothing was written.</exception>
    public void SetSetting(string name, string value, string? biosPassword)
    {
        if (!_writes.TryTake(out var retryAfter))
            throw new WriteLimitExceededException(retryAfter);

        inner.SetSetting(name, value, biosPassword);
    }

    public bool IsBiosPasswordSet() => inner.IsBiosPasswordSet();

    public ChargeModes? GetChargeModes(IReadOnlyList<FirmwareSetting> settings) => inner.GetChargeModes(settings);

    public double? GetReportedChargeLimit(IReadOnlyList<FirmwareSetting> settings) => inner.GetReportedChargeLimit(settings);
}

internal sealed class WriteLimitExceededException(TimeSpan retryAfter)
    : Exception($"Too many firmware writes in a short time; retry in {retryAfter}.")
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}
