namespace Mail2SNMP.Models.Entities;

/// <summary>
/// Tracks processed emails by MessageId to ensure idempotent processing across cluster instances.
/// </summary>
public class ProcessedMail
{
    /// <summary>Surrogate primary key. Identity column assigned by the database.</summary>
    public long Id { get; set; }

    /// <summary>FK to the <see cref="Mailbox"/> the message was fetched from; idempotency is tracked per mailbox.</summary>
    public int MailboxId { get; set; }

    /// <summary>Navigation to the owning <see cref="Mailbox"/> identified by <see cref="MailboxId"/>.</summary>
    public Mailbox Mailbox { get; set; } = null!;

    /// <summary>
    /// H-1: FK to the <see cref="Entities.Job"/> that processed this mail. The claim is
    /// scoped per job so that several jobs (i.e. several rules) can share one mailbox
    /// and each evaluates every message independently.
    /// </summary>
    /// <remarks>
    /// Before this existed the uniqueness claim was <c>(MessageId, MailboxId)</c>, so the
    /// first job to poll won the claim and every other job on the same mailbox silently
    /// never fired — the most natural configuration of the product (one alert mailbox,
    /// several rules) dropped all but one rule's alerts, non-deterministically.
    /// <c>null</c> only on rows written before the column existed.
    /// </remarks>
    public int? JobId { get; set; }

    /// <summary>
    /// RFC 5322 <c>Message-ID</c> header of the email. Together with <see cref="MailboxId"/>
    /// and <see cref="JobId"/> this uniquely identifies a processed message so the same job
    /// does not handle it twice across cluster instances.
    /// </summary>
    public string MessageId { get; set; } = string.Empty;

    /// <summary>Sender address of the email, retained for diagnostics. <c>null</c> if absent or not captured.</summary>
    public string? From { get; set; }

    /// <summary>Subject line of the email, retained for diagnostics. <c>null</c> if absent or not captured.</summary>
    public string? Subject { get; set; }

    /// <summary>UTC time the email was received (from its date header), used for retention and ordering.</summary>
    public DateTime ReceivedUtc { get; set; }

    /// <summary>UTC time this server finished processing the email. Defaults to <see cref="DateTime.UtcNow"/> at construction.</summary>
    public DateTime ProcessedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// UC-5: the processing outcome of this mail (matched/no-match/deduplicated/
    /// maintenance-suppressed) so "why was I (not) notified for mail X?" is
    /// answerable from the product. <see cref="Enums.MailDisposition.Unknown"/>
    /// on legacy rows created before dispositions were recorded.
    /// </summary>
    public Enums.MailDisposition Disposition { get; set; } = Enums.MailDisposition.Unknown;

    /// <summary>
    /// UC-5: the <see cref="Event"/> this mail produced or was collapsed into
    /// (for <see cref="Enums.MailDisposition.EventCreated"/>, <see cref="Enums.MailDisposition.Deduplicated"/>
    /// and <see cref="Enums.MailDisposition.MaintenanceSuppressed"/>); <c>null</c>
    /// when no event resulted. Intentionally NOT a foreign key: events are purged
    /// by retention on a different schedule than processed-mail rows, and the
    /// trace record must survive the event's deletion.
    /// </summary>
    public long? EventId { get; set; }
}
