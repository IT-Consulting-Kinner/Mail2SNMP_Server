using Mail2SNMP.Models.Enums;

namespace Mail2SNMP.Models.Entities;

/// <summary>
/// Immutable audit log entry recording configuration changes, login events, and system actions.
/// </summary>
public class AuditEvent
{
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public int SchemaVersion { get; set; } = 1;
    public ActorType ActorType { get; set; }
    public string ActorId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? TargetType { get; set; }
    public string? TargetId { get; set; }
    public string? Details { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? CorrelationId { get; set; }
    public AuditResult Result { get; set; } = AuditResult.Success;
}
