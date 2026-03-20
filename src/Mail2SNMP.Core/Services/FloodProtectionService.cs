using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Mail2SNMP.Core.Services;

/// <summary>
/// In-memory sliding-window rate limiter for event creation and notification delivery.
/// Each distinct key maintains its own independent counter and time window.
/// </summary>
public class FloodProtectionService
{
    private readonly ConcurrentDictionary<string, RateCounter> _counters = new();
    private readonly ILogger<FloodProtectionService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FloodProtectionService"/> class.
    /// </summary>
    /// <param name="logger">Logger used to record rate-limit warnings.</param>
    public FloodProtectionService(ILogger<FloodProtectionService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Checks whether the specified notification target has exceeded its per-minute rate limit.
    /// If the target is still under the limit, the current request is counted against it.
    /// </summary>
    /// <param name="targetKey">A unique key identifying the notification target (e.g. a channel or endpoint).</param>
    /// <param name="maxPerMinute">The maximum number of notifications allowed per one-minute sliding window.</param>
    /// <returns><c>true</c> if the target is rate-limited and the request should be suppressed; otherwise <c>false</c>.</returns>
    public bool IsRateLimited(string targetKey, int maxPerMinute)
    {
        var counter = _counters.GetOrAdd(targetKey, _ => new RateCounter());
        if (counter.TryIncrement(maxPerMinute))
            return false;

        _logger.LogWarning("Rate limit reached for target {Target}: {Max} per minute", targetKey, maxPerMinute);
        return true;
    }

    /// <summary>
    /// Checks whether the specified job has exceeded its per-hour event creation limit.
    /// If the job is still under the limit, the current event is counted against it.
    /// </summary>
    /// <param name="jobId">The identifier of the monitoring job.</param>
    /// <param name="maxPerHour">The maximum number of events allowed per one-hour sliding window.</param>
    /// <returns><c>true</c> if the job is rate-limited and the event should be suppressed; otherwise <c>false</c>.</returns>
    public bool IsEventRateLimited(int jobId, int maxPerHour)
    {
        var key = $"event:{jobId}";
        var counter = _counters.GetOrAdd(key, _ => new RateCounter(TimeSpan.FromHours(1)));
        if (counter.TryIncrement(maxPerHour))
            return false;

        _logger.LogWarning("Event rate limit reached for job {JobId}: {Max} per hour", jobId, maxPerHour);
        return true;
    }

    /// <summary>
    /// Thread-safe sliding-window counter that tracks timestamps of recent requests
    /// and evicts entries that fall outside the configured window.
    /// </summary>
    private class RateCounter
    {
        private readonly Queue<DateTime> _timestamps = new();
        private readonly TimeSpan _window;
        private readonly object _lock = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="RateCounter"/> class.
        /// </summary>
        /// <param name="window">The duration of the sliding window. Defaults to one minute when <c>null</c>.</param>
        public RateCounter(TimeSpan? window = null)
        {
            _window = window ?? TimeSpan.FromMinutes(1);
        }

        /// <summary>
        /// Atomically checks if under the limit and increments if so.
        /// Returns true if the increment was performed (under limit), false if rate limited.
        /// </summary>
        public bool TryIncrement(int maxCount)
        {
            lock (_lock)
            {
                var cutoff = DateTime.UtcNow - _window;
                while (_timestamps.Count > 0 && _timestamps.Peek() < cutoff)
                    _timestamps.Dequeue();

                if (_timestamps.Count >= maxCount)
                    return false;

                _timestamps.Enqueue(DateTime.UtcNow);
                return true;
            }
        }

        /// <summary>
        /// Gets the number of timestamps currently tracked within the sliding window.
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _timestamps.Count;
                }
            }
        }
    }
}
