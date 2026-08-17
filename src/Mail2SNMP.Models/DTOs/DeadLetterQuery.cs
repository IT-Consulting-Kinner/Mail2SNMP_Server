using Mail2SNMP.Models.Enums;

namespace Mail2SNMP.Models.DTOs;

/// <summary>
/// Filter and paging criteria for the dead-letter queue.
/// </summary>
/// <remarks>
/// The queue listing previously had no filter at all and was hard-capped at the newest
/// 500 rows. On a deployment with a burst of failures that silently hid every older
/// entry — including the <see cref="DeadLetterStatus.Abandoned"/> ones an operator most
/// needs to see, since nothing will ever retry those. The same criteria object drives
/// both the listing and the bulk retry, so "Retry all" always acts on exactly the set
/// the operator is looking at.
/// </remarks>
public sealed class DeadLetterQuery
{
    /// <summary>Restricts the result to a single status. <c>null</c> returns every status.</summary>
    public DeadLetterStatus? Status { get; set; }

    /// <summary>Restricts the result to webhook or SNMP entries. <c>null</c> returns both kinds.</summary>
    public DeadLetterTargetKind? Kind { get; set; }

    /// <summary>
    /// Restricts the result to one target. Interpreted against <see cref="Kind"/>: a
    /// <see cref="DeadLetterTargetKind.Snmp"/> query matches <c>SnmpTargetId</c>, otherwise
    /// <c>WebhookTargetId</c>. Ignored when <c>null</c>.
    /// </summary>
    public int? TargetId { get; set; }

    /// <summary>Number of matching rows to skip (server-side paging). Negative values are clamped to 0.</summary>
    public int Skip { get; set; }

    /// <summary>
    /// Maximum number of rows to return. Clamped to <see cref="MaxTake"/> so a client cannot
    /// ask the server to materialize an unbounded queue.
    /// </summary>
    public int Take { get; set; } = 50;

    /// <summary>Upper bound enforced on <see cref="Take"/> by the service.</summary>
    public const int MaxTake = 500;
}

/// <summary>
/// One page of dead-letter entries plus the total number of rows matching the filter.
/// </summary>
/// <remarks>
/// <see cref="TotalCount"/> is the count *before* paging, so the UI can render honest
/// pagination and say "showing 1–50 of 12 480" instead of silently presenting a
/// truncated list as if it were the whole queue.
/// </remarks>
public sealed class DeadLetterQueryResult
{
    /// <summary>The requested page, newest first.</summary>
    public IReadOnlyList<Entities.DeadLetterEntry> Entries { get; init; } = Array.Empty<Entities.DeadLetterEntry>();

    /// <summary>Total number of rows matching the filter, ignoring <c>Skip</c>/<c>Take</c>.</summary>
    public int TotalCount { get; init; }
}
