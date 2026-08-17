using Mail2SNMP.Models.DTOs;
using Mail2SNMP.Models.Entities;

namespace Mail2SNMP.Core.Interfaces;

/// <summary>
/// Manages the dead-letter queue for failed notification deliveries — webhook POSTs and,
/// since UC-3, SNMP traps. Entries are automatically retried with exponential backoff by
/// the DeadLetterRetryService background worker (in the Mail2SNMP.Worker project), which
/// runs under the Enterprise edition only; Community records entries for inspection.
/// </summary>
/// <remarks>
/// The queue holds both target kinds, so nothing on this interface is phrased in webhook
/// terms: callers filter and bulk-retry through <see cref="DeadLetterQuery"/>, which
/// matches either kind. Previously the only bulk operation took a <c>webhookTargetId</c>,
/// which forced the UI to issue one call per SNMP row — an N+1 that also applied
/// different semantics to the two kinds.
/// </remarks>
public interface IDeadLetterService
{
    /// <summary>
    /// Returns one page of dead-letter entries matching <paramref name="query"/>, newest first,
    /// together with the total number of matching rows.
    /// </summary>
    /// <param name="query">Filter and paging criteria. Pass a default instance for the newest page of everything.</param>
    /// <param name="ct">Token used to cancel the query.</param>
    Task<DeadLetterQueryResult> QueryAsync(DeadLetterQuery query, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the newest dead-letter entries, ordered by creation date descending.
    /// </summary>
    /// <remarks>
    /// Convenience wrapper over <see cref="QueryAsync"/> for callers that genuinely want an
    /// unfiltered snapshot (CLI listing, tests). The result is capped at
    /// <see cref="DeadLetterQuery.MaxTake"/> rows — prefer <see cref="QueryAsync"/> anywhere
    /// the caller needs to know whether it is seeing the whole queue.
    /// </remarks>
    Task<IReadOnlyList<DeadLetterEntry>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Creates a new dead-letter entry for a failed delivery (webhook or SNMP).
    /// Sets the initial retry time to 15 minutes from now.
    /// </summary>
    Task<DeadLetterEntry> CreateAsync(DeadLetterEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Resets a single dead-letter entry for immediate retry, clearing its lock, status,
    /// attempt counter and last error.
    /// </summary>
    Task RetryAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// Resets every entry matching <paramref name="filter"/> for immediate retry, regardless
    /// of target kind, and returns how many rows were re-queued.
    /// </summary>
    /// <param name="filter">
    /// Which entries to re-queue. <c>Skip</c>/<c>Take</c> are ignored — a bulk retry acts on
    /// the whole matching set, not on the page the operator happens to be viewing.
    /// </param>
    /// <param name="ct">Token used to cancel the operation.</param>
    /// <returns>The number of entries re-queued.</returns>
    Task<int> RetryAllAsync(DeadLetterQuery filter, CancellationToken ct = default);
}
