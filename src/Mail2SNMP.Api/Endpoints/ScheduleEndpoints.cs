using Mail2SNMP.Api.Filters;
using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Models.Entities;

namespace Mail2SNMP.Api.Endpoints;

/// <summary>
/// REST API endpoints for managing schedules that run jobs at fixed polling intervals.
/// </summary>
public static class ScheduleEndpoints
{
    /// <summary>
    /// Registers the <c>/api/v1/schedules</c> route group.
    /// </summary>
    /// <remarks>
    /// Maps <c>GET /</c> (list) and <c>GET /{id}</c> (fetch one), both requiring the
    /// <c>ReadOnly</c> policy, and <c>PUT /{id}/toggle</c> (enable/disable) requiring
    /// the <c>Operator</c> policy. The mutating operations <c>POST /</c> (create),
    /// <c>PUT /{id}</c> (update) and <c>DELETE /{id}</c> (delete) all require the
    /// <c>Admin</c> policy. Create and update payloads are validated by
    /// <see cref="Filters.ValidationFilter{T}"/>.
    /// </remarks>
    /// <param name="endpoints">The route builder to register the endpoints on.</param>
    /// <returns>The same <paramref name="endpoints"/> builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapScheduleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/schedules")
            .WithTags("Schedules");

        group.MapGet("/", async (IScheduleService service, CancellationToken ct) =>
        {
            var schedules = await service.GetAllAsync(ct);
            return Results.Ok(schedules);
        })
        .RequireAuthorization("ReadOnly")
        .WithName("GetSchedules")
        .WithOpenApi();

        group.MapGet("/{id:int}", async (int id, IScheduleService service, CancellationToken ct) =>
        {
            var schedule = await service.GetByIdAsync(id, ct);
            return schedule is not null ? Results.Ok(schedule) : Results.NotFound();
        })
        .RequireAuthorization("ReadOnly")
        .WithName("GetScheduleById")
        .WithOpenApi();

        group.MapPost("/", async (Schedule schedule, IScheduleService service, CancellationToken ct) =>
        {
            var created = await service.CreateAsync(schedule, ct);
            return Results.Created($"/api/v1/schedules/{created.Id}", created);
        })
        .AddEndpointFilter<ValidationFilter<Schedule>>()
        .RequireAuthorization("Admin")
        .WithName("CreateSchedule")
        .WithOpenApi();

        group.MapPut("/{id:int}", async (int id, Schedule schedule, IScheduleService service, CancellationToken ct) =>
        {
            var existing = await service.GetByIdAsync(id, ct);
            if (existing is null)
                return Results.NotFound();

            schedule.Id = id;
            var updated = await service.UpdateAsync(schedule, ct);
            return Results.Ok(updated);
        })
        .AddEndpointFilter<ValidationFilter<Schedule>>()
        .RequireAuthorization("Admin")
        .WithName("UpdateSchedule")
        .WithOpenApi();

        group.MapDelete("/{id:int}", async (int id, IScheduleService service, CancellationToken ct) =>
        {
            var existing = await service.GetByIdAsync(id, ct);
            if (existing is null)
                return Results.NotFound();

            await service.DeleteAsync(id, ct);
            return Results.NoContent();
        })
        .RequireAuthorization("Admin")
        .WithName("DeleteSchedule")
        .WithOpenApi();

        group.MapPut("/{id:int}/toggle", async (int id, IScheduleService service, CancellationToken ct) =>
        {
            var existing = await service.GetByIdAsync(id, ct);
            if (existing is null)
                return Results.NotFound();

            await service.ToggleAsync(id, ct);
            var updated = await service.GetByIdAsync(id, ct);
            return Results.Ok(updated);
        })
        .RequireAuthorization("Operator")
        .WithName("ToggleSchedule")
        .WithOpenApi();

        return endpoints;
    }
}
