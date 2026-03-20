using Mail2SNMP.Core.Exceptions;
using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Models.DTOs;

namespace Mail2SNMP.Api.Endpoints;

public static class JobEndpoints
{
    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/jobs")
            .WithTags("Jobs");

        group.MapGet("/", async (IJobService service, CancellationToken ct) =>
        {
            var jobs = await service.GetAllAsync(ct);
            return Results.Ok(jobs.Select(j => j.ToResponse()));
        })
        .RequireAuthorization("ReadOnly")
        .WithName("GetJobs")
        .WithOpenApi();

        group.MapGet("/{id:int}", async (int id, IJobService service, CancellationToken ct) =>
        {
            var job = await service.GetByIdAsync(id, ct);
            return job is not null ? Results.Ok(job.ToResponse()) : Results.NotFound();
        })
        .RequireAuthorization("ReadOnly")
        .WithName("GetJobById")
        .WithOpenApi();

        group.MapPost("/", async (JobRequest request, IJobService service, CancellationToken ct) =>
        {
            var job = request.ToEntity();
            var created = await service.CreateAsync(job, ct);
            await service.UpdateTargetAssignmentsAsync(created.Id, request.SnmpTargetIds, request.WebhookTargetIds, ct);

            // Re-load with includes for the response
            var loaded = await service.GetByIdAsync(created.Id, ct);
            return Results.Created($"/api/v1/jobs/{created.Id}", loaded!.ToResponse());
        })
        .RequireAuthorization("Admin")
        .WithName("CreateJob")
        .WithOpenApi();

        group.MapPut("/{id:int}", async (int id, JobRequest request, IJobService service, CancellationToken ct) =>
        {
            var existing = await service.GetByIdAsync(id, ct);
            if (existing is null)
                return Results.NotFound();

            // Update scalar properties
            existing.Name = request.Name;
            existing.MailboxId = request.MailboxId;
            existing.RuleId = request.RuleId;
            existing.TrapTemplate = request.TrapTemplate;
            existing.WebhookTemplate = request.WebhookTemplate;
            existing.OidMapping = request.OidMapping;
            existing.MaxEventsPerHour = request.MaxEventsPerHour;
            existing.MaxActiveEvents = request.MaxActiveEvents;
            existing.DedupWindowMinutes = request.DedupWindowMinutes;
            existing.IsActive = request.IsActive;

            await service.UpdateAsync(existing, ct);
            await service.UpdateTargetAssignmentsAsync(id, request.SnmpTargetIds, request.WebhookTargetIds, ct);

            // Re-load with includes for the response
            var loaded = await service.GetByIdAsync(id, ct);
            return Results.Ok(loaded!.ToResponse());
        })
        .RequireAuthorization("Admin")
        .WithName("UpdateJob")
        .WithOpenApi();

        group.MapDelete("/{id:int}", async (int id, IJobService service, CancellationToken ct) =>
        {
            var existing = await service.GetByIdAsync(id, ct);
            if (existing is null)
                return Results.NotFound();

            try
            {
                await service.DeleteAsync(id, ct);
                return Results.NoContent();
            }
            catch (DependencyException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        })
        .RequireAuthorization("Admin")
        .WithName("DeleteJob")
        .WithOpenApi();

        group.MapPost("/{id:int}/dryrun", async (int id, IJobService service, CancellationToken ct) =>
        {
            var existing = await service.GetByIdAsync(id, ct);
            if (existing is null)
                return Results.NotFound();

            var result = await service.DryRunAsync(id, ct);
            return Results.Ok(new { JobId = id, Output = result });
        })
        .RequireAuthorization("Operator")
        .WithName("DryRunJob")
        .WithOpenApi();

        return endpoints;
    }
}
