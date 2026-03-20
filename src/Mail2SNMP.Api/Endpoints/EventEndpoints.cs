using System.Security.Claims;
using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Models.Enums;

namespace Mail2SNMP.Api.Endpoints;

public static class EventEndpoints
{
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
