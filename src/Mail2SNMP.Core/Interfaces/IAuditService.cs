using Mail2SNMP.Models.Entities;
using Mail2SNMP.Models.Enums;

namespace Mail2SNMP.Core.Interfaces;

/// <summary>
/// Service for recording and querying audit trail entries.
/// Every configuration change, login event, and significant system action is logged here.
/// Enterprise edition captures additional context (IP address, UserAgent, CorrelationId).
/// </summary>
public interface IAuditService
{
    /// <summary>
    /// Retrieves audit events with optional filters for action type and time range.
    /// </summary>
    Task<IReadOnlyList<AuditEvent>> GetAllAsync(string? actionFilter = null, DateTime? from = null, DateTime? to = null, CancellationToken ct = default);

    /// <summary>
    /// Logs a new audit event. Enterprise edition should include ipAddress, userAgent,
    /// and correlationId for full traceability.
    /// </summary>
    Task LogAsync(ActorType actorType, string actorId, string action, string? targetType = null, string? targetId = null, string? details = null, AuditResult result = AuditResult.Success, string? ipAddress = null, string? userAgent = null, string? correlationId = null, CancellationToken ct = default);
}
