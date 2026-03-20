using Mail2SNMP.Models.Enums;

namespace Mail2SNMP.Models.Entities;

/// <summary>
/// Represents a monitoring event created when an email matches a rule.
/// Events follow a lifecycle: New -> Notified -> Acknowledged -> Resolved.
/// </summary>
public class Event
{
    public long Id { get; set; }
    public int JobId { get; set; }
    public Job Job { get; set; } = null!;
    public EventState State { get; set; } = EventState.New;
    public Severity Severity { get; set; }
    public string? MessageId { get; set; }
    public string? MailFrom { get; set; }
    public string? Subject { get; set; }
    public string? RuleName { get; set; }
    public int HitCount { get; set; } = 1;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? NotifiedUtc { get; set; }
    public DateTime? AcknowledgedUtc { get; set; }
    public DateTime? ResolvedUtc { get; set; }
    public DateTime? LastStateChangeUtc { get; set; }
    public string? AcknowledgedBy { get; set; }
    public string? ResolvedBy { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
