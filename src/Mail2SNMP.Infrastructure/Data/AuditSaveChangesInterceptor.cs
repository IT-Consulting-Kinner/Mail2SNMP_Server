using Mail2SNMP.Models.Entities;
using Mail2SNMP.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Mail2SNMP.Infrastructure.Data;

/// <summary>
/// EF Core SaveChanges interceptor that automatically creates AuditEvent entries
/// for CRUD operations on tracked entities that are NOT already audited explicitly
/// by the service layer.
///
/// Peer-review P-1: this interceptor previously double-audited every config/event
/// mutation, because the services (MailboxService, SnmpTargetService, …,
/// EventService) ALSO call <c>_audit.LogAsync(...)</c> with richer, semantic action
/// names (e.g. "Event.Acknowledged" vs. a generic "Event.Updated") and sometimes
/// the real actor. The clear ownership rule now is:
///   - Config + Event entities (and the Job↔target join tables) are audited
///     EXPLICITLY by their services — excluded here to avoid duplicate rows.
///   - Everything else (e.g. ApiKey, AppUser, Setting) has no explicit service
///     audit, so the interceptor remains its automatic safety-net trail.
/// Plus the original exclusions: AuditEvent (recursion), ProcessedMail/EventDedup/
/// AuthTicket/WorkerLease (high-volume / internal housekeeping noise).
/// </summary>
public class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    // Entity types that are excluded from automatic CRUD audit to prevent recursion,
    // noise, or duplication with explicit service-layer audit calls.
    private static readonly HashSet<Type> ExcludedTypes = new()
    {
        // Recursion / high-volume / internal housekeeping.
        typeof(AuditEvent),
        typeof(ProcessedMail),
        typeof(EventDedup),
        typeof(AuthTicket),
        typeof(WorkerLease),
        // P-1: explicitly audited by the service layer — excluded to avoid the
        // double-write (interceptor row + service row for the same operation).
        typeof(Mailbox),
        typeof(SnmpTarget),
        typeof(WebhookTarget),
        typeof(Rule),
        typeof(Job),
        typeof(JobSnmpTarget),
        typeof(JobWebhookTarget),
        typeof(Schedule),
        typeof(MaintenanceWindow),
        typeof(Event)
    };

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
            RecordAuditEntries(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
            RecordAuditEntries(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void RecordAuditEntries(DbContext context)
    {
        context.ChangeTracker.DetectChanges();

        var entries = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(e => !ExcludedTypes.Contains(e.Entity.GetType()))
            .ToList();

        foreach (var entry in entries)
        {
            var entityType = entry.Entity.GetType().Name;
            var entityId = GetPrimaryKeyValue(entry);
            var action = entry.State switch
            {
                EntityState.Added => $"{entityType}.Created",
                EntityState.Modified => $"{entityType}.Updated",
                EntityState.Deleted => $"{entityType}.Deleted",
                _ => $"{entityType}.Unknown"
            };

            // Build a compact details string with changed property names (not values, to avoid leaking secrets)
            string? details = null;
            if (entry.State == EntityState.Modified)
            {
                var changedProps = entry.Properties
                    .Where(p => p.IsModified && p.Metadata.Name != "RowVersion")
                    .Select(p => p.Metadata.Name)
                    .ToList();
                if (changedProps.Count > 0)
                    details = $"Changed: {string.Join(", ", changedProps)}";
            }

            // Truncate details to 4096 chars max (matches AuditEvent.Details constraint)
            if (details is not null && details.Length > 4096)
                details = details[..4096];

            context.Set<AuditEvent>().Add(new AuditEvent
            {
                TimestampUtc = DateTime.UtcNow,
                ActorType = ActorType.System,
                ActorId = "ef-interceptor",
                Action = action,
                TargetType = entityType,
                TargetId = entityId,
                Details = details,
                Result = AuditResult.Success
            });
        }
    }

    private static string? GetPrimaryKeyValue(EntityEntry entry)
    {
        var keyProps = entry.Metadata.FindPrimaryKey()?.Properties;
        if (keyProps is null || keyProps.Count == 0)
            return null;

        if (keyProps.Count == 1)
            return entry.Property(keyProps[0].Name).CurrentValue?.ToString();

        return string.Join(",", keyProps.Select(p => entry.Property(p.Name).CurrentValue?.ToString()));
    }
}
