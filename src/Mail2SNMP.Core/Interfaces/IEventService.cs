using Mail2SNMP.Models.Entities;
using Mail2SNMP.Models.Enums;

namespace Mail2SNMP.Core.Interfaces;

/// <summary>
/// Manages the event lifecycle. Events follow a state machine:
/// New -> Notified -> Acknowledged -> Resolved, with optional transitions
/// to Suppressed (from New, Notified, or Acknowledged) and Expired (via data retention).
/// Deduplication is handled automatically via the EventDedup table.
/// </summary>
public interface IEventService
{
    /// <summary>
    /// Retrieves events with optional filters for state and job.
    /// </summary>
    Task<IReadOnlyList<Event>> GetAllAsync(EventState? stateFilter = null, int? jobId = null, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a single event by its identifier, including the associated job.
    /// </summary>
    Task<Event?> GetByIdAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// Creates a new event. If a MessageId is present and a matching dedup entry exists,
    /// the existing event's HitCount is incremented instead of creating a duplicate.
    /// </summary>
    Task<Event> CreateAsync(Event evt, CancellationToken ct = default);

    /// <summary>
    /// Transitions an event from New to Notified after at least one notification
    /// channel has successfully delivered the event.
    /// </summary>
    Task MarkAsNotifiedAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// Transitions an event to Acknowledged by a specific user. Records the timestamp
    /// and user identifier, and logs an audit entry.
    /// </summary>
    Task AcknowledgeAsync(long id, string userId, CancellationToken ct = default);

    /// <summary>
    /// Transitions an event to Resolved by a specific user. Records the timestamp
    /// and user identifier, and logs an audit entry.
    /// </summary>
    Task ResolveAsync(long id, string userId, CancellationToken ct = default);

    /// <summary>
    /// Transitions an event to Suppressed (e.g., during a maintenance window).
    /// </summary>
    Task SuppressAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// Re-sends notifications for an existing event through all configured channels.
    /// Useful for testing and troubleshooting delivery issues.
    /// </summary>
    Task ReplayAsync(long id, CancellationToken ct = default);
}
