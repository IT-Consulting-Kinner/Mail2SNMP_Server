using Mail2SNMP.Api.Filters;
using Mail2SNMP.Core.Exceptions;
using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Models.DTOs;
using Mail2SNMP.Models.Entities;

namespace Mail2SNMP.Api.Endpoints;

/// <summary>
/// REST API endpoints for managing SNMP trap targets that receive generated traps.
/// </summary>
public static class SnmpTargetEndpoints
{
    /// <summary>
    /// Registers the <c>/api/v1/snmp-targets</c> route group.
    /// </summary>
    /// <remarks>
    /// Maps <c>GET /</c> (list) and <c>GET /{id}</c> (fetch one), both requiring the
    /// <c>ReadOnly</c> policy, and <c>POST /{id}/test</c> (send a test trap) requiring
    /// the <c>Operator</c> policy. The mutating operations <c>POST /</c> (create),
    /// <c>PUT /{id}</c> (update) and <c>DELETE /{id}</c> (delete) all require the
    /// <c>Admin</c> policy. Create and update payloads are validated by
    /// <see cref="Filters.ValidationFilter{T}"/>.
    /// </remarks>
    /// <param name="endpoints">The route builder to register the endpoints on.</param>
    /// <returns>The same <paramref name="endpoints"/> builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapSnmpTargetEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/snmp-targets")
            .WithTags("SNMP Targets");

        group.MapGet("/", async (ISnmpTargetService service, CancellationToken ct) =>
        {
            var targets = await service.GetAllAsync(ct);
            return Results.Ok(targets.Select(t => t.ToResponse()));
        })
        .RequireAuthorization("ReadOnly")
        .WithName("GetSnmpTargets")
        .WithOpenApi();

        group.MapGet("/{id:int}", async (int id, ISnmpTargetService service, CancellationToken ct) =>
        {
            var target = await service.GetByIdAsync(id, ct);
            return target is not null ? Results.Ok(target.ToResponse()) : Results.NotFound();
        })
        .RequireAuthorization("ReadOnly")
        .WithName("GetSnmpTargetById")
        .WithOpenApi();

        group.MapPost("/", async (SnmpTarget target, ISnmpTargetService service, CancellationToken ct) =>
        {
            var created = await service.CreateAsync(target, ct);
            return Results.Created($"/api/v1/snmp-targets/{created.Id}", created.ToResponse());
        })
        .AddEndpointFilter<ValidationFilter<SnmpTarget>>()
        .RequireAuthorization("Admin")
        .WithName("CreateSnmpTarget")
        .WithOpenApi();

        group.MapPut("/{id:int}", async (int id, SnmpTarget target, ISnmpTargetService service, CancellationToken ct) =>
        {
            var existing = await service.GetByIdAsync(id, ct);
            if (existing is null)
                return Results.NotFound();

            target.Id = id;
            var updated = await service.UpdateAsync(target, ct);
            return Results.Ok(updated.ToResponse());
        })
        .AddEndpointFilter<ValidationFilter<SnmpTarget>>()
        .RequireAuthorization("Admin")
        .WithName("UpdateSnmpTarget")
        .WithOpenApi();

        group.MapDelete("/{id:int}", async (int id, ISnmpTargetService service, CancellationToken ct) =>
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
        .WithName("DeleteSnmpTarget")
        .WithOpenApi();

        group.MapPost("/{id:int}/test", async (int id, ISnmpTargetService service, CancellationToken ct) =>
        {
            var existing = await service.GetByIdAsync(id, ct);
            if (existing is null)
                return Results.NotFound();

            var success = await service.TestAsync(id, ct);
            return success
                ? Results.Ok(new { Success = true, Message = "SNMP test trap sent successfully" })
                : Results.Ok(new { Success = false, Message = "SNMP test trap failed" });
        })
        .RequireAuthorization("Operator")
        .WithName("TestSnmpTarget")
        .WithOpenApi();

        return endpoints;
    }
}
