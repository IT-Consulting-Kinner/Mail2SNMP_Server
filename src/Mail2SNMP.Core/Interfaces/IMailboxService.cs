using Mail2SNMP.Models.Entities;

namespace Mail2SNMP.Core.Interfaces;

/// <summary>
/// CRUD operations for IMAP mailbox configurations.
/// </summary>
public interface IMailboxService
{
    /// <summary>
    /// Retrieves all mailbox configurations.
    /// </summary>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>A read-only list of all mailboxes.</returns>
    Task<IReadOnlyList<Mailbox>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Retrieves a single mailbox by its identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the mailbox.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The matching mailbox, or <c>null</c> if not found.</returns>
    Task<Mailbox?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Creates a new mailbox configuration.
    /// </summary>
    /// <param name="mailbox">The mailbox entity to create.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The created mailbox with its assigned identifier.</returns>
    Task<Mailbox> CreateAsync(Mailbox mailbox, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing mailbox configuration.
    /// </summary>
    /// <param name="mailbox">The mailbox entity containing updated values.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The updated mailbox.</returns>
    Task<Mailbox> UpdateAsync(Mailbox mailbox, CancellationToken ct = default);

    /// <summary>
    /// Deletes a mailbox configuration by its identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the mailbox to delete.</param>
    /// <param name="ct">Optional cancellation token.</param>
    Task DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Tests the IMAP connection for the specified mailbox configuration.
    /// </summary>
    /// <param name="id">The unique identifier of the mailbox to test.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns><c>true</c> if the connection succeeded; otherwise, <c>false</c>.</returns>
    Task<bool> TestConnectionAsync(int id, CancellationToken ct = default);
}
