using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Core.Services;
using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Models.Entities;
using Mail2SNMP.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Mail2SNMP.Infrastructure.Services;

/// <summary>
/// Manages the event lifecycle with a validated state machine and built-in deduplication.
/// </summary>
public class EventService : IEventService
{
    private readonly Mail2SnmpDbContext _db;
    private readonly IAuditService _audit;
    private readonly IEnumerable<INotificationChannel> _channels;
    private readonly ILogger<EventService> _logger;

    // Valid state transitions: New → Notified → Acknowledged → Resolved.
    // Suppressed can be reached from New, Notified, or Acknowledged.
    private static readonly Dictionary<EventState, HashSet<EventState>> ValidTransitions = new()
    {
        [EventState.New] = new() { EventState.Acknowledged, EventState.Resolved, EventState.Suppressed, EventState.Notified },
        [EventState.Notified] = new() { EventState.Acknowledged, EventState.Resolved, EventState.Suppressed },
        [EventState.Acknowledged] = new() { EventState.Resolved, EventState.Suppressed },
        [EventState.Resolved] = new(),
        [EventState.Suppressed] = new(),
        [EventState.Expired] = new(),
    };

    public EventService(Mail2SnmpDbContext db, IAuditService audit, IEnumerable<INotificationChannel> channels, ILogger<EventService> logger)
    {
        _db = db;
        _audit = audit;
        _channels = channels;
        _logger = logger;
    }

    /// <summary>
    /// Returns up to 500 events ordered by creation date, with optional state and job filters.
    /// </summary>
    public async Task<IReadOnlyList<Event>> GetAllAsync(EventState? stateFilter = null, int? jobId = null, CancellationToken ct = default)
    {
        var query = _db.Events.AsNoTracking().Include(e => e.Job).AsQueryable();
        if (stateFilter.HasValue)
            query = query.Where(e => e.State == stateFilter.Value);
        if (jobId.HasValue)
            query = query.Where(e => e.JobId == jobId.Value);
        return await query.OrderByDescending(e => e.CreatedUtc).Take(500).ToListAsync(ct);
    }

    /// <summary>
    /// Returns a single event by its identifier, including the related Job.
    /// </summary>
    public async Task<Event?> GetByIdAsync(long id, CancellationToken ct = default)
        => await _db.Events.AsNoTracking().Include(e => e.Job).FirstOrDefaultAsync(e => e.Id == id, ct);

    /// <summary>
    /// Creates a new event with deduplication. If a matching MessageId already exists within the
    /// job's dedup window, increments the existing event's hit count instead of inserting a duplicate.
    /// </summary>
    public async Task<Event> CreateAsync(Event evt, CancellationToken ct = default)
    {
        evt.LastStateChangeUtc = DateTime.UtcNow;

        // Enforce active event limit per job
        var job = await _db.Jobs.FindAsync(new object[] { evt.JobId }, ct);
        if (job != null)
        {
            var activeStates = new[] { EventState.New, EventState.Notified, EventState.Acknowledged };
            var activeCount = await _db.Events
                .CountAsync(e => e.JobId == evt.JobId && activeStates.Contains(e.State), ct);

            if (activeCount >= job.MaxActiveEvents)
            {
                // Expire oldest New event first, then oldest Notified
                // Never auto-expire Acknowledged events
                var toExpire = await _db.Events
                    .Where(e => e.JobId == evt.JobId && (e.State == EventState.New || e.State == EventState.Notified))
                    .OrderBy(e => e.State == EventState.New ? 0 : 1) // New first
                    .ThenBy(e => e.CreatedUtc)
                    .FirstOrDefaultAsync(ct);

                if (toExpire != null)
                {
                    toExpire.State = EventState.Expired;
                    toExpire.LastStateChangeUtc = DateTime.UtcNow;
                    _logger.LogInformation(
                        "Active event limit ({Limit}) reached for Job {JobId}. Expired Event {EventId} ({State})",
                        job.MaxActiveEvents, evt.JobId, toExpire.Id, toExpire.State);
                }
            }
        }

        // Event deduplication: if we have a MessageId, check the EventDedup table.
        // If a dedup entry exists within the job's window, increment HitCount on the
        // existing event instead. The dedup-check + increment must run inside a
        // serializable transaction to avoid lost updates under concurrent ingestion.
        if (!string.IsNullOrEmpty(evt.MessageId))
        {
            var dedupKey = EventDedupKeyGenerator.Generate(evt.MessageId, evt.JobId);
            return await CreateOrIncrementAsync(evt, dedupKey, ct);
        }

        // No MessageId — use fallback dedup key based on subject + sender + time
        var fallbackKey = EventDedupKeyGenerator.GenerateFallback(evt.Subject, evt.MailFrom, evt.CreatedUtc, evt.JobId);
        return await CreateOrIncrementAsync(evt, fallbackKey, ct);
    }

