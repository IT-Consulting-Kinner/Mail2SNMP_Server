using Mail2SNMP.Core.Interfaces;

namespace Mail2SNMP.Api.Endpoints;

/// <summary>
/// REST API endpoints for monitoring and managing distributed worker instances.
/// </summary>
public static class WorkerEndpoints
{
    public static IEndpointRouteBuilder MapWorkerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/workers")
            .WithTags("Workers");

        group.MapGet("/", async (IWorkerLeaseService service, CancellationToken ct) =>
        {
            var leases = await service.GetActiveLeasesAsync(ct);
            return Results.Ok(leases);
        })
        .RequireAuthorization("ReadOnly")
        .WithName("GetActiveWorkers")
        .WithOpenApi();

        group.MapDelete("/{instanceId}", async (string instanceId, IWorkerLeaseService service, CancellationToken ct) =>
        {
            await service.ReleaseLeaseAsync(instanceId, ct);
            return Results.Ok(new { Message = $"Lease for worker '{instanceId}' released." });
        })
        .RequireAuthorization("Admin")
        .WithName("ReleaseWorkerLease")
        .WithOpenApi();

        group.MapDelete("/", async (IWorkerLeaseService service, CancellationToken ct) =>
        {
            await service.ReleaseAllLeasesAsync(ct);
            return Results.Ok(new { Message = "All worker leases released." });
        })
        .RequireAuthorization("Admin")
        .WithName("ReleaseAllWorkerLeases")
        .WithOpenApi();

        return endpoints;
    }
}
