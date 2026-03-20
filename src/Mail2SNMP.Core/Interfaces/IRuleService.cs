using Mail2SNMP.Models.Entities;

namespace Mail2SNMP.Core.Interfaces;

/// <summary>
/// CRUD operations for email parsing rules.
/// </summary>
public interface IRuleService
{
    /// <summary>
    /// Retrieves all email parsing rules.
    /// </summary>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>A read-only list of all rules.</returns>
    Task<IReadOnlyList<Rule>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Retrieves a single rule by its identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the rule.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The matching rule, or <c>null</c> if not found.</returns>
    Task<Rule?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Creates a new email parsing rule.
    /// </summary>
    /// <param name="rule">The rule entity to create.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The created rule with its assigned identifier.</returns>
    Task<Rule> CreateAsync(Rule rule, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing email parsing rule.
    /// </summary>
    /// <param name="rule">The rule entity containing updated values.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The updated rule.</returns>
    Task<Rule> UpdateAsync(Rule rule, CancellationToken ct = default);

    /// <summary>
    /// Deletes an email parsing rule by its identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the rule to delete.</param>
    /// <param name="ct">Optional cancellation token.</param>
    Task DeleteAsync(int id, CancellationToken ct = default);
}
