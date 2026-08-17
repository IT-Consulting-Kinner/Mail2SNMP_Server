using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Models.DTOs;
using Mail2SNMP.Models.Enums;

namespace Mail2SNMP.Api.Endpoints;

/// <summary>
/// REST API endpoints for managing the dead-letter queue (failed webhook and SNMP deliveries).
/// </summary>
public static class DeadLetterEndpoints
{
    /// <summary>
    /// Registers the <c>/api/v1/dead-letters</c> route group.
    /// </summary>
    /// <remarks>
    /// Maps <c>GET /</c> (filtered, paged listing) and <c>POST /{id}/retry</c> (re-queue a
    /// single entry), both requiring the <c>Operator</c> policy, plus two <c>Admin</c> bulk
    /// operations: <c>POST /retry-all</c> (re-queue everything matching a filter, either
    /// target kind) and the pre-1.2 <c>POST /retry-all/{webhookTargetId}</c>, kept so
    /// existing 1.1.0 clients and scripts do not break.
    /// <para>
    /// <c>GET /</c> accepts the optional query parameters <c>status</c>, <c>kind</c>,
    /// <c>targetId</c>, <c>skip</c> and <c>take</c>. The response body remains a bare
    /// array of entries (unchanged from 1.1.0); the number of rows matching the filter
    /// before paging is returned in the <c>X-Total-Count</c> response header.
    /// </para>
    /// </remarks>
    /// <param name="endpoints">The route builder to register the endpoints on.</param>
    /// <returns>The same <paramref name="endpoints"/> builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapDeadLetterEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/dead-letters")
            .WithTags("Dead Letters");

        // The listing used to return the newest 500 rows with no filter and no total,
        // so a burst of failures silently hid every older entry — including Abandoned
        // ones, which nothing will ever retry. Filters + a total count make the queue
        // navigable and make truncation visible.
        group.MapGet("/", async (
            HttpContext http,
            DeadLetterStatus? status,
            DeadLetterTargetKind? kind,
            int? targetId,
            int? skip,
            int? take,
            IDeadLetterService service,
            CancellationToken ct) =>
        {
            var result = await service.QueryAsync(new DeadLetterQuery
            {
                Status = status,
                Kind = kind,
                TargetId = targetId,
                Skip = skip ?? 0,
                Take = take ?? DeadLetterQuery.MaxTake
            }, ct);

            // The body stays a bare array so 1.1.0 clients keep working; the total goes
            // in a header. Wrapping it in an envelope would have been a silent breaking
            // change for every existing script.
            http.Response.Headers["X-Total-Count"] = result.TotalCount.ToString();

            // Project to the response DTO: serializing the entities directly dragged the
            // eager-loaded target navigations — and therefore their encrypted secrets —
            // into the JSON.
            return Results.Ok(result.Entries.Select(e => e.ToResponse()));
        })
        .RequireAuthorization("Operator")
        .WithName("GetDeadLetters")
        .WithOpenApi();

        group.MapPost("/{id:long}/retry", async (long id, IDeadLetterService service, CancellationToken ct) =>
        {
            await service.RetryAsync(id, ct);
            return Results.Ok(new { Message = $"Dead letter {id} queued for immediate retry." });
        })
        .RequireAuthorization("Operator")
        .WithName("RetryDeadLetter")
        .WithOpenApi();

        group.MapPost("/retry-all", async (
            DeadLetterStatus? status,
            DeadLetterTargetKind? kind,
            int? targetId,
            IDeadLetterService service,
            CancellationToken ct) =>
        {
            var count = await service.RetryAllAsync(new DeadLetterQuery
            {
                Status = status,
                Kind = kind,
                TargetId = targetId
            }, ct);
            return Results.Ok(new { Count = count, Message = $"{count} dead letter(s) queued for retry." });
        })
        .RequireAuthorization("Admin")
        .WithName("RetryAllDeadLetters")
        .WithOpenApi();

        // Compatibility route for 1.1.0 clients. Semantics are unchanged from the
        // caller's point of view: every entry of that webhook target is re-queued.
        group.MapPost("/retry-all/{webhookTargetId:int}", async (int webhookTargetId, IDeadLetterService service, CancellationToken ct) =>
        {
            var count = await service.RetryAllAsync(new DeadLetterQuery
            {
                Kind = DeadLetterTargetKind.Webhook,
                TargetId = webhookTargetId
            }, ct);
            return Results.Ok(new { Count = count, Message = $"All dead letters for webhook target {webhookTargetId} queued for retry." });
        })
        .RequireAuthorization("Admin")
        .WithName("RetryAllDeadLettersForWebhookTarget")
        .WithOpenApi();

        return endpoints;
    }
}
