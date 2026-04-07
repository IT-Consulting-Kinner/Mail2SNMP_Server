using Mail2SNMP.Models.Entities;

namespace Mail2SNMP.Core.Interfaces;

/// <summary>
/// Manages distributed worker leases for single-instance enforcement (Community) or cluster coordination (SQL Server).
/// </summary>
public interface IWorkerLeaseService
{
    /// <summary>
    /// Attempts to acquire a lease for the specified worker instance.
    /// </summary>
    /// <param name="instanceId">The unique identifier of the worker instance requesting the lease.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns><c>true</c> if the lease was successfully acquired; otherwise, <c>false</c>.</returns>
    Task<bool> TryAcquireLeaseAsync(string instanceId, CancellationToken ct = default);

    /// <summary>
    /// Renews an existing lease for the specified worker instance, extending its expiration.
    /// Returns <c>false</c> when the lease is no longer present in the database — typically
    /// because another node already expired it (e.g. after a network partition). Callers
    /// MUST treat <c>false</c> as a fatal cluster-state error and shut the instance down,
    /// otherwise it would continue running as a "ghost worker" outside the cluster's view.
    /// </summary>
    Task<bool> RenewLeaseAsync(string instanceId, CancellationToken ct = default);

    /// <summary>
    /// Returns the lease record for the given instance, or <c>null</c> if none exists.
    /// Used by HeartbeatService (in the Mail2SNMP.Worker project) to verify a freshly
    /// acquired lease was actually persisted — defends against a connection drop
    /// after INSERT but before the server's response was read.
    /// </summary>
    Task<WorkerLease?> GetByInstanceIdAsync(string instanceId, CancellationToken ct = default);

    /// <summary>
    /// Releases the lease held by the specified worker instance.
    /// </summary>
    /// <param name="instanceId">The unique identifier of the worker instance whose lease should be released.</param>
    /// <param name="ct">Optional cancellation token.</param>
    Task ReleaseLeaseAsync(string instanceId, CancellationToken ct = default);

    /// <summary>
    /// Releases all active worker leases. Typically used during shutdown or administrative cleanup.
    /// </summary>
    /// <param name="ct">Optional cancellation token.</param>
    Task ReleaseAllLeasesAsync(CancellationToken ct = default);

    /// <summary>
    /// Retrieves all currently active worker leases.
    /// </summary>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>A read-only list of active worker leases.</returns>
    Task<IReadOnlyList<WorkerLease>> GetActiveLeasesAsync(CancellationToken ct = default);
}
