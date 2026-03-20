using Mail2SNMP.Core.Interfaces;

namespace Mail2SNMP.Api.Endpoints;

public static class AuditEndpoints
{
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
