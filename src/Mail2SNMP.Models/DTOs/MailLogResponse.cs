using Mail2SNMP.Models.Enums;

namespace Mail2SNMP.Models.DTOs;

/// <summary>
/// One row of the per-mail processing trace (UC-5): what arrived, what the job made of it,
/// and whether anyone was actually told.
/// </summary>
public sealed class MailLogResponse
{
    /// <summary>Surrogate key of the trace row.</summary>
    public long Id { get; set; }

    /// <summary>The mailbox the message was fetched from.</summary>
    public int MailboxId { get; set; }

    /// <summary>The mailbox's display name, when loaded.</summary>
    public string? MailboxName { get; set; }

    /// <summary>
    /// The job that produced this trace. One mail on a shared mailbox yields one row per
    /// active job, each with its own outcome. <c>null</c> on rows written before the claim
    /// was scoped per job.
    /// </summary>
    public int? JobId { get; set; }

    /// <summary>
    /// The idempotency key — normally the RFC 5322 <c>Message-ID</c>, or a deterministic
    /// synthetic key for the mails that arrive without one.
    /// </summary>
    public string MessageId { get; set; } = string.Empty;

    /// <summary>Sender address as recorded.</summary>
    public string? From { get; set; }

    /// <summary>Subject line as recorded.</summary>
    public string? Subject { get; set; }

    /// <summary>When the message was sent, from its own date header (UTC).</summary>
    public DateTime ReceivedUtc { get; set; }

    /// <summary>When this server finished processing it (UTC).</summary>
    public DateTime ProcessedUtc { get; set; }

    /// <summary>
    /// What the job made of the mail: no match, event created, deduplicated into an
    /// existing event, suppressed by maintenance, or dropped by the hourly event budget.
    /// </summary>
    public MailDisposition Disposition { get; set; }

    /// <summary>The event this mail produced or was collapsed into, if any.</summary>
    public long? EventId { get; set; }

    /// <summary>
    /// The event's current state, or <c>null</c> when there was no event or it has since
    /// been removed by retention.
    /// </summary>
    public EventState? EventState { get; set; }

    /// <summary>Number of that event's delivery attempts sitting in the dead-letter queue.</summary>
    public int OpenDeadLetters { get; set; }

    /// <summary>
    /// The delivery half of the trace in one word: <c>none</c> (no event was raised),
    /// <c>delivered</c>, <c>not-delivered</c> (no channel reported success),
    /// <c>suppressed</c> (a maintenance window was active), <c>failed</c> (attempts are
    /// dead-lettered), or <c>purged</c> (the event is gone, so its outcome is no longer
    /// knowable).
    /// </summary>
    public string Delivery { get; set; } = "none";
}
