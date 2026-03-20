using System.Collections.Concurrent;

namespace Mail2SNMP.Core.Services;

/// <summary>
/// Short-lived in-memory cache that prevents the same notification from being
/// sent twice within a 60-second window. Expired entries are purged periodically
/// to keep memory usage bounded.
/// </summary>
public class NotificationDedupCache
{
    private readonly ConcurrentDictionary<string, DateTime> _cache = new();
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(60);
    private DateTime _lastCleanup = DateTime.UtcNow;

    /// <summary>
    /// Determines whether a notification for the given target and event has already
    /// been recorded within the deduplication window.
    /// </summary>
    /// <param name="targetKey">A unique key identifying the notification target (e.g. channel or endpoint).</param>
    /// <param name="eventId">The identifier of the event that triggered the notification.</param>
    /// <returns>
    /// <c>true</c> if the same target/event combination was seen within the last 60 seconds
    /// and the notification should be suppressed; otherwise <c>false</c>.
    /// </returns>
    public bool IsDuplicate(string targetKey, long eventId)
    {
        var key = $"{targetKey}:{eventId}";
        var now = DateTime.UtcNow;

        // Periodic cleanup (not every call, to reduce overhead)
        if (now - _lastCleanup > TimeSpan.FromSeconds(30))
        {
            _lastCleanup = now;
            Cleanup();
        }

        // Atomic check-and-insert: TryAdd returns false if key already exists
        if (_cache.TryAdd(key, now))
            return false; // Successfully added → not a duplicate

        // Key exists - check if within dedup window
        if (_cache.TryGetValue(key, out var lastSent) && now - lastSent < Window)
            return true; // Within window → duplicate

        // Expired entry, update it
        _cache[key] = now;
        return false;
    }

    /// <summary>
    /// Removes all cache entries whose timestamps have fallen outside the deduplication window.
    /// </summary>
    private void Cleanup()
    {
        var cutoff = DateTime.UtcNow - Window;
        foreach (var kvp in _cache)
        {
            if (kvp.Value < cutoff)
                _cache.TryRemove(kvp.Key, out _);
        }
    }
}
