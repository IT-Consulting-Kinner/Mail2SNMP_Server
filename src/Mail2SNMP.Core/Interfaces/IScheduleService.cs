using Mail2SNMP.Models.Entities;

namespace Mail2SNMP.Core.Interfaces;

/// <summary>
/// CRUD operations for polling schedules that drive IMAP polling jobs.
/// </summary>
public interface IScheduleService
{
    /// <summary>
    /// Retrieves all polling schedules.
    /// </summary>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>A read-only list of all schedules.</returns>
    Task<IReadOnlyList<Schedule>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Retrieves a single schedule by its identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the schedule.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The matching schedule, or <c>null</c> if not found.</returns>
    Task<Schedule?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Creates a new polling schedule.
    /// </summary>
    /// <param name="schedule">The schedule entity to create.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The created schedule with its assigned identifier.</returns>
    Task<Schedule> CreateAsync(Schedule schedule, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing polling schedule.
    /// </summary>
    /// <param name="schedule">The schedule entity containing updated values.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The updated schedule.</returns>
    Task<Schedule> UpdateAsync(Schedule schedule, CancellationToken ct = default);

    /// <summary>
    /// Deletes a polling schedule by its identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the schedule to delete.</param>
    /// <param name="ct">Optional cancellation token.</param>
    Task DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Toggles the enabled state of a polling schedule.
    /// </summary>
    /// <param name="id">The unique identifier of the schedule to toggle.</param>
    /// <param name="ct">Optional cancellation token.</param>
    Task ToggleAsync(int id, CancellationToken ct = default);
}
