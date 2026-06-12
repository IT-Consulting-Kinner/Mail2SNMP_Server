using Mail2SNMP.Models.Enums;

namespace Mail2SNMP.Models.Entities;

/// <summary>
/// Immutable audit log entry recording configuration changes, login events, and system actions.
/// </summary>
public class AuditEvent
{
    /// <summary>Surrogate primary key. 64-bit because the audit log is append-only and high-volume. Database-generated.</summary>
    public long Id { get; set; }

    /// <summary>UTC timestamp of when the audited action occurred. Defaults to the current UTC time.</summary>
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Version of the audit-record schema, allowing the <see cref="Details"/> shape to evolve while
    /// keeping older rows interpretable. Defaults to 1.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Kind of actor that initiated the action: human <see cref="ActorType.User"/>, background <see cref="ActorType.Service"/>, or <see cref="ActorType.System"/>.</summary>
    public ActorType ActorType { get; set; }

    /// <summary>Identifier of the actor (e.g. user name, service name, or API-key id) interpreted in the context of <see cref="ActorType"/>.</summary>
    public string ActorId { get; set; } = string.Empty;

    /// <summary>The audited operation, typically a verb or verb-noun code (e.g. <c>"Login"</c>, <c>"Job.Update"</c>).</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Type of the entity the action targeted (e.g. <c>"Job"</c>, <c>"Mailbox"</c>); <c>null</c> for actions with no specific target.</summary>
    public string? TargetType { get; set; }

    /// <summary>Identifier of the targeted entity, paired with <see cref="TargetType"/>; <c>null</c> when not applicable.</summary>
    public string? TargetId { get; set; }

    /// <summary>
    /// Free-form or structured (often JSON) supplementary detail about the action, whose shape is
    /// governed by <see cref="SchemaVersion"/>. <c>null</c> when no extra detail was captured.
    /// </summary>
    public string? Details { get; set; }

    /// <summary>Source IP address of the request that triggered the action; <c>null</c> when unavailable (e.g. system actions).</summary>
    public string? IpAddress { get; set; }

    /// <summary>User-Agent header of the originating request; <c>null</c> when unavailable.</summary>
    public string? UserAgent { get; set; }

    /// <summary>Correlation id linking this audit entry to a request trace or related entries; <c>null</c> if no correlation context existed.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Outcome of the action: <see cref="AuditResult.Success"/>, <see cref="AuditResult.Failure"/>,
    /// or <see cref="AuditResult.Denied"/> (authorization rejection). Defaults to <see cref="AuditResult.Success"/>.
    /// </summary>
    public AuditResult Result { get; set; } = AuditResult.Success;
}
