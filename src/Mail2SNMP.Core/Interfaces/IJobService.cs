using Mail2SNMP.Models.Entities;

namespace Mail2SNMP.Core.Interfaces;

/// <summary>
/// CRUD operations for polling jobs that link a mailbox to a rule and notification channels.
/// </summary>
public interface IJobService
{
    /// <summary>
    /// Retrieves all polling jobs.
    /// </summary>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>A read-only list of all jobs.</returns>
    Task<IReadOnlyList<Job>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Retrieves a single job by its identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the job.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The matching job, or <c>null</c> if not found.</returns>
    Task<Job?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Creates a new polling job.
    /// </summary>
    /// <param name="job">The job entity to create.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The created job with its assigned identifier.</returns>
    Task<Job> CreateAsync(Job job, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing polling job.
    /// </summary>
    /// <param name="job">The job entity containing updated values.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The updated job.</returns>
    Task<Job> UpdateAsync(Job job, CancellationToken ct = default);

    /// <summary>
    /// Deletes a polling job by its identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the job to delete.</param>
    /// <param name="ct">Optional cancellation token.</param>
    Task DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Executes a dry run of the specified job, returning a summary of what would happen without sending any notifications.
    /// </summary>
    /// <param name="id">The unique identifier of the job to dry-run.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>A human-readable summary of the dry-run results.</returns>
    Task<string> DryRunAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// UC-7: sends a synthetic test event through the REAL delivery path of this job —
    /// its templates, OID mapping and assigned SNMP/webhook targets — so an operator
    /// can prove the assembled pipeline end-to-end without waiting for a live alert.
    /// Complements <see cref="DryRunAsync"/>, which deliberately skips delivery.
    /// </summary>
    /// <param name="id">The unique identifier of the job to test.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>A human-readable per-target delivery summary.</returns>
    /// <summary>
    /// Sends a synthetic event through the job's real delivery path and reports the
    /// per-target outcome.
    /// </summary>
    /// <param name="id">The job to test.</param>
    /// <param name="severity">
    /// Severity to stamp on the synthetic event. Test Send was fixed at
    /// <see cref="Models.Enums.Severity.Information"/>, which made it useless for verifying
    /// the very feature most likely to be misconfigured: a target set to "Critical only"
    /// could only ever be reported as skipped, so an operator could not confirm that
    /// severity routing reaches the right target.
    /// </param>
    /// <param name="ct">Token used to cancel the operation.</param>
    /// <returns>A human-readable per-target report.</returns>
    Task<string> SendTestEventAsync(int id, Models.Enums.Severity severity = Models.Enums.Severity.Information, CancellationToken ct = default);

    /// <summary>
    /// Updates the SNMP and Webhook target assignments for a job, replacing existing entries.
    /// </summary>
    /// <param name="jobId">The unique identifier of the job.</param>
    /// <param name="snmpTargetIds">The IDs of SNMP targets to assign.</param>
    /// <param name="webhookTargetIds">The IDs of Webhook targets to assign.</param>
    /// <param name="ct">Optional cancellation token.</param>
    Task UpdateTargetAssignmentsAsync(int jobId, IEnumerable<int> snmpTargetIds, IEnumerable<int> webhookTargetIds, CancellationToken ct = default);
}
