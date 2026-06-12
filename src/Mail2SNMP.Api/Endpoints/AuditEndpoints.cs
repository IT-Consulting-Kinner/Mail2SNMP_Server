using Mail2SNMP.Core.Interfaces;

namespace Mail2SNMP.Api.Endpoints;

/// <summary>
/// REST API endpoints for querying the audit log of administrative actions.
/// </summary>
public static class AuditEndpoints
{
    /// <summary>
    /// Registers the <c>/api/v1/audit</c> route group.
    /// </summary>
    /// <remarks>
    /// Maps a single <c>GET /</c> that lists audit events, optionally filtered by
    /// <c>action</c> and a <c>from</c>/<c>to</c> timestamp range. Requires the
    /// <c>Admin</c> policy.
    /// </remarks>
    /// <param name="endpoints">The route builder to register the endpoints on.</param>
    /// <returns>The same <paramref name="endpoints"/> builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/audit")
            .WithTags("Audit");

        group.MapGet("/", async (
            string? action,
            DateTime? from,
            DateTime? to,
            IAuditService service,
            CancellationToken ct) =>
        {
            var events = await service.GetAllAsync(action, from, to, ct);
            return Results.Ok(events);
        })
        .RequireAuthorization("Admin")
        .WithName("GetAuditEvents")
        .WithOpenApi();

        return endpoints;
    }
}
