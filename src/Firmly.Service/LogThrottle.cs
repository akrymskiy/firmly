namespace Firmly.Service;

/// <summary>
/// Lets a warning through at most once per <paramref name="window"/> for each key and counts the repeats it holds
/// back, so a client stuck in a loop can't flood the event log.
/// </summary>
internal sealed class LogThrottle(TimeSpan window, TimeProvider time)
{
    // Keys include setting names from requests; forget expired ones before tracking more than this.
    private const int MaxKeys = 1000;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, (long LoggedAt, int Repeats)> _entries = [];

    /// <param name="repeats">When true, how many repeats were held back since this key was last logged.</param>
    public bool ShouldLog(string key, out int repeats)
    {
        lock (_lock)
        {
            var now = time.GetTimestamp();
            if (_entries.TryGetValue(key, out var entry) && time.GetElapsedTime(entry.LoggedAt, now) < window)
            {
                _entries[key] = entry with { Repeats = entry.Repeats + 1 };
                repeats = 0;
                return false;
            }

            if (_entries.Count >= MaxKeys)
            {
                foreach (var (oldKey, old) in _entries)
                {
                    if (time.GetElapsedTime(old.LoggedAt, now) >= window)
                        _entries.Remove(oldKey);
                }
            }

            _entries[key] = (now, 0);
            repeats = entry.Repeats;
            return true;
        }
    }

    /// <summary>A suffix for the logged message saying how many repeats were held back, if any.</summary>
    public static string Note(int repeats) =>
        repeats > 0 ? $" (Repeated {repeats} times since last logged.)" : "";
}
