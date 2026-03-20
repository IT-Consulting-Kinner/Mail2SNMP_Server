using System.Text.Json;
using Mail2SNMP.Models.Entities;
using Mail2SNMP.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Mail2SNMP.Infrastructure.Data;

/// <summary>
/// EF Core SaveChanges interceptor that automatically creates AuditEvent entries
/// for all CRUD operations on tracked entities (v5.8: consistent, automatic audit trail).
/// Excluded entities: AuditEvent (to prevent recursion), ProcessedMail (high-volume telemetry),
/// EventDedup (internal dedup state), AuthTicket (session housekeeping), WorkerLease (heartbeat noise).
/// </summary>
public class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    // Entity types that are excluded from automatic CRUD audit to prevent recursion or noise.
    private static readonly HashSet<Type> ExcludedTypes = new()
    {
        typeof(AuditEvent),
        typeof(ProcessedMail),
        typeof(EventDedup),
        typeof(AuthTicket),
        typeof(WorkerLease)
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
