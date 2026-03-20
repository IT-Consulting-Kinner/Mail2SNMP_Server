using Mail2SNMP.Models.Entities;

namespace Mail2SNMP.Core.Interfaces;

/// <summary>
/// CRUD operations for webhook target configurations.
/// </summary>
public interface IWebhookTargetService
{
    /// <summary>
    /// Retrieves all webhook target configurations.
    /// </summary>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>A read-only list of all webhook targets.</returns>
    Task<IReadOnlyList<WebhookTarget>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Retrieves a single webhook target by its identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the webhook target.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The matching webhook target, or <c>null</c> if not found.</returns>
    Task<WebhookTarget?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Creates a new webhook target configuration.
    /// </summary>
    /// <param name="target">The webhook target entity to create.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The created webhook target with its assigned identifier.</returns>
    Task<WebhookTarget> CreateAsync(WebhookTarget target, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing webhook target configuration.
    /// </summary>
    /// <param name="target">The webhook target entity containing updated values.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The updated webhook target.</returns>
    Task<WebhookTarget> UpdateAsync(WebhookTarget target, CancellationToken ct = default);

    /// <summary>
    /// Deletes a webhook target configuration by its identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the webhook target to delete.</param>
    /// <param name="ct">Optional cancellation token.</param>
    Task DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Sends a test payload to the specified webhook target to verify connectivity.
    /// </summary>
    /// <param name="id">The unique identifier of the webhook target to test.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns><c>true</c> if the test request succeeded; otherwise, <c>false</c>.</returns>
    Task<bool> TestAsync(int id, CancellationToken ct = default);
}
