namespace Firmly.Service;

/// <summary>
/// Allows up to <paramref name="capacity"/> calls in a burst, then one more per <paramref name="refillInterval"/>.
/// </summary>
internal sealed class TokenBucket(int capacity, TimeSpan refillInterval, TimeProvider time)
{
    private readonly Lock _lock = new();
    private double _tokens = capacity;
    private long _updatedAt = time.GetTimestamp();

    /// <param name="retryAfter">When refused, how long until a call would be allowed again.</param>
    public bool TryTake(out TimeSpan retryAfter)
    {
        lock (_lock)
        {
            var now = time.GetTimestamp();
            _tokens = Math.Min(capacity, _tokens + (time.GetElapsedTime(_updatedAt, now) / refillInterval));
            _updatedAt = now;

            if (_tokens >= 1)
            {
                _tokens--;
                retryAfter = TimeSpan.Zero;
                return true;
            }

            retryAfter = (1 - _tokens) * refillInterval;
            return false;
        }
    }
}
