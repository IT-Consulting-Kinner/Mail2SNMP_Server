using Mail2SNMP.Models.Enums;

namespace Mail2SNMP.Models.Entities;

/// <summary>
/// Represents a monitoring event created when an email matches a rule.
/// Events follow a lifecycle: New -> Notified -> Acknowledged -> Resolved.
/// </summary>
public class Event
{
    /// <summary>Surrogate primary key. 64-bit because the event table is high-volume. Database-generated.</summary>
    public long Id { get; set; }

    /// <summary>FK to the owning <see cref="Entities.Job"/> that produced this event.</summary>
    public int JobId { get; set; }

    /// <summary>Navigation property for the job referenced by <see cref="JobId"/>. Lazy-loaded.</summary>
    public Job Job { get; set; } = null!;

    /// <summary>
    /// Current lifecycle state. Transitions follow
    /// <see cref="EventState.New"/> → <see cref="EventState.Notified"/> →
    /// <see cref="EventState.Acknowledged"/> → <see cref="EventState.Resolved"/>,
    /// with <see cref="EventState.Suppressed"/> and <see cref="EventState.Expired"/> as alternates.
    /// Defaults to <see cref="EventState.New"/>.
    /// </summary>
    public EventState State { get; set; } = EventState.New;

    /// <summary>Severity assigned by the matching rule. See <see cref="Mail2SNMP.Models.Enums.Severity"/>.</summary>
    public Severity Severity { get; set; }

    /// <summary>
    /// RFC 5322 Message-ID of the source email, used for correlation and deduplication.
    /// <c>null</c> when the source message had no Message-ID header.
    /// </summary>
    public string? MessageId { get; set; }

    /// <summary>Sender (From) address of the source email. <c>null</c> if unavailable.</summary>
    public string? MailFrom { get; set; }

    /// <summary>Subject line of the source email. <c>null</c> if the message had no subject.</summary>
    public string? Subject { get; set; }

    /// <summary>
    /// Snapshot of the rule name that produced this event, denormalized so the event stays meaningful
    /// even if the underlying rule is later renamed or deleted. <c>null</c> if not captured.
    /// </summary>
    public string? RuleName { get; set; }

    /// <summary>
    /// Number of times a matching email has folded into this event via deduplication. Starts at 1
    /// for the originating mail and increments for each duplicate seen inside the job's dedup window.
    /// </summary>
    public int HitCount { get; set; } = 1;

    /// <summary>UTC timestamp when the event was first created. Defaults to the current UTC time.</summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp when the event was delivered to at least one notification channel; <c>null</c> until notified.</summary>
    public DateTime? NotifiedUtc { get; set; }

    /// <summary>UTC timestamp when the event was acknowledged; <c>null</c> while unacknowledged.</summary>
    public DateTime? AcknowledgedUtc { get; set; }

    /// <summary>UTC timestamp when the event was resolved; <c>null</c> while unresolved.</summary>
    public DateTime? ResolvedUtc { get; set; }

    /// <summary>UTC timestamp of the most recent <see cref="State"/> transition; <c>null</c> if the state has never changed since creation.</summary>
    public DateTime? LastStateChangeUtc { get; set; }

    /// <summary>Identifier (user name or actor id) of whoever acknowledged the event; <c>null</c> if unacknowledged.</summary>
    public string? AcknowledgedBy { get; set; }

    /// <summary>Identifier (user name or actor id) of whoever resolved the event; <c>null</c> if unresolved.</summary>
    public string? ResolvedBy { get; set; }

    /// <summary>
    /// EF Core optimistic-concurrency token. The database updates it on each change so concurrent
    /// state transitions (e.g. ack vs. resolve) raise a conflict instead of silently overwriting.
    /// </summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
