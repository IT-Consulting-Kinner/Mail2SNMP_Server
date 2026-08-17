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
    /// Logs a new audit event, attributing it to whoever is responsible for the current
    /// operation as reported by <see cref="ICurrentActor"/>.
    /// </summary>
    /// <remarks>
    /// This is the overload configuration changes should use. Every such change used to
    /// pass <see cref="ActorType.System"/> / <c>"system"</c> literally, so the audit log
    /// recorded that a job was deleted but never by whom — the one question an audit trail
    /// exists to answer. Pass the explicit-actor overload only where the actor is genuinely
    /// not the ambient one, such as recording a failed sign-in for a named account.
    /// </remarks>
    /// <param name="action">Semantic action name, e.g. <c>"Job.Deleted"</c>.</param>
    /// <param name="targetType">The entity type the action applied to.</param>
    /// <param name="targetId">The entity's identifier.</param>
    /// <param name="details">Optional context. Never include credentials or field values that may contain them.</param>
    /// <param name="result">Whether the action succeeded.</param>
    /// <param name="ct">Token used to cancel the write.</param>
    Task LogAsync(string action, string? targetType = null, string? targetId = null, string? details = null, AuditResult result = AuditResult.Success, CancellationToken ct = default);

    /// <summary>
    /// Logs a new audit event for an explicitly named actor. Enterprise edition should
    /// include ipAddress, userAgent, and correlationId for full traceability.
    /// </summary>
    Task LogAsync(ActorType actorType, string actorId, string action, string? targetType = null, string? targetId = null, string? details = null, AuditResult result = AuditResult.Success, string? ipAddress = null, string? userAgent = null, string? correlationId = null, CancellationToken ct = default);
}
