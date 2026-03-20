using Mail2SNMP.Api.Filters;
using Mail2SNMP.Core.Exceptions;
using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Models.Entities;

namespace Mail2SNMP.Api.Endpoints;

public static class RuleEndpoints
{
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