    /// <summary>
    /// Atomically checks for an existing dedup entry for <paramref name="dedupKey"/>;
    /// if found, increments the existing event's HitCount; otherwise inserts the new
    /// event together with its dedup entry. Wrapped in a Serializable transaction so
    /// concurrent producers can never both pass the dedup check and create duplicate
    /// events for the same key.
    /// </summary>
    private async Task<Event> CreateOrIncrementAsync(Event evt, string dedupKey, CancellationToken ct)
    {
        // Use the existing transaction if the caller already started one (e.g. integration tests)
        var ownTransaction = _db.Database.CurrentTransaction == null;
        var transaction = ownTransaction
            ? await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct)
            : null;
        try
        {
            var existingDedup = await _db.EventDedups
                .FirstOrDefaultAsync(d => d.DedupKeyHash == dedupKey && d.JobId == evt.JobId, ct);

            if (existingDedup != null)
            {
                var existingEvent = await _db.Events.FindAsync(new object[] { existingDedup.EventId }, ct);
                if (existingEvent != null)
                {
                    existingEvent.HitCount++;
                    existingDedup.LastSeenUtc = DateTime.UtcNow;
                    await _db.SaveChangesAsync(ct);
                    if (transaction != null) await transaction.CommitAsync(ct);
                    _logger.LogInformation(
                        "Duplicate event suppressed for Job {JobId}. Existing Event {EventId} HitCount={HitCount}",
                        evt.JobId, existingEvent.Id, existingEvent.HitCount);
                    return existingEvent;
                }
            }

            // No duplicate — insert the event and dedup entry together.
            _db.Events.Add(evt);
            _db.EventDedups.Add(new EventDedup
            {
                DedupKeyHash = dedupKey,
                JobId = evt.JobId,
                Event = evt,
                FirstSeenUtc = DateTime.UtcNow,
                LastSeenUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(ct);
            if (transaction != null) await transaction.CommitAsync(ct);
            return evt;
        }
        catch
        {
            if (transaction != null) await transaction.RollbackAsync(ct);
            throw;
        }
        finally
        {
            if (transaction != null) await transaction.DisposeAsync();
        }
    }

    /// <summary>
    /// Transitions the event to the Notified state after successful notification delivery.
    /// </summary>
    public async Task MarkAsNotifiedAsync(long id, CancellationToken ct = default)
    {
        var evt = await _db.Events.FindAsync(new object[] { id }, ct)
            ?? throw new KeyNotFoundException($"Event {id} not found.");

        ValidateStateTransition(evt, EventState.Notified);

        evt.State = EventState.Notified;
        evt.NotifiedUtc = DateTime.UtcNow;
        evt.LastStateChangeUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _logger.LogDebug("Event {EventId} marked as Notified", id);
    }

    /// <summary>
    /// Transitions the event to Acknowledged on behalf of the specified user
    /// and emits an mail2SNMPEventConfirmedNotification trap to all active SNMP targets.
    /// </summary>
    public async Task AcknowledgeAsync(long id, string userId, CancellationToken ct = default)
    {
        var evt = await _db.Events.FindAsync(new object[] { id }, ct)
            ?? throw new KeyNotFoundException($"Event {id} not found.");

        ValidateStateTransition(evt, EventState.Acknowledged);

        evt.State = EventState.Acknowledged;
        evt.AcknowledgedUtc = DateTime.UtcNow;
        evt.AcknowledgedBy = userId;
        evt.LastStateChangeUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(ActorType.User, userId, "Event.Acknowledged", "Event", id.ToString(), ct: ct);

        // Emit EventConfirmed SNMP trap (best-effort, must not fail the acknowledge action)
        try
        {
            var snmpChannel = _channels.FirstOrDefault(c => c.ChannelName == "snmp");
            if (snmpChannel != null)
                await snmpChannel.SendEventConfirmedAsync(id, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send EventConfirmed trap for event {EventId}", id);
        }
    }

    /// <summary>
    /// Transitions the event to Resolved on behalf of the specified user.
    /// </summary>
    public async Task ResolveAsync(long id, string userId, CancellationToken ct = default)
    {
        var evt = await _db.Events.FindAsync(new object[] { id }, ct)
            ?? throw new KeyNotFoundException($"Event {id} not found.");

        ValidateStateTransition(evt, EventState.Resolved);

        evt.State = EventState.Resolved;
        evt.ResolvedUtc = DateTime.UtcNow;
        evt.ResolvedBy = userId;
        evt.LastStateChangeUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(ActorType.User, userId, "Event.Resolved", "Event", id.ToString(), ct: ct);
    }

    /// <summary>
    /// Transitions the event to Suppressed, typically during a maintenance window.
    /// </summary>
    public async Task SuppressAsync(long id, CancellationToken ct = default)
    {
        var evt = await _db.Events.FindAsync(new object[] { id }, ct)
            ?? throw new KeyNotFoundException($"Event {id} not found.");

        ValidateStateTransition(evt, EventState.Suppressed);

        evt.State = EventState.Suppressed;
        evt.LastStateChangeUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(ActorType.System, "system", "Event.Suppressed", "Event", id.ToString(), ct: ct);
    }

    private static void ValidateStateTransition(Event evt, EventState targetState)
    {
        if (!ValidTransitions.TryGetValue(evt.State, out var allowed) || !allowed.Contains(targetState))
        {
            throw new InvalidOperationException(
                $"Cannot transition event {evt.Id} from {evt.State} to {targetState}.");
        }
    }

    /// <summary>
    /// Re-sends notifications for an existing event through its assigned targets.
    /// </summary>
    public async Task ReplayAsync(long id, CancellationToken ct = default)
    {
        var evt = await _db.Events
            .Include(e => e.Job).ThenInclude(j => j.Mailbox)
            .Include(e => e.Job).ThenInclude(j => j.JobSnmpTargets).ThenInclude(jst => jst.SnmpTarget)
            .Include(e => e.Job).ThenInclude(j => j.JobWebhookTargets).ThenInclude(jwt => jwt.WebhookTarget)
            .FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new KeyNotFoundException($"Event {id} not found.");

        var context = new Models.DTOs.NotificationContext
        {
            EventId = evt.Id,
            JobName = evt.Job.Name,
            Mailbox = evt.Job.Mailbox.Name,
            From = evt.MailFrom ?? "",
            Subject = evt.Subject ?? "",
            Severity = evt.Severity,
            RuleName = evt.RuleName ?? "",
            HitCount = evt.HitCount,
            TimestampUtc = evt.CreatedUtc,
            TrapTemplate = evt.Job.TrapTemplate,
            WebhookTemplate = evt.Job.WebhookTemplate,
            OidMapping = evt.Job.OidMapping
        };

        var channels = _channels.ToList();
        var snmpChannel = channels.FirstOrDefault(c => c.ChannelName == "snmp");
        var webhookChannel = channels.FirstOrDefault(c => c.ChannelName == "webhook");

        // Send to assigned SNMP targets
        foreach (var jst in evt.Job.JobSnmpTargets.Where(t => t.SnmpTarget.IsActive))
        {
            if (snmpChannel != null)
                await snmpChannel.SendToSnmpTargetAsync(context, jst.SnmpTarget, ct);
        }

        // Send to assigned Webhook targets
        foreach (var jwt in evt.Job.JobWebhookTargets.Where(t => t.WebhookTarget.IsActive))
        {
            if (webhookChannel != null)
                await webhookChannel.SendToWebhookTargetAsync(context, jwt.WebhookTarget, ct);
        }

        await _audit.LogAsync(ActorType.System, "system", "Event.Replayed", "Event", id.ToString(), ct: ct);
        _logger.LogInformation("Event {EventId} replayed successfully", id);
    }
}
