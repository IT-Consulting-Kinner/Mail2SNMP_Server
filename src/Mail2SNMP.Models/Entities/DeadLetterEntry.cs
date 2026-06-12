using Mail2SNMP.Models.Enums;

namespace Mail2SNMP.Models.Entities;

/// <summary>
/// A failed webhook delivery queued for automatic retry with exponential backoff (Enterprise only).
/// </summary>
public class DeadLetterEntry
{
    /// <summary>Surrogate primary key. 64-bit because the dead-letter table can be high-volume. Database-generated.</summary>
    public long Id { get; set; }

    /// <summary>FK to the <see cref="WebhookTarget"/> whose delivery failed and is to be retried.</summary>
    public int WebhookTargetId { get; set; }

    /// <summary>Navigation property for the target referenced by <see cref="WebhookTargetId"/>. Lazy-loaded.</summary>
    public WebhookTarget WebhookTarget { get; set; } = null!;

    /// <summary>FK to the originating <see cref="Entities.Event"/> whose notification is being retried.</summary>
    public long EventId { get; set; }

    /// <summary>Navigation property for the event referenced by <see cref="EventId"/>. Lazy-loaded.</summary>
    public Event Event { get; set; } = null!;

    /// <summary>Serialized JSON webhook payload captured at enqueue time, replayed verbatim on each retry. Plaintext.</summary>
    public string PayloadJson { get; set; } = string.Empty;

    /// <summary>Message from the most recent failed delivery attempt; <c>null</c> before the first attempt.</summary>
    public string? LastError { get; set; }

    /// <summary>Number of delivery attempts made so far. Drives exponential backoff and the abandon threshold.</summary>
    public int AttemptCount { get; set; }

    /// <summary>UTC timestamp when the entry was dead-lettered. Defaults to the current UTC time.</summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Earliest UTC time the entry is eligible for its next retry, computed from the backoff schedule.
    /// <c>null</c> means immediately eligible.
    /// </summary>
    public DateTime? NextRetryUtc { get; set; }

    /// <summary>
    /// UTC time until which a worker instance holds an exclusive processing lock on this entry,
    /// preventing concurrent retries across instances. <c>null</c> when not locked; an expired value
    /// is treated as unlocked.
    /// </summary>
    public DateTime? LockedUntilUtc { get; set; }

    /// <summary>Identifier of the worker instance currently holding the lock (see <see cref="LockedUntilUtc"/>); <c>null</c> when unlocked.</summary>
    public string? LockedByInstanceId { get; set; }

    /// <summary>
    /// Processing status. See <see cref="DeadLetterStatus"/>: <see cref="DeadLetterStatus.Pending"/> →
    /// <see cref="DeadLetterStatus.Locked"/> → success (row deleted) or back to Pending with backoff;
    /// <see cref="DeadLetterStatus.Abandoned"/> is terminal. Defaults to <see cref="DeadLetterStatus.Pending"/>.
    /// </summary>
    public DeadLetterStatus Status { get; set; } = DeadLetterStatus.Pending;

    /// <summary>
    /// EF Core optimistic-concurrency token; updated by the database on each row change so two workers
    /// cannot both claim the same entry.
    /// </summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
