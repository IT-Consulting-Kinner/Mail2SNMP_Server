using Mail2SNMP.Api.Filters;
using Mail2SNMP.Core.Exceptions;
using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Models.DTOs;
using Mail2SNMP.Models.Entities;

namespace Mail2SNMP.Api.Endpoints;

public static class MailboxEndpoints
{
    public static IEndpointRouteBuilder MapMailboxEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/mailboxes")
            .WithTags("Mailboxes");

        group.MapGet("/", async (IMailboxService service, CancellationToken ct) =>
        {
            var mailboxes = await service.GetAllAsync(ct);
            return Results.Ok(mailboxes.Select(m => m.ToResponse()));
        })
        .RequireAuthorization("ReadOnly")
        .WithName("GetMailboxes")
        .WithOpenApi();

        group.MapPost("/", async (Mailbox mailbox, IMailboxService service, CancellationToken ct) =>
        {
            var created = await service.CreateAsync(mailbox, ct);
            return Results.Created($"/api/v1/mailboxes/{created.Id}", created.ToResponse());
        })
        .AddEndpointFilter<ValidationFilter<Mailbox>>()
        .RequireAuthorization("Admin")
        .WithName("CreateMailbox")
        .WithOpenApi();

        group.MapPut("/{id:int}", async (int id, Mailbox mailbox, IMailboxService service, CancellationToken ct) =>
        {
            var existing = await service.GetByIdAsync(id, ct);
            if (existing is null)
                return Results.NotFound();

            mailbox.Id = id;
            var updated = await service.UpdateAsync(mailbox, ct);
            return Results.Ok(updated.ToResponse());
        })
        .AddEndpointFilter<ValidationFilter<Mailbox>>()
        .RequireAuthorization("Admin")
        .WithName("UpdateMailbox")
        .WithOpenApi();

        group.MapDelete("/{id:int}", async (int id, IMailboxService service, CancellationToken ct) =>
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
        .WithName("DeleteMailbox")
        .WithOpenApi();

        group.MapPost("/{id:int}/test", async (int id, IMailboxService service, CancellationToken ct) =>
        {
            var existing = await service.GetByIdAsync(id, ct);
            if (existing is null)
                return Results.NotFound();

            var success = await service.TestConnectionAsync(id, ct);
            return success
                ? Results.Ok(new { Success = true, Message = "Connection successful" })
                : Results.Ok(new { Success = false, Message = "Connection failed" });
        })
        .RequireAuthorization("Operator")
        .WithName("TestMailboxConnection")
        .WithOpenApi();

        return endpoints;
    }
}
