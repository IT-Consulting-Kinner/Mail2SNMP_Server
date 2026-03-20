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

            var activeCount = await _db.WorkerLeases.CountAsync(ct);
            var maxInstances = _license.GetLimit("maxworkerinstances");

            if (activeCount >= maxInstances)
            {
                var existing = await _db.WorkerLeases.FirstOrDefaultAsync(ct);
                _logger.LogWarning("Another worker instance is already running (Machine: {Machine}, since {Since} UTC). " +
                    "{Edition} Edition allows {Max} instance(s).",
                    existing?.MachineName, existing?.StartedUtc, _license.Current.Edition, maxInstances);
                await transaction.RollbackAsync(ct);
                return false;
            }

            _db.WorkerLeases.Add(new WorkerLease
            {
                InstanceId = instanceId,
                LicenseEdition = _license.Current.Edition.ToString()
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
    /// </summary>
    public async Task RenewLeaseAsync(string instanceId, CancellationToken ct = default)
    {
        var lease = await _db.WorkerLeases.FirstOrDefaultAsync(w => w.InstanceId == instanceId, ct);
        if (lease != null)
        {
            lease.LastHeartbeatUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

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
