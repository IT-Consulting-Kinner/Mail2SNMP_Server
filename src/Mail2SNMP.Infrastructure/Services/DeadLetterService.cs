using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Models.DTOs;
using Mail2SNMP.Models.Entities;
using Mail2SNMP.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Mail2SNMP.Infrastructure.Services;

/// <summary>
/// Manages failed delivery entries (dead letters) with retry scheduling. The queue holds
/// both webhook and SNMP (UC-3) failures; filtering and bulk retry are target-kind neutral.
/// </summary>
public class DeadLetterService : IDeadLetterService
{
    private readonly Mail2SnmpDbContext _db;
    private readonly ILogger<DeadLetterService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeadLetterService"/> class.
    /// </summary>
    /// <param name="db">The database context used to read and persist dead-letter entries.</param>
    /// <param name="logger">The logger for dead-letter creation and retry diagnostics.</param>
    public DeadLetterService(Mail2SnmpDbContext db, ILogger<DeadLetterService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Builds the filtered queryable shared by <see cref="QueryAsync"/> and
    /// <see cref="RetryAllAsync"/> so a bulk retry can never act on a different set than
    /// the one the operator is looking at.
    /// </summary>
    private IQueryable<DeadLetterEntry> Filtered(IQueryable<DeadLetterEntry> source, DeadLetterQuery query)
    {
        if (query.Status is { } status)
            source = source.Where(d => d.Status == status);

        // An entry carries exactly one of the two foreign keys (UC-3), so the kind
        // filter is expressible as a null-check rather than a stored discriminator.
        if (query.Kind == DeadLetterTargetKind.Snmp)
            source = source.Where(d => d.SnmpTargetId != null);
        else if (query.Kind == DeadLetterTargetKind.Webhook)
            source = source.Where(d => d.WebhookTargetId != null);

        if (query.TargetId is { } targetId)
        {
            source = query.Kind == DeadLetterTargetKind.Snmp
                ? source.Where(d => d.SnmpTargetId == targetId)
                : source.Where(d => d.WebhookTargetId == targetId);
        }

        return source;
    }

    /// <summary>
    /// Returns one page of dead-letter entries matching the filter, newest first, including
    /// the related webhook or SNMP target, plus the total count before paging.
    /// </summary>
    public async Task<DeadLetterQueryResult> QueryAsync(DeadLetterQuery query, CancellationToken ct = default)
    {
        var filtered = Filtered(_db.DeadLetterEntries.AsNoTracking(), query);

        // Counted before paging: the UI must be able to say how much of the queue it is
        // showing. Silently returning a truncated list reads as "this is everything".
        var total = await filtered.CountAsync(ct);

        var skip = Math.Max(0, query.Skip);
        var take = Math.Clamp(query.Take, 1, DeadLetterQuery.MaxTake);

        var entries = await filtered
            .Include(d => d.WebhookTarget)
            .Include(d => d.SnmpTarget)
            .OrderByDescending(d => d.CreatedUtc)
            .ThenByDescending(d => d.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        return new DeadLetterQueryResult { Entries = entries, TotalCount = total };
    }

    /// <summary>
    /// Returns the newest dead-letter entries (capped at <see cref="DeadLetterQuery.MaxTake"/>)
    /// ordered by creation date, including the related webhook or SNMP target.
    /// </summary>
    public async Task<IReadOnlyList<DeadLetterEntry>> GetAllAsync(CancellationToken ct = default)
        => (await QueryAsync(new DeadLetterQuery { Take = DeadLetterQuery.MaxTake }, ct)).Entries;

    /// <summary>
    /// Records a new dead-letter entry and schedules the first retry in 15 minutes.
    /// </summary>
    public async Task<DeadLetterEntry> CreateAsync(DeadLetterEntry entry, CancellationToken ct = default)
    {
        entry.NextRetryUtc = DateTime.UtcNow.AddMinutes(15);
        _db.DeadLetterEntries.Add(entry);
        await _db.SaveChangesAsync(ct);
        // AR-1: single funnel for dead-letter creation — every failed delivery passes
        // through here, so this is the one place the counters must live. UC-3
        // (verified fix): SNMP entries get their own counter and log line instead of
        // silently inflating the webhook-named metric.
        if (entry.SnmpTargetId is not null)
        {
            Mail2SnmpMetrics.SnmpDeadLetterTotal.Inc();
            _logger.LogWarning("Dead letter created for SNMP target {TargetId}, event {EventId}: {Error}",
                entry.SnmpTargetId, entry.EventId, entry.LastError);
        }
        else
        {
            Mail2SnmpMetrics.WebhookDeadLetterTotal.Inc();
            _logger.LogWarning("Dead letter created for webhook target {TargetId}, event {EventId}: {Error}",
                entry.WebhookTargetId, entry.EventId, entry.LastError);
        }
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
        // An Abandoned entry has AttemptCount >= MaxAttempts, so the retry worker's
        // claim query (AttemptCount < max) skipped it forever: the API and UI reported
        // "queued for retry" and nothing ever happened. An explicit operator retry is a
        // fresh start, so the attempt counter is reset along with the status.
        entry.AttemptCount = 0;
        entry.LastError = null;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Dead letter {Id} re-queued for retry by operator request.", id);
    }

    /// <summary>
    /// Resets every entry matching the filter — webhook or SNMP — for immediate retry.
    /// </summary>
    /// <remarks>
    /// Applies exactly the same reset as <see cref="RetryAsync"/>. The two used to differ:
    /// the bulk path left <c>Status</c> and <c>AttemptCount</c> untouched, so an entry that
    /// had exhausted its attempts was reported as "queued for retry" while the worker's
    /// claim query (<c>AttemptCount &lt; max</c>) kept skipping it. An operator-initiated
    /// retry is a fresh start in both paths.
    /// </remarks>
    public async Task<int> RetryAllAsync(DeadLetterQuery filter, CancellationToken ct = default)
    {
        var entries = await Filtered(_db.DeadLetterEntries, filter).ToListAsync(ct);

        var now = DateTime.UtcNow;
        foreach (var entry in entries)
        {
            entry.Status = DeadLetterStatus.Pending;
            entry.NextRetryUtc = now;
            entry.LockedUntilUtc = null;
            entry.LockedByInstanceId = null;
            entry.AttemptCount = 0;
            entry.LastError = null;
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Bulk retry requested (Status={Status}, Kind={Kind}, TargetId={TargetId}): {Count} entries re-queued",
            filter.Status, filter.Kind, filter.TargetId, entries.Count);
        return entries.Count;
    }
}
