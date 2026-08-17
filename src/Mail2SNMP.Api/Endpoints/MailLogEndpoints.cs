using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Models.DTOs;
using Mail2SNMP.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace Mail2SNMP.Api.Endpoints;

/// <summary>
/// REST API endpoint exposing the per-mail processing trace (UC-5).
/// </summary>
/// <remarks>
/// The Mail Log shipped as a UI-only page, so the question it exists to answer — "we
/// emailed an alert at 03:14 and got no trap, why?" — could not be asked by a monitoring
/// system, a support script, or anything other than a human with a browser. This exposes
/// the same trace, including the delivery half.
/// </remarks>
public static class MailLogEndpoints
{
    /// <summary>
    /// Registers <c>GET /api/v1/mail-log</c>, a filtered and paged view of processed mails
    /// and their outcomes.
    /// </summary>
    /// <remarks>
    /// Accepts optional <c>mailboxId</c>, <c>jobId</c>, <c>disposition</c>, <c>search</c>
    /// (sender or subject), <c>from</c>/<c>to</c> (UTC bounds on receipt time), <c>skip</c>
    /// and <c>take</c>. The number of rows matching the filter before paging is returned in
    /// the <c>X-Total-Count</c> response header. Requires the <c>ReadOnly</c> policy.
    /// </remarks>
    /// <param name="endpoints">The route builder to register the endpoint on.</param>
    /// <returns>The same <paramref name="endpoints"/> builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapMailLogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/mail-log", async (
            HttpContext http,
            int? mailboxId,
            int? jobId,
            MailDisposition? disposition,
            string? search,
            DateTime? from,
            DateTime? to,
            int? skip,
            int? take,
            Mail2SnmpDbContext db,
            CancellationToken ct) =>
        {
            const int maxTake = 500;

            var query = db.ProcessedMails.AsNoTracking().AsQueryable();

            if (mailboxId is { } mb) query = query.Where(m => m.MailboxId == mb);
            if (jobId is { } j) query = query.Where(m => m.JobId == j);
            if (disposition is { } d) query = query.Where(m => m.Disposition == d);
            if (from is { } f) query = query.Where(m => m.ReceivedUtc >= f);
            if (to is { } t) query = query.Where(m => m.ReceivedUtc <= t);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search;
                query = query.Where(m =>
                    (m.From != null && m.From.Contains(term)) ||
                    (m.Subject != null && m.Subject.Contains(term)));
            }

            var total = await query.CountAsync(ct);

            // Sorted by the same column the from/to filter uses, so both are served by
            // one index rather than range-filtering on one column and sorting on another.
            var rows = await query
                .Include(m => m.Mailbox)
                .OrderByDescending(m => m.ReceivedUtc)
                .ThenByDescending(m => m.Id)
                .Skip(Math.Max(0, skip ?? 0))
                .Take(Math.Clamp(take ?? 100, 1, maxTake))
                .ToListAsync(ct);

            // The delivery half of the trace. ProcessedMail.EventId is deliberately not a
            // foreign key — retention purges events on a different schedule than mail
            // traces — so this is a manual lookup, and a missing event honestly means
            // "already purged" rather than "no event".
            var eventIds = rows.Where(r => r.EventId.HasValue).Select(r => r.EventId!.Value).Distinct().ToList();

            var eventStates = eventIds.Count == 0
                ? new Dictionary<long, EventState>()
                : await db.Events.AsNoTracking()
                    .Where(e => eventIds.Contains(e.Id))
                    .Select(e => new { e.Id, e.State })
                    .ToDictionaryAsync(e => e.Id, e => e.State, ct);

            var deadLetters = eventIds.Count == 0
                ? new Dictionary<long, int>()
                : await db.DeadLetterEntries.AsNoTracking()
                    .Where(dl => eventIds.Contains(dl.EventId) && dl.Status != DeadLetterStatus.Locked)
                    .GroupBy(dl => dl.EventId)
                    .Select(g => new { EventId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(g => g.EventId, g => g.Count, ct);

            var items = rows.Select(m => m.ToResponse(
                eventState: m.EventId is long id && eventStates.TryGetValue(id, out var s) ? s : null,
                eventPurged: m.EventId is long pid && !eventStates.ContainsKey(pid),
                openDeadLetters: m.EventId is long did && deadLetters.TryGetValue(did, out var c) ? c : 0));

            http.Response.Headers["X-Total-Count"] = total.ToString();
            return Results.Ok(items);
        })
        .RequireAuthorization("ReadOnly")
        .WithTags("Mail Log")
        .WithName("GetMailLog")
        .WithOpenApi();

        return endpoints;
    }
}
