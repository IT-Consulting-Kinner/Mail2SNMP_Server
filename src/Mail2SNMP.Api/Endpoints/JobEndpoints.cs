using Mail2SNMP.Core.Exceptions;
using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Models.DTOs;
using Mail2SNMP.Models.Enums;

namespace Mail2SNMP.Api.Endpoints;

/// <summary>
/// REST API endpoints for managing jobs, which bind a mailbox and rule to SNMP/webhook
/// targets and drive event generation.
/// </summary>
public static class JobEndpoints
{
    /// <summary>
    /// Registers the <c>/api/v1/jobs</c> route group.
    /// </summary>
    /// <remarks>
    /// Maps <c>GET /</c> (list) and <c>GET /{id}</c> (fetch one), both requiring the
    /// <c>ReadOnly</c> policy, and <c>POST /{id}/dryrun</c> (preview a job's output)
    /// requiring the <c>Operator</c> policy. The mutating operations <c>POST /</c>
    /// (create), <c>PUT /{id}</c> (update) and <c>DELETE /{id}</c> (delete) all require
    /// the <c>Admin</c> policy; create and update also persist the job's SNMP and
    /// webhook target assignments.
    /// </remarks>
    /// <param name="endpoints">The route builder to register the endpoints on.</param>
    /// <returns>The same <paramref name="endpoints"/> builder, for chaining.</returns>
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

        // UC-7 parity: Test Send existed only as a button in the management UI, so the
        // one operation that proves a job's whole delivery path works end-to-end was
        // unreachable from a deployment script or a monitoring check.
        group.MapPost("/{id:int}/test-send", async (
            int id, Severity? severity, IJobService service, CancellationToken ct) =>
        {
            var existing = await service.GetByIdAsync(id, ct);
            if (existing is null)
                return Results.NotFound();

            var result = await service.SendTestEventAsync(id, severity ?? Severity.Information, ct);
            return Results.Ok(new { JobId = id, Severity = (severity ?? Severity.Information).ToString(), Report = result });
        })
        .RequireAuthorization("Operator")
        .WithName("TestSendJob")
        .WithOpenApi();

        // UX-5 parity: bulk activate/deactivate/delete existed only in the UI. Without
        // this, "deactivate every job on the mailbox we're migrating" is a loop of
        // read-modify-write PUTs that can each clobber a concurrent edit.
        group.MapPost("/bulk", async (JobBulkRequest request, IJobService service, CancellationToken ct) =>
        {
            if (request.Ids is null || request.Ids.Count == 0)
                return Results.BadRequest(new { error = "At least one job id is required." });

            var succeeded = new List<int>();
            var failed = new List<object>();

            foreach (var id in request.Ids.Distinct())
            {
                try
                {
                    var job = await service.GetByIdAsync(id, ct);
                    if (job is null)
                    {
                        failed.Add(new { Id = id, Error = "Not found." });
                        continue;
                    }

                    if (request.Action == JobBulkAction.Delete)
                    {
                        await service.DeleteAsync(id, ct);
                    }
                    else
                    {
                        job.IsActive = request.Action == JobBulkAction.Activate;
                        await service.UpdateAsync(job, ct);
                    }
                    succeeded.Add(id);
                }
                catch (DependencyException ex)
                {
                    // A job still referenced by a schedule cannot be deleted. Report it
                    // per id rather than failing the whole batch — the caller asked for
                    // several independent operations, not a transaction.
                    failed.Add(new { Id = id, Error = ex.Message });
                }
            }

            return Results.Ok(new { Action = request.Action.ToString(), Succeeded = succeeded, Failed = failed });
        })
        .RequireAuthorization("Admin")
        .WithName("BulkUpdateJobs")
        .WithOpenApi();

        return endpoints;
    }
}
