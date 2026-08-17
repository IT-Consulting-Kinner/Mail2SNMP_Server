using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Models.DTOs;
using Mail2SNMP.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace Mail2SNMP.Infrastructure.Services;

/// <summary>
/// Single source of the dashboard's aggregate counters, shared by the REST endpoint and
/// the management UI so the two can never disagree about what the system looks like.
/// </summary>
public class DashboardService : IDashboardService
{
    /// <summary>
    /// Event states that count as "open": everything an operator still has to deal with.
    /// Acknowledged belongs here — someone has seen it, but it is not resolved.
    /// </summary>
    private static readonly EventState[] ActiveEventStates =
        { EventState.New, EventState.Notified, EventState.Acknowledged };

    private readonly Mail2SnmpDbContext _db;
    private readonly IMaintenanceWindowService _maintenance;
    private readonly ILicenseProvider _license;

    /// <summary>
    /// Initializes a new instance of the <see cref="DashboardService"/> class.
    /// </summary>
    /// <param name="db">Database context used for the server-side count aggregates.</param>
    /// <param name="maintenance">Supplies the current maintenance state and window name.</param>
    /// <param name="license">Supplies the licensed edition shown on the dashboard.</param>
    public DashboardService(
        Mail2SnmpDbContext db,
        IMaintenanceWindowService maintenance,
        ILicenseProvider license)
    {
        _db = db;
        _maintenance = maintenance;
        _license = license;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Counts run server-side. The earlier implementations materialized full entity graphs
    /// (jobs with four-level includes, 500 events, 500 dead letters) just to render six
    /// integers, and two counters were wrong as a result: open events were capped by the
    /// service layer's <c>Take(500)</c>, and pending dead letters counted the loaded page
    /// across every status rather than the pending ones.
    /// </remarks>
    public async Task<DashboardDto> GetAsync(CancellationToken ct = default)
    {
        var activeMailboxes = await _db.Mailboxes.CountAsync(m => m.IsActive, ct);
        var mailboxesInError = await _db.Mailboxes.CountAsync(m => m.IsActive && m.LastError != null, ct);
        var activeJobs = await _db.Jobs.CountAsync(j => j.IsActive, ct);
        var activeSchedules = await _db.Schedules.CountAsync(s => s.IsActive, ct);
        var openEvents = await _db.Events.CountAsync(e => ActiveEventStates.Contains(e.State), ct);
        var pendingDeadLetters = await _db.DeadLetterEntries.CountAsync(d => d.Status == DeadLetterStatus.Pending, ct);
        var inMaintenance = await _maintenance.IsInMaintenanceAsync(ct: ct);

        string? maintenanceWindowName = null;
        if (inMaintenance)
        {
            var now = DateTime.UtcNow;
            var windows = await _maintenance.GetAllAsync(ct);
            maintenanceWindowName = windows
                .FirstOrDefault(w => w.IsActive && w.StartUtc <= now && w.EndUtc >= now)?.Name;
        }

        return new DashboardDto
        {
            ActiveMailboxes = activeMailboxes,
            ActiveJobs = activeJobs,
            ActiveSchedules = activeSchedules,
            OpenEvents = openEvents,
            PendingDeadLetters = pendingDeadLetters,
            MaintenanceActive = inMaintenance,
            MaintenanceWindowName = maintenanceWindowName,
            // UC-1: health is computed, not asserted. A broken active mailbox means
            // ingestion — the product's core loop — is failing for its jobs, which the
            // dashboard must surface instead of reporting a hardcoded green.
            MailboxesInError = mailboxesInError,
            IsHealthy = mailboxesInError == 0,
            LicenseEdition = _license.Current.Edition.ToString()
        };
    }
}
