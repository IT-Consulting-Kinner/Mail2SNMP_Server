using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Mail2SNMP.Infrastructure.Services;

/// <summary>
/// Implements distributed worker leases using the database for cluster-safe single-instance enforcement.
/// </summary>
public class WorkerLeaseService : IWorkerLeaseService
{
    private readonly Mail2SnmpDbContext _db;
    private readonly ILicenseProvider _license;
    private readonly ILogger<WorkerLeaseService> _logger;
    private static readonly TimeSpan LeaseTimeout = TimeSpan.FromSeconds(90);

    public WorkerLeaseService(Mail2SnmpDbContext db, ILicenseProvider license, ILogger<WorkerLeaseService> logger)
    {
        _db = db;
        _license = license;
        _logger = logger;
    }

    /// <summary>
    /// Attempts to acquire a worker lease using a serializable transaction.
    /// Cleans up expired leases first, then checks the license-enforced instance limit.
    /// </summary>
    public async Task<bool> TryAcquireLeaseAsync(string instanceId, CancellationToken ct = default)
    {
        // Use a serializable transaction to make the count-check + insert atomic,
        // preventing the TOCTOU race where two workers both pass the limit check.
        using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        try
        {
            // Clean up expired leases first
            var cutoff = DateTime.UtcNow - LeaseTimeout;
            var expiredLeases = await _db.WorkerLeases
                .Where(w => w.LastHeartbeatUtc <= cutoff)
                .ToListAsync(ct);
            if (expiredLeases.Count > 0)
            {
                _db.WorkerLeases.RemoveRange(expiredLeases);
                await _db.SaveChangesAsync(ct);
                _logger.LogInformation("Cleaned up {Count} expired worker lease(s)", expiredLeases.Count);
            }

            // Snapshot the local license once so a concurrent ReloadAsync cannot
            // change limit/edition between checks.
            var localLicense = _license.Current;
            var maxInstances = _license.GetLimit("maxworkerinstances");

            var existingLeases = await _db.WorkerLeases.AsNoTracking().ToListAsync(ct);

            // N8: cluster license consensus. If any other instance is already
            // running with a different LicenseEdition (e.g. one container has an
            // Enterprise license file mounted, another accidentally has none),
            // refuse to join the cluster — otherwise both nodes would enforce
            // their own limits and the effective worker count could exceed
            // either license. The first node to acquire a lease sets the
            // cluster's edition; later nodes must match.
            var existingEditions = existingLeases
                .Select(l => l.LicenseEdition)
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (existingEditions.Count > 0 &&
                !existingEditions.Contains(localLicense.Edition.ToString(), StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogCritical(
                    "License edition mismatch: cluster is running {ClusterEdition} but this instance ({InstanceId}) " +
                    "is configured for {LocalEdition}. Refusing to join. Mount the same license.key on every node.",
                    string.Join(",", existingEditions), instanceId, localLicense.Edition);
                await transaction.RollbackAsync(ct);
                return false;
            }

            if (existingLeases.Count >= maxInstances)
            {
                var existing = existingLeases.FirstOrDefault();
                _logger.LogWarning("Another worker instance is already running (Machine: {Machine}, since {Since} UTC). " +
                    "{Edition} Edition allows {Max} instance(s).",
                    existing?.MachineName, existing?.StartedUtc, localLicense.Edition, maxInstances);
                await transaction.RollbackAsync(ct);
                return false;
            }

            _db.WorkerLeases.Add(new WorkerLease
            {
                InstanceId = instanceId,
                LicenseEdition = localLicense.Edition.ToString()
            });
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to acquire worker lease for {InstanceId}", instanceId);
            await transaction.RollbackAsync(ct);
            return false;
        }
    }

    /// <summary>
    /// Refreshes the heartbeat timestamp for an existing lease to prevent expiration.
    /// Returns <c>false</c> when the lease no longer exists in the database — typically
    /// because another node already expired it after a network partition. The caller
    /// (HeartbeatService) treats <c>false</c> as fatal and shuts the instance down.
    /// </summary>
    public async Task<bool> RenewLeaseAsync(string instanceId, CancellationToken ct = default)
    {
        var lease = await _db.WorkerLeases.FirstOrDefaultAsync(w => w.InstanceId == instanceId, ct);
        if (lease == null)
        {
            // N2: ghost-worker detection. Our lease was expired and removed by another
            // node while we were partitioned or hung — we are no longer part of the
            // cluster and must not continue processing.
            return false;
        }
        lease.LastHeartbeatUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// N4: Returns the lease record for the given instance, or <c>null</c> if none
    /// exists. HeartbeatService calls this immediately after a successful
    /// <see cref="TryAcquireLeaseAsync"/> to verify the row is actually persisted —
    /// defends against a connection drop after the INSERT but before the server's
    /// commit response was read.
    /// </summary>
    public async Task<WorkerLease?> GetByInstanceIdAsync(string instanceId, CancellationToken ct = default)
        => await _db.WorkerLeases.AsNoTracking().FirstOrDefaultAsync(w => w.InstanceId == instanceId, ct);

    /// <summary>
    /// Releases the lease held by the specified instance so another worker can start.
    /// </summary>
    public async Task ReleaseLeaseAsync(string instanceId, CancellationToken ct = default)
    {
        var lease = await _db.WorkerLeases.FirstOrDefaultAsync(w => w.InstanceId == instanceId, ct);
        if (lease != null)
        {
            _db.WorkerLeases.Remove(lease);
            await _db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Removes all worker leases from the database, regardless of instance or heartbeat status.
    /// </summary>
    public async Task ReleaseAllLeasesAsync(CancellationToken ct = default)
    {
        var all = await _db.WorkerLeases.ToListAsync(ct);
        _db.WorkerLeases.RemoveRange(all);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("All worker leases released ({Count})", all.Count);
    }

    /// <summary>
    /// Returns all leases whose heartbeat is still within the timeout window.
    /// </summary>
    public async Task<IReadOnlyList<WorkerLease>> GetActiveLeasesAsync(CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - LeaseTimeout;
        return await _db.WorkerLeases.AsNoTracking()
            .Where(w => w.LastHeartbeatUtc > cutoff)
            .ToListAsync(ct);
    }
}
