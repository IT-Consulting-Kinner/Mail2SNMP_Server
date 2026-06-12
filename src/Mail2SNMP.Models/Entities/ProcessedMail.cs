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
}
