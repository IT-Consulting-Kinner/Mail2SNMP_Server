namespace Mail2SNMP.Models.Entities;

/// <summary>
/// Tracks processed emails by MessageId to ensure idempotent processing across cluster instances.
/// </summary>
public class ProcessedMail
{
    public long Id { get; set; }
    public int MailboxId { get; set; }
    public Mailbox Mailbox { get; set; } = null!;
    public string MessageId { get; set; } = string.Empty;
    public string? From { get; set; }
    public string? Subject { get; set; }
    public DateTime ReceivedUtc { get; set; }
    public DateTime ProcessedUtc { get; set; } = DateTime.UtcNow;
}
