using Mail2SNMP.Models.Enums;

namespace Mail2SNMP.Models.Entities;

/// <summary>
/// A failed webhook delivery queued for automatic retry with exponential backoff (Enterprise only).
/// </summary>
public class DeadLetterEntry
{
    public long Id { get; set; }
    public int WebhookTargetId { get; set; }
    public WebhookTarget WebhookTarget { get; set; } = null!;
    public long EventId { get; set; }
    public Event Event { get; set; } = null!;
    public string PayloadJson { get; set; } = string.Empty;
    public string? LastError { get; set; }
    public int AttemptCount { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? NextRetryUtc { get; set; }
    public DateTime? LockedUntilUtc { get; set; }
    public string? LockedByInstanceId { get; set; }
    public DeadLetterStatus Status { get; set; } = DeadLetterStatus.Pending;

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
