using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Models.DTOs;
using Mail2SNMP.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace Mail2SNMP.Api.Endpoints;

/// <summary>
/// REST API endpoint that exposes aggregate dashboard counters for the UI home page.
/// </summary>
public static class DashboardEndpoints
{
    /// <summary>
    /// Registers <c>GET /api/v1/dashboard</c>, which aggregates active mailbox, job and
    /// schedule counts, open-event and pending dead-letter totals, current maintenance
    /// state and the licensed edition into a single <see cref="Models.DTOs.DashboardDto"/>.
    /// </summary>
    /// <remarks>Requires the <c>ReadOnly</c> policy.</remarks>
    /// <param name="endpoints">The route builder to register the endpoint on.</param>
    /// <returns>The same <paramref name="endpoints"/> builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/dashboard", async (
            Mail2SnmpDbContext db,
            IMaintenanceWindowService maintenanceWindowService,
            ILicenseProvider licenseProvider,
            CancellationToken ct) =>
        {
            // PF-1: the previous implementation materialized full entity graphs
            // (jobs with 4-level Includes, 500 events, 500 dead letters) on every
            // dashboard hit just to render six integers, and two counters were
            // wrong: OpenEvents was capped by GetAllAsync's Take(500) (undercounting
            // beyond 500 total events) and PendingDeadLetters counted the loaded
            // page across ALL statuses. Server-side COUNT aggregates fix both.
            // Direct DbContext use is deliberate here: read-only aggregate counters
            // with no business logic — the service layer adds only materialization.
            var activeEventStates = new[] { EventState.New, EventState.Notified, EventState.Acknowledged };

            var activeMailboxes = await db.Mailboxes.CountAsync(m => m.IsActive, ct);
            var mailboxesInError = await db.Mailboxes.CountAsync(m => m.IsActive && m.LastError != null, ct);
            var activeJobs = await db.Jobs.CountAsync(j => j.IsActive, ct);
            var activeSchedules = await db.Schedules.CountAsync(s => s.IsActive, ct);
            var openEvents = await db.Events.CountAsync(e => activeEventStates.Contains(e.State), ct);
            var pendingDeadLetters = await db.DeadLetterEntries.CountAsync(d => d.Status == DeadLetterStatus.Pending, ct);
            var inMaintenance = await maintenanceWindowService.IsInMaintenanceAsync(ct: ct);

            // Find active maintenance window name
            string? maintenanceWindowName = null;
            if (inMaintenance)
            {
                var windows = await maintenanceWindowService.GetAllAsync(ct);
                var now = DateTime.UtcNow;
                maintenanceWindowName = windows
                    .FirstOrDefault(w => w.IsActive && w.StartUtc <= now && w.EndUtc >= now)?.Name;
            }

            var dashboard = new DashboardDto
            {
                ActiveMailboxes = activeMailboxes,
                ActiveJobs = activeJobs,
                ActiveSchedules = activeSchedules,
                OpenEvents = openEvents,
                PendingDeadLetters = pendingDeadLetters,
                MaintenanceActive = inMaintenance,
                MaintenanceWindowName = maintenanceWindowName,
                // UC-1: health is computed, not asserted. A broken active mailbox means
                // ingestion (the product's core loop) is failing for its jobs.
                MailboxesInError = mailboxesInError,
                IsHealthy = mailboxesInError == 0,
                LicenseEdition = licenseProvider.Current.Edition.ToString()
            };

            return Results.Ok(dashboard);
        })
        .RequireAuthorization("ReadOnly")
        .WithTags("Dashboard")
        .WithName("GetDashboard")
        .WithOpenApi();

        return endpoints;
    }
}
