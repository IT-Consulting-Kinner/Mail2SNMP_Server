using Mail2SNMP.Api.Filters;
using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Models.Entities;

namespace Mail2SNMP.Api.Endpoints;

public static class ScheduleEndpoints
{
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
