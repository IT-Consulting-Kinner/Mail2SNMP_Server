using Mail2SNMP.Models.Entities;

namespace Mail2SNMP.Core.Interfaces;

/// <summary>
/// Manages maintenance windows that suppress alert generation during planned downtime.
/// </summary>
public interface IMaintenanceWindowService
{
    /// <summary>
    /// Retrieves all maintenance windows.
    /// </summary>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>A read-only list of all maintenance windows.</returns>
    Task<IReadOnlyList<MaintenanceWindow>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Retrieves a single maintenance window by its identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the maintenance window.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The matching maintenance window, or <c>null</c> if not found.</returns>
    Task<MaintenanceWindow?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Creates a new maintenance window.
    /// </summary>
    /// <param name="window">The maintenance window entity to create.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The created maintenance window with its assigned identifier.</returns>
    Task<MaintenanceWindow> CreateAsync(MaintenanceWindow window, CancellationToken ct = default);

    /// <summary>
    /// Deletes a maintenance window by its identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the maintenance window to delete.</param>
    /// <param name="ct">Optional cancellation token.</param>
    Task DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Determines whether the system (or a specific job) is currently within an active maintenance window.
    /// </summary>
    /// <param name="jobId">Optional job identifier to check for a job-specific maintenance window. When <c>null</c>, checks for global maintenance.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns><c>true</c> if an active maintenance window applies; otherwise, <c>false</c>.</returns>
    Task<bool> IsInMaintenanceAsync(int? jobId = null, CancellationToken ct = default);
}
