using Mail2SNMP.Core.Interfaces;

namespace Mail2SNMP.Api.Endpoints;

/// <summary>
/// REST API endpoints for managing the dead-letter queue (failed webhook deliveries).
/// </summary>
public static class DeadLetterEndpoints
{
    /// <summary>
    /// Registers the <c>/api/v1/dead-letters</c> route group.
    /// </summary>
    /// <remarks>
    /// Maps <c>GET /</c> (list pending entries) and <c>POST /{id}/retry</c> (re-queue a
    /// single entry), both requiring the <c>Operator</c> policy, and
    /// <c>POST /retry-all/{webhookTargetId}</c> (re-queue every entry for a target)
    /// requiring the <c>Admin</c> policy.
    /// </remarks>
    /// <param name="endpoints">The route builder to register the endpoints on.</param>
    /// <returns>The same <paramref name="endpoints"/> builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapDeadLetterEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/dead-letters")
            .WithTags("Dead Letters");

        group.MapGet("/", async (IDeadLetterService service, CancellationToken ct) =>
        {
            var entries = await service.GetAllAsync(ct);
            return Results.Ok(entries);
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

        group.MapPost("/retry-all/{webhookTargetId:int}", async (int webhookTargetId, IDeadLetterService service, CancellationToken ct) =>
        {
            await service.RetryAllAsync(webhookTargetId, ct);
            return Results.Ok(new { Message = $"All dead letters for webhook target {webhookTargetId} queued for retry." });
        })
        .RequireAuthorization("Admin")
        .WithName("RetryAllDeadLetters")
        .WithOpenApi();

        return endpoints;
    }
}
