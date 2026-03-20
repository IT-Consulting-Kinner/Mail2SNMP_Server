using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Models.Entities;
using Mail2SNMP.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Mail2SNMP.Infrastructure.Services;

/// <summary>
/// Persists audit trail entries to the database. Truncates long detail strings to 4 KB.
/// </summary>
public class AuditService : IAuditService
{
    private readonly Mail2SnmpDbContext _db;
    private readonly ILogger<AuditService> _logger;

    public AuditService(Mail2SnmpDbContext db, ILogger<AuditService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Returns up to 1000 audit events ordered by timestamp, with optional action and date-range filters.
    /// </summary>
    public async Task<IReadOnlyList<AuditEvent>> GetAllAsync(string? actionFilter = null, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var query = _db.AuditEvents.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(actionFilter))
            query = query.Where(a => a.Action.Contains(actionFilter));
        if (from.HasValue)
            query = query.Where(a => a.TimestampUtc >= from.Value);
        if (to.HasValue)
            query = query.Where(a => a.TimestampUtc <= to.Value);
        return await query.OrderByDescending(a => a.TimestampUtc).Take(1000).ToListAsync(ct);
    }

    /// <summary>
    /// Persists a new audit trail entry. Detail strings longer than 4 KB are truncated.
    /// </summary>
    public async Task LogAsync(ActorType actorType, string actorId, string action, string? targetType = null,
        string? targetId = null, string? details = null, AuditResult result = AuditResult.Success,
        string? ipAddress = null, string? userAgent = null, string? correlationId = null, CancellationToken ct = default)
    {
        // Truncate details to 4KB
        if (details?.Length > 4096)
            details = details[..4096];

        var audit = new AuditEvent
        {
            ActorType = actorType,
            ActorId = actorId,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Details = details,
            Result = result,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            CorrelationId = correlationId
        };

        _db.AuditEvents.Add(audit);
        await _db.SaveChangesAsync(ct);

        _logger.LogDebug("Audit: {Action} by {ActorType}:{ActorId} on {TargetType}:{TargetId}",
            action, actorType, actorId, targetType, targetId);
    }
}
