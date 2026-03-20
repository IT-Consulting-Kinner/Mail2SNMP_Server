using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Models.DTOs;
using Mail2SNMP.Models.Enums;

namespace Mail2SNMP.Api.Endpoints;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/dashboard", async (
            IMailboxService mailboxService,
            IJobService jobService,
            IScheduleService scheduleService,
            IEventService eventService,
            IDeadLetterService deadLetterService,
            IMaintenanceWindowService maintenanceWindowService,
            ILicenseProvider licenseProvider,
            CancellationToken ct) =>
        {
            var mailboxes = await mailboxService.GetAllAsync(ct);
            var jobs = await jobService.GetAllAsync(ct);
            var schedules = await scheduleService.GetAllAsync(ct);
            var openEvents = await eventService.GetAllAsync(stateFilter: null, jobId: null, ct: ct);
            var deadLetters = await deadLetterService.GetAllAsync(ct);
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
                ActiveMailboxes = mailboxes.Count(m => m.IsActive),
                ActiveJobs = jobs.Count(j => j.IsActive),
                ActiveSchedules = schedules.Count(s => s.IsActive),
                OpenEvents = openEvents.Count(e =>
                    e.State == EventState.New ||
                    e.State == EventState.Notified ||
                    e.State == EventState.Acknowledged),
                PendingDeadLetters = deadLetters.Count,
                MaintenanceActive = inMaintenance,
                MaintenanceWindowName = maintenanceWindowName,
                IsHealthy = true,
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
