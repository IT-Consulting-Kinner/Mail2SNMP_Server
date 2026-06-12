using Mail2SNMP.Api.Filters;
using Mail2SNMP.Core.Exceptions;
using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Models.Entities;

namespace Mail2SNMP.Api.Endpoints;

/// <summary>
/// REST API endpoints for managing match rules that classify incoming mail into events.
/// </summary>
public static class RuleEndpoints
{
    /// <summary>
    /// Registers the <c>/api/v1/rules</c> route group.
    /// </summary>
    /// <remarks>
    /// Maps <c>GET /</c> (list) and <c>GET /{id}</c> (fetch one), both requiring the
    /// <c>ReadOnly</c> policy. The mutating operations <c>POST /</c> (create),
    /// <c>PUT /{id}</c> (update) and <c>DELETE /{id}</c> (delete) all require the
    /// <c>Admin</c> policy. Create and update payloads are validated by
    /// <see cref="Filters.ValidationFilter{T}"/>.
    /// </remarks>
    /// <param name="endpoints">The route builder to register the endpoints on.</param>
    /// <returns>The same <paramref name="endpoints"/> builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapRuleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/rules")
            .WithTags("Rules");

        group.MapGet("/", async (IRuleService service, CancellationToken ct) =>
        {
            var rules = await service.GetAllAsync(ct);
            return Results.Ok(rules);
        })
        .RequireAuthorization("ReadOnly")
        .WithName("GetRules")
        .WithOpenApi();

        group.MapGet("/{id:int}", async (int id, IRuleService service, CancellationToken ct) =>
        {
            var rule = await service.GetByIdAsync(id, ct);
            return rule is not null ? Results.Ok(rule) : Results.NotFound();
        })
        .RequireAuthorization("ReadOnly")
        .WithName("GetRuleById")
        .WithOpenApi();

        group.MapPost("/", async (Rule rule, IRuleService service, CancellationToken ct) =>
        {
            var created = await service.CreateAsync(rule, ct);
            return Results.Created($"/api/v1/rules/{created.Id}", created);
        })
        .AddEndpointFilter<ValidationFilter<Rule>>()
        .RequireAuthorization("Admin")
        .WithName("CreateRule")
        .WithOpenApi();

        group.MapPut("/{id:int}", async (int id, Rule rule, IRuleService service, CancellationToken ct) =>
        {
            var existing = await service.GetByIdAsync(id, ct);
            if (existing is null)
                return Results.NotFound();

            rule.Id = id;
            var updated = await service.UpdateAsync(rule, ct);
            return Results.Ok(updated);
        })
        .AddEndpointFilter<ValidationFilter<Rule>>()
        .RequireAuthorization("Admin")
        .WithName("UpdateRule")
        .WithOpenApi();

        group.MapDelete("/{id:int}", async (int id, IRuleService service, CancellationToken ct) =>
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
        .WithName("DeleteRule")
        .WithOpenApi();

        return endpoints;
    }
}
