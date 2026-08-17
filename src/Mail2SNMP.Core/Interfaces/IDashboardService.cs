using Mail2SNMP.Models.DTOs;

namespace Mail2SNMP.Core.Interfaces;

/// <summary>
/// Computes the aggregate counters shown on the dashboard and returned by
/// <c>GET /api/v1/dashboard</c>.
/// </summary>
/// <remarks>
/// The same aggregation was implemented twice — once in the API endpoint and once in the
/// Blazor home page — with the same counters, the same active-state set and the same
/// health rule copied between them. Two copies of "what does healthy mean" is one copy too
/// many: a correction applied to one silently leaves the other reporting something else,
/// and the two had already drifted once (the API surfaced the active maintenance window's
/// name, the UI did not).
/// </remarks>
public interface IDashboardService
{
    /// <summary>
    /// Returns the current dashboard snapshot: active mailbox/job/schedule counts, open
    /// events, pending dead letters, maintenance state and computed health.
    /// </summary>
    /// <param name="ct">Token used to cancel the aggregation.</param>
    Task<DashboardDto> GetAsync(CancellationToken ct = default);
}
