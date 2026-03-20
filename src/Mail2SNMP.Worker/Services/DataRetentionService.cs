using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Models.Configuration;
using Mail2SNMP.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Mail2SNMP.Worker.Services;

/// <summary>
/// Background service that periodically cleans up old data based on retention settings.
/// Handles: expired events, old resolved/suppressed events, processed mails, audit entries,
/// dead letter entries, and event dedup entries.
/// </summary>
public class DataRetentionService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DataRetentionService> _logger;
    private readonly EventSettings _eventSettings;
    private readonly RetentionSettings _retentionSettings;
    private readonly TimeSpan _interval = TimeSpan.FromHours(1);

    public DataRetentionService(
        IServiceScopeFactory scopeFactory,
        ILogger<DataRetentionService> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _eventSettings = configuration.GetSection("Events").Get<EventSettings>() ?? new EventSettings();
        _retentionSettings = configuration.GetSection("Retention").Get<RetentionSettings>() ?? new RetentionSettings();
    }

    /// <summary>
    /// Waits for initial startup, then runs the retention cleanup cycle at a fixed hourly interval.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "DataRetentionService started. AutoExpireDays={AutoExpire}, ResolvedRetentionDays={Resolved}, " +
            "ProcessedMailDays={ProcessedMail}, AuditDays={Audit}, DeadLetterDays={DeadLetter}, MaxAuditEntries={MaxAudit}",
            _eventSettings.AutoExpireDays, _eventSettings.ResolvedRetentionDays,
            _retentionSettings.ProcessedMailDays, _retentionSettings.AuditEventDays,
            _retentionSettings.DeadLetterDays, _retentionSettings.MaxAuditEntries);

        // Initial delay to let application start up
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunRetentionCleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Data retention cleanup failed. Will retry in {Interval}", _interval);
            }

            await Task.Delay(_interval, stoppingToken);
        }

        _logger.LogInformation("DataRetentionService stopped");
    }

    /// <summary>
    /// Executes all retention cleanup steps in sequence: expire old events, delete terminal-state events,
    /// purge processed mail records, trim audit entries, remove dead letters, and clean event dedup entries.
    /// </summary>
    private async Task RunRetentionCleanupAsync(CancellationToken ct)
    {
        _logger.LogDebug("Starting data retention cleanup cycle");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Mail2SnmpDbContext>();

        var totalDeleted = 0;

        // 1. Auto-expire old New/Notified events (not yet acknowledged)
        totalDeleted += await ExpireOldEventsAsync(db, ct);

        // 2. Delete old resolved/suppressed/expired events beyond retention period
        totalDeleted += await DeleteOldEventsAsync(db, ct);

        // 3. Delete old processed mail records
        totalDeleted += await DeleteOldProcessedMailsAsync(db, ct);

        // 4. Delete old audit events (by age and max count)
        totalDeleted += await DeleteOldAuditEventsAsync(db, ct);

        // 5. Delete old dead letter entries
        totalDeleted += await DeleteOldDeadLettersAsync(db, ct);

        // 6. Delete old event dedup entries
        totalDeleted += await DeleteOldEventDedupsAsync(db, ct);

        // 7. Delete expired authentication tickets (server-side session store)
        totalDeleted += await DeleteExpiredAuthTicketsAsync(db, ct);

        if (totalDeleted > 0)
            _logger.LogInformation("Data retention cleanup completed. Total records removed: {Count}", totalDeleted);
        else
            _logger.LogDebug("Data retention cleanup completed. No records to remove.");
    }

    /// <summary>
    /// Transitions New and Notified events older than the auto-expire threshold to the Expired state.
    /// </summary>
    private async Task<int> ExpireOldEventsAsync(Mail2SnmpDbContext db, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-_eventSettings.AutoExpireDays);
        var expirableStates = new[] { EventState.New, EventState.Notified };

        var eventsToExpire = await db.Events
            .Where(e => expirableStates.Contains(e.State) && e.CreatedUtc < cutoff)
            .Take(1000)
            .ToListAsync(ct);

        foreach (var evt in eventsToExpire)
        {
            evt.State = EventState.Expired;
            evt.LastStateChangeUtc = DateTime.UtcNow;
        }

        if (eventsToExpire.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Auto-expired {Count} events older than {Days} days", eventsToExpire.Count, _eventSettings.AutoExpireDays);
        }

        return eventsToExpire.Count;
    }

    /// <summary>
    /// Deletes Resolved, Suppressed, and Expired events whose last state change exceeds the retention period,
    /// along with their associated dedup entries.
    /// </summary>
    private async Task<int> DeleteOldEventsAsync(Mail2SnmpDbContext db, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-_eventSettings.ResolvedRetentionDays);
        var terminalStates = new[] { EventState.Resolved, EventState.Suppressed, EventState.Expired };

        var eventsToDelete = await db.Events
            .Where(e => terminalStates.Contains(e.State) && e.LastStateChangeUtc < cutoff)
            .Take(1000)
            .ToListAsync(ct);

        if (eventsToDelete.Count > 0)
        {
            // Also delete associated event dedup entries
            var eventIds = eventsToDelete.Select(e => e.Id).ToList();
            var dedups = await db.EventDedups.Where(d => eventIds.Contains(d.EventId)).ToListAsync(ct);
            db.EventDedups.RemoveRange(dedups);

            db.Events.RemoveRange(eventsToDelete);
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Deleted {Count} terminal-state events older than {Days} days", eventsToDelete.Count, _eventSettings.ResolvedRetentionDays);
        }

        return eventsToDelete.Count;
    }

    /// <summary>
    /// Deletes processed mail idempotency records older than the configured retention period.
    /// </summary>
    private async Task<int> DeleteOldProcessedMailsAsync(Mail2SnmpDbContext db, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-_retentionSettings.ProcessedMailDays);

        var toDelete = await db.ProcessedMails
            .Where(p => p.ProcessedUtc < cutoff)
            .Take(5000)
            .ToListAsync(ct);

        if (toDelete.Count > 0)
        {
            db.ProcessedMails.RemoveRange(toDelete);
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Deleted {Count} processed mail records older than {Days} days", toDelete.Count, _retentionSettings.ProcessedMailDays);
        }

        return toDelete.Count;
    }

    /// <summary>
    /// Deletes audit log entries older than the configured age limit and trims excess entries
    /// beyond the maximum count, keeping the most recent records.
    /// </summary>
    private async Task<int> DeleteOldAuditEventsAsync(Mail2SnmpDbContext db, CancellationToken ct)
    {
        var deleted = 0;

        // Delete by age
        var cutoff = DateTime.UtcNow.AddDays(-_retentionSettings.AuditEventDays);
        var oldAudit = await db.AuditEvents
            .Where(a => a.TimestampUtc < cutoff)
            .Take(5000)
            .ToListAsync(ct);

        if (oldAudit.Count > 0)
        {
            db.AuditEvents.RemoveRange(oldAudit);
            await db.SaveChangesAsync(ct);
            deleted += oldAudit.Count;
            _logger.LogInformation("Deleted {Count} audit events older than {Days} days", oldAudit.Count, _retentionSettings.AuditEventDays);
        }

        // Delete by max count (keep the most recent)
        var totalCount = await db.AuditEvents.CountAsync(ct);
        if (totalCount > _retentionSettings.MaxAuditEntries)
        {
            var excess = totalCount - _retentionSettings.MaxAuditEntries;
            var excessLimit = Math.Min(excess, 5000);
            var excessAudit = await db.AuditEvents
                .OrderBy(a => a.TimestampUtc)
                .Take(excessLimit)
                .ToListAsync(ct);

            db.AuditEvents.RemoveRange(excessAudit);
            await db.SaveChangesAsync(ct);
            deleted += excessAudit.Count;
            _logger.LogInformation("Deleted {Count} excess audit events (max {Max})", excessAudit.Count, _retentionSettings.MaxAuditEntries);
        }

        return deleted;
    }

    /// <summary>
    /// Deletes dead letter entries older than the configured retention period.
    /// v5.8: All entries older than the threshold are deleted regardless of status.
    /// Abandoned entries are terminal. Old Pending/Locked entries indicate stale retries
    /// or orphaned locks and must also be cleaned up.
    /// </summary>
    private async Task<int> DeleteOldDeadLettersAsync(Mail2SnmpDbContext db, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-_retentionSettings.DeadLetterDays);
        var toDelete = await db.DeadLetterEntries
            .Where(d => d.CreatedUtc < cutoff)
            .Take(1000)
            .ToListAsync(ct);

        if (toDelete.Count > 0)
        {
            db.DeadLetterEntries.RemoveRange(toDelete);
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Deleted {Count} dead letter entries older than {Days} days", toDelete.Count, _retentionSettings.DeadLetterDays);
        }

        return toDelete.Count;
    }

    /// <summary>
    /// Deletes event deduplication entries whose last-seen timestamp exceeds twice the default dedup window.
    /// </summary>
    private async Task<int> DeleteOldEventDedupsAsync(Mail2SnmpDbContext db, CancellationToken ct)
    {
        // Delete dedup entries older than 2x the default dedup window
        var cutoffMinutes = _eventSettings.DefaultDedupWindowMinutes * 2;
        var cutoff = DateTime.UtcNow.AddMinutes(-cutoffMinutes);

        var toDelete = await db.EventDedups
            .Where(d => d.LastSeenUtc < cutoff)
            .Take(1000)
            .ToListAsync(ct);

        if (toDelete.Count > 0)
        {
            db.EventDedups.RemoveRange(toDelete);
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Deleted {Count} event dedup entries older than {Minutes} minutes", toDelete.Count, cutoffMinutes);
        }

        return toDelete.Count;
    }

    /// <summary>
    /// Deletes expired authentication tickets from the server-side session store.
    /// </summary>
    private async Task<int> DeleteExpiredAuthTicketsAsync(Mail2SnmpDbContext db, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var toDelete = await db.AuthTickets
            .Where(t => t.ExpiresUtc != null && t.ExpiresUtc < now)
            .Take(1000)
            .ToListAsync(ct);

        if (toDelete.Count > 0)
        {
            db.AuthTickets.RemoveRange(toDelete);
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Deleted {Count} expired auth tickets", toDelete.Count);
        }

        return toDelete.Count;
    }
}
