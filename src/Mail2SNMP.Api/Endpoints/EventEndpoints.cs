using System.Security.Claims;
using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Models.Enums;

namespace Mail2SNMP.Api.Endpoints;

/// <summary>
/// REST API endpoints for querying events and driving them through their lifecycle
/// (acknowledge, resolve, suppress, replay).
/// </summary>
public static class EventEndpoints
{
    /// <summary>
    /// Registers the <c>/api/v1/events</c> route group.
    /// </summary>
    /// <remarks>
    /// Maps <c>GET /</c> (list, optionally filtered by state and job) and <c>GET /{id}</c>
    /// (fetch one), both requiring the <c>ReadOnly</c> policy. State transitions
    /// <c>POST /{id}/acknowledge</c>, <c>POST /{id}/resolve</c> and <c>POST /{id}/replay</c>
    /// require the <c>Operator</c> policy (acknowledge and resolve record the calling user),
    /// while <c>POST /{id}/suppress</c> requires the <c>Admin</c> policy.
    /// </remarks>
    /// <param name="endpoints">The route builder to register the endpoints on.</param>
    /// <returns>The same <paramref name="endpoints"/> builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapEventEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/events")
            .WithTags("Events");

        group.MapGet("/", async (
            EventState? state,
            int? jobId,
            IEventService service,
            CancellationToken ct) =>
        {
            var events = await service.GetAllAsync(state, jobId, ct);
            return Results.Ok(events);
        })
        .RequireAuthorization("ReadOnly")
        .WithName("GetEvents")
        .WithOpenApi();

        group.MapGet("/{id:long}", async (long id, IEventService service, CancellationToken ct) =>
        {
            var evt = await service.GetByIdAsync(id, ct);
            return evt is not null ? Results.Ok(evt) : Results.NotFound();
        })
        .RequireAuthorization("ReadOnly")
        .WithName("GetEventById")
        .WithOpenApi();

        group.MapPost("/{id:long}/acknowledge", async (
            long id,
            ClaimsPrincipal user,
            IEventService service,
            CancellationToken ct) =>
        {
            var evt = await service.GetByIdAsync(id, ct);
            if (evt is null)
                return Results.NotFound();

            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
            await service.AcknowledgeAsync(id, userId, ct);
            return Results.Ok(new { EventId = id, NewState = nameof(EventState.Acknowledged) });
        })
        .RequireAuthorization("Operator")
        .WithName("AcknowledgeEvent")
        .WithOpenApi();

        group.MapPost("/{id:long}/resolve", async (
            long id,
            ClaimsPrincipal user,
            IEventService service,
            CancellationToken ct) =>
        {
            var evt = await service.GetByIdAsync(id, ct);
            if (evt is null)
                return Results.NotFound();

            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
            await service.ResolveAsync(id, userId, ct);
            return Results.Ok(new { EventId = id, NewState = nameof(EventState.Resolved) });
        })
        .RequireAuthorization("Operator")
        .WithName("ResolveEvent")
        .WithOpenApi();

        group.MapPost("/{id:long}/suppress", async (
            long id,
            IEventService service,
            CancellationToken ct) =>
        {
            var evt = await service.GetByIdAsync(id, ct);
            if (evt is null)
                return Results.NotFound();

            await service.SuppressAsync(id, ct);
            return Results.Ok(new { EventId = id, NewState = nameof(EventState.Suppressed) });
        })
        .RequireAuthorization("Admin")
        .WithName("SuppressEvent")
        .WithOpenApi();

        group.MapPost("/{id:long}/replay", async (
            long id,
            IEventService service,
            CancellationToken ct) =>
        {
            var evt = await service.GetByIdAsync(id, ct);
            if (evt is null)
                return Results.NotFound();

            await service.ReplayAsync(id, ct);
            return Results.Ok(new { EventId = id, Message = "Event replayed" });
        })
        .RequireAuthorization("Operator")
        .WithName("ReplayEvent")
        .WithOpenApi();

        return endpoints;
    }
}
