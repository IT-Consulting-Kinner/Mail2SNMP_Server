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
    /// RFC 5322 <c>Message-ID</c> header of the email. Together with <see cref="MailboxId"/> this uniquely
    /// identifies a processed message so it is not handled twice across cluster instances.
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
