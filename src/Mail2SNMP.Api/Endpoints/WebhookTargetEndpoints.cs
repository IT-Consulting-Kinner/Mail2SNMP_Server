using Mail2SNMP.Api.Filters;
using Mail2SNMP.Core.Exceptions;
using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Models.DTOs;
using Mail2SNMP.Models.Entities;

namespace Mail2SNMP.Api.Endpoints;

public static class WebhookTargetEndpoints
{
    public static IEndpointRouteBuilder MapWebhookTargetEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/webhook-targets")
            .WithTags("Webhook Targets");

        group.MapGet("/", async (IWebhookTargetService service, CancellationToken ct) =>
        {
            var targets = await service.GetAllAsync(ct);
            return Results.Ok(targets.Select(t => t.ToResponse()));
        })
        .RequireAuthorization("ReadOnly")
        .WithName("GetWebhookTargets")
        .WithOpenApi();

        group.MapGet("/{id:int}", async (int id, IWebhookTargetService service, CancellationToken ct) =>
        {
            var target = await service.GetByIdAsync(id, ct);
            return target is not null ? Results.Ok(target.ToResponse()) : Results.NotFound();
        })
        .RequireAuthorization("ReadOnly")
        .WithName("GetWebhookTargetById")
        .WithOpenApi();

        group.MapPost("/", async (WebhookTarget target, IWebhookTargetService service, CancellationToken ct) =>
        {
            var created = await service.CreateAsync(target, ct);
            return Results.Created($"/api/v1/webhook-targets/{created.Id}", created.ToResponse());
        })
        .AddEndpointFilter<ValidationFilter<WebhookTarget>>()
        .RequireAuthorization("Admin")
        .WithName("CreateWebhookTarget")
        .WithOpenApi();

        group.MapPut("/{id:int}", async (int id, WebhookTarget target, IWebhookTargetService service, CancellationToken ct) =>
        {
            var existing = await service.GetByIdAsync(id, ct);
            if (existing is null)
                return Results.NotFound();

            target.Id = id;
            var updated = await service.UpdateAsync(target, ct);
            return Results.Ok(updated.ToResponse());
        })
        .AddEndpointFilter<ValidationFilter<WebhookTarget>>()
        .RequireAuthorization("Admin")
        .WithName("UpdateWebhookTarget")
        .WithOpenApi();

        group.MapDelete("/{id:int}", async (int id, IWebhookTargetService service, CancellationToken ct) =>
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
        .WithName("DeleteWebhookTarget")
        .WithOpenApi();

        group.MapPost("/{id:int}/test", async (int id, IWebhookTargetService service, CancellationToken ct) =>
        {
            var existing = await service.GetByIdAsync(id, ct);
            if (existing is null)
                return Results.NotFound();

            var success = await service.TestAsync(id, ct);
            return success
                ? Results.Ok(new { Success = true, Message = "Webhook test request sent successfully" })
                : Results.Ok(new { Success = false, Message = "Webhook test request failed" });
        })
        .RequireAuthorization("Operator")
        .WithName("TestWebhookTarget")
        .WithOpenApi();

        return endpoints;
    }
}
