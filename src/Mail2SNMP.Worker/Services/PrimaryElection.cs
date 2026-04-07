using Mail2SNMP.Core.Interfaces;

namespace Mail2SNMP.Worker.Services;

/// <summary>
/// Shared cluster leader-election helper. The "primary" is the active worker
/// lease with the lexicographically smallest InstanceId. Used by every worker
/// service that must run on exactly one node in a cluster (KeepAlive, IMAP IDLE,
/// UpdateCheck) so we don't reimplement the same election logic four times with
/// subtly different edge cases.
///
/// The election is intentionally simple — there is no Raft / Paxos here. The
/// guarantees we need are:
///
/// 1. <b>Eventually consistent</b>: when the primary fails, the next-smallest
///    instance takes over within one heartbeat interval (≤30s) plus the lease
///    timeout (90s).
/// 2. <b>No silent zero-primary</b>: if the active lease list does not contain
///    the calling instance (e.g. all heartbeats just expired during a DB stall),
///    the helper returns false instead of letting every node assume it is the
///    primary. This was the bug behind the N5 zero-primary transient.
/// 3. <b>Single-instance fast path</b>: when only one lease exists and it is
///    ours, we are the primary — no extra round trip.
/// </summary>
public static class PrimaryElection
{
    /// <summary>
    /// Returns <c>true</c> if the calling instance is the elected cluster primary.
    /// Returns <c>false</c> if the instance is not in the active lease list at all
    /// (transient state — caller should skip this iteration and retry next interval)
    /// or if a different instance won the election.
    /// </summary>
    public static async Task<bool> IsPrimaryAsync(
        IWorkerLeaseService leaseService,
        string instanceId,
        CancellationToken ct)
    {
        var leases = await leaseService.GetActiveLeasesAsync(ct);

        // We must be a member of the active set. If our heartbeat has not been
        // recorded yet (startup race) or has just expired (network stall), we
        // are NOT eligible to be primary — return false so the caller defers.
        var self = leases.FirstOrDefault(l =>
            string.Equals(l.InstanceId, instanceId, StringComparison.Ordinal));
        if (self == null)
            return false;

        var primary = leases
            .OrderBy(l => l.InstanceId, StringComparer.Ordinal)
            .First();
        return string.Equals(primary.InstanceId, instanceId, StringComparison.Ordinal);
    }
}
