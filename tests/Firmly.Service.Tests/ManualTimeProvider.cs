namespace Firmly.Service.Tests;

/// <summary>A clock that only moves when a test advances it; both timestamps and the wall clock.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    public static readonly DateTimeOffset Start = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    private long _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _ticks;

    public override DateTimeOffset GetUtcNow() => Start + TimeSpan.FromTicks(_ticks);

    public void Advance(TimeSpan by) => _ticks += by.Ticks;
}
