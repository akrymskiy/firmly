namespace Firmly.Service.Tests;

public class TokenBucketTests
{
    private readonly ManualTimeProvider _time = new();

    [Fact]
    public void AllowsBurst_ThenRefusesWithRetryAfter()
    {
        var bucket = new TokenBucket(3, TimeSpan.FromSeconds(10), _time);

        for (var i = 0; i < 3; i++)
            Assert.True(bucket.TryTake(out _));
        Assert.False(bucket.TryTake(out var retryAfter));
        Assert.Equal(TimeSpan.FromSeconds(10), retryAfter);
    }

    [Fact]
    public void RefillsOneTokenPerInterval_UpToCapacity()
    {
        var bucket = new TokenBucket(2, TimeSpan.FromSeconds(10), _time);
        bucket.TryTake(out _);
        bucket.TryTake(out _);

        _time.Advance(TimeSpan.FromSeconds(4));
        Assert.False(bucket.TryTake(out var retryAfter));
        Assert.Equal(TimeSpan.FromSeconds(6), retryAfter);

        _time.Advance(TimeSpan.FromHours(1));
        Assert.True(bucket.TryTake(out _));
        Assert.True(bucket.TryTake(out _));
        Assert.False(bucket.TryTake(out _));
    }
}
