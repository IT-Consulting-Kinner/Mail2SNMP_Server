using Mail2SNMP.Models.Entities;

namespace Mail2SNMP.Core.Interfaces;

/// <summary>
/// Manages the dead-letter queue for failed webhook deliveries (Enterprise edition only).
/// Entries are automatically retried with exponential backoff by the
/// DeadLetterRetryService background worker (in the Mail2SNMP.Worker project).
/// </summary>
public interface IDeadLetterService
{
    /// <summary>
    /// Retrieves all dead-letter entries, ordered by creation date descending.
    /// </summary>
    Task<IReadOnlyList<DeadLetterEntry>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Creates a new dead-letter entry for a failed webhook delivery.
    /// Sets the initial retry time to 15 minutes from now.
    /// </summary>
    Task<DeadLetterEntry> CreateAsync(DeadLetterEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Resets a single dead-letter entry for immediate retry.
    /// </summary>
    Task RetryAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// Resets all pending dead-letter entries for a specific webhook target for immediate retry.
    /// </summary>
    Task RetryAllAsync(int webhookTargetId, CancellationToken ct = default);
}
