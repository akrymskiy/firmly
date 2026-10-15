namespace Firmly.Service.Tests;

public class LogThrottleTests
{
    private readonly ManualTimeProvider _time = new();

    [Fact]
    public void LogsOncePerWindow_ThenReportsRepeatsHeldBack()
    {
        var throttle = new LogThrottle(TimeSpan.FromMinutes(1), _time);

        Assert.True(throttle.ShouldLog("a", out var repeats));
        Assert.Equal(0, repeats);
        Assert.False(throttle.ShouldLog("a", out _));
        Assert.False(throttle.ShouldLog("a", out _));

        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.True(throttle.ShouldLog("a", out repeats));
        Assert.Equal(2, repeats);
    }

    [Fact]
    public void KeysAreThrottledSeparately()
    {
        var throttle = new LogThrottle(TimeSpan.FromMinutes(1), _time);

        Assert.True(throttle.ShouldLog("a", out _));
        Assert.True(throttle.ShouldLog("b", out _));
    }

    [Fact]
    public void Note_IsEmptyWithoutRepeats()
    {
        Assert.Equal("", LogThrottle.Note(0));
        Assert.Equal(" (Repeated 5 times since last logged.)", LogThrottle.Note(5));
    }
}
