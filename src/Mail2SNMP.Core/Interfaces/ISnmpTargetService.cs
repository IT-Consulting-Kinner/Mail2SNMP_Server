using Mail2SNMP.Models.Entities;

namespace Mail2SNMP.Core.Interfaces;

/// <summary>
/// CRUD operations for SNMP trap target configurations.
/// </summary>
public interface ISnmpTargetService
{
    /// <summary>
    /// Retrieves all SNMP trap target configurations.
    /// </summary>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>A read-only list of all SNMP targets.</returns>
    Task<IReadOnlyList<SnmpTarget>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Retrieves a single SNMP target by its identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the SNMP target.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The matching SNMP target, or <c>null</c> if not found.</returns>
    Task<SnmpTarget?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Creates a new SNMP trap target configuration.
    /// </summary>
    /// <param name="target">The SNMP target entity to create.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The created SNMP target with its assigned identifier.</returns>
    Task<SnmpTarget> CreateAsync(SnmpTarget target, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing SNMP trap target configuration.
    /// </summary>
    /// <param name="target">The SNMP target entity containing updated values.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The updated SNMP target.</returns>
    Task<SnmpTarget> UpdateAsync(SnmpTarget target, CancellationToken ct = default);

    /// <summary>
    /// Deletes an SNMP trap target configuration by its identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the SNMP target to delete.</param>
    /// <param name="ct">Optional cancellation token.</param>
    Task DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Sends a test SNMP trap to the specified target to verify connectivity.
    /// </summary>
    /// <param name="id">The unique identifier of the SNMP target to test.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns><c>true</c> if the test trap was sent successfully; otherwise, <c>false</c>.</returns>
    Task<bool> TestAsync(int id, CancellationToken ct = default);
}
