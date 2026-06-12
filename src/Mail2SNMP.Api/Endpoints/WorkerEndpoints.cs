using Mail2SNMP.Core.Interfaces;

namespace Mail2SNMP.Api.Endpoints;

/// <summary>
/// REST API endpoints for monitoring and managing distributed worker instances.
/// </summary>
public static class WorkerEndpoints
{
    /// <summary>
    /// Registers the <c>/api/v1/workers</c> route group.
    /// </summary>
    /// <remarks>
    /// Maps <c>GET /</c> (list active worker leases) requiring the <c>ReadOnly</c>
    /// policy, and the lease-release operations <c>DELETE /{instanceId}</c> (release one
    /// worker's lease) and <c>DELETE /</c> (release all leases), both requiring the
    /// <c>Admin</c> policy.
    /// </remarks>
    /// <param name="endpoints">The route builder to register the endpoints on.</param>
    /// <returns>The same <paramref name="endpoints"/> builder, for chaining.</returns>
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
