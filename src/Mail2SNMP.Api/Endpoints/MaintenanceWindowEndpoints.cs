using Mail2SNMP.Api.Filters;
using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Models.Entities;

namespace Mail2SNMP.Api.Endpoints;

public static class MaintenanceWindowEndpoints
{
    public static IEndpointRouteBuilder MapMaintenanceWindowEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/maintenance-windows")
            .WithTags("Maintenance Windows");

        group.MapGet("/", async (IMaintenanceWindowService service, CancellationToken ct) =>
        {
            var windows = await service.GetAllAsync(ct);
            return Results.Ok(windows);
        })
        .RequireAuthorization("ReadOnly")
        .WithName("GetMaintenanceWindows")
        .WithOpenApi();

        group.MapGet("/{id:int}", async (int id, IMaintenanceWindowService service, CancellationToken ct) =>
        {
            var window = await service.GetByIdAsync(id, ct);
            return window is not null ? Results.Ok(window) : Results.NotFound();
        })
        .RequireAuthorization("ReadOnly")
        .WithName("GetMaintenanceWindowById")
        .WithOpenApi();

        group.MapPost("/", async (MaintenanceWindow window, IMaintenanceWindowService service, CancellationToken ct) =>
        {
            var created = await service.CreateAsync(window, ct);
            return Results.Created($"/api/v1/maintenance-windows/{created.Id}", created);
        })
        .AddEndpointFilter<ValidationFilter<MaintenanceWindow>>()
        .RequireAuthorization("Admin")
        .WithName("CreateMaintenanceWindow")
        .WithOpenApi();

        group.MapDelete("/{id:int}", async (int id, IMaintenanceWindowService service, CancellationToken ct) =>
        {
            var existing = await service.GetByIdAsync(id, ct);
            if (existing is null)
                return Results.NotFound();

            await service.DeleteAsync(id, ct);
            return Results.NoContent();
        })
        .RequireAuthorization("Admin")
        .WithName("DeleteMaintenanceWindow")
        .WithOpenApi();

        group.MapGet("/active", async (int? jobId, IMaintenanceWindowService service, CancellationToken ct) =>
        {
            var isActive = await service.IsInMaintenanceAsync(jobId, ct);
            return Results.Ok(new { InMaintenance = isActive });
        })
        .RequireAuthorization("ReadOnly")
        .WithName("CheckMaintenanceActive")
        .WithOpenApi();

        return endpoints;
    }
}
