using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Models.Entities;
using Mail2SNMP.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Mail2SNMP.Infrastructure.Services;

/// <summary>
/// Manages failed webhook delivery entries (dead letters) with retry scheduling.
/// </summary>
public class DeadLetterService : IDeadLetterService
{
    private readonly Mail2SnmpDbContext _db;
    private readonly ILogger<DeadLetterService> _logger;

    public DeadLetterService(Mail2SnmpDbContext db, ILogger<DeadLetterService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Returns up to 500 dead-letter entries ordered by creation date, including the related webhook target.
    /// </summary>
    public async Task<IReadOnlyList<DeadLetterEntry>> GetAllAsync(CancellationToken ct = default)
        => await _db.DeadLetterEntries.AsNoTracking()
            .Include(d => d.WebhookTarget)
            .OrderByDescending(d => d.CreatedUtc)
            .Take(500)
            .ToListAsync(ct);

    /// <summary>
    /// Records a new dead-letter entry and schedules the first retry in 15 minutes.
    /// </summary>
    public async Task<DeadLetterEntry> CreateAsync(DeadLetterEntry entry, CancellationToken ct = default)
    {
        entry.NextRetryUtc = DateTime.UtcNow.AddMinutes(15);
        _db.DeadLetterEntries.Add(entry);
        await _db.SaveChangesAsync(ct);
        _logger.LogWarning("Dead letter created for webhook target {TargetId}, event {EventId}: {Error}",
            entry.WebhookTargetId, entry.EventId, entry.LastError);
        return entry;
    }

    /// <summary>
    /// Resets a single dead-letter entry for immediate retry by clearing its lock and setting the next retry time to now.
    /// </summary>
    public async Task RetryAsync(long id, CancellationToken ct = default)
    {
        var entry = await _db.DeadLetterEntries.FindAsync(new object[] { id }, ct)
            ?? throw new KeyNotFoundException($"Dead letter {id} not found.");
        entry.Status = DeadLetterStatus.Pending;
        entry.NextRetryUtc = DateTime.UtcNow;
        entry.LockedUntilUtc = null;
        entry.LockedByInstanceId = null;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Resets all pending dead-letter entries for the given webhook target for immediate retry.
    /// </summary>
    public async Task RetryAllAsync(int webhookTargetId, CancellationToken ct = default)
    {
        var entries = await _db.DeadLetterEntries
            .Where(d => d.WebhookTargetId == webhookTargetId && d.Status == DeadLetterStatus.Pending)
            .ToListAsync(ct);

        foreach (var entry in entries)
        {
            entry.NextRetryUtc = DateTime.UtcNow;
            entry.LockedUntilUtc = null;
            entry.LockedByInstanceId = null;
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Retry all requested for webhook target {TargetId}: {Count} entries", webhookTargetId, entries.Count);
    }
}
