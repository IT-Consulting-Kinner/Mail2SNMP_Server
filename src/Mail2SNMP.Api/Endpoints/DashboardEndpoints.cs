using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Models.DTOs;
using Mail2SNMP.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace Mail2SNMP.Api.Endpoints;

/// <summary>
/// REST API endpoint that exposes aggregate dashboard counters for the UI home page.
/// </summary>
public static class DashboardEndpoints
{
    /// <summary>
    /// Registers <c>GET /api/v1/dashboard</c>, which aggregates active mailbox, job and
    /// schedule counts, open-event and pending dead-letter totals, current maintenance
    /// state and the licensed edition into a single <see cref="Models.DTOs.DashboardDto"/>.
    /// </summary>
    /// <remarks>Requires the <c>ReadOnly</c> policy.</remarks>
    /// <param name="endpoints">The route builder to register the endpoint on.</param>
    /// <returns>The same <paramref name="endpoints"/> builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/dashboard", async (IDashboardService dashboard, CancellationToken ct) =>
            Results.Ok(await dashboard.GetAsync(ct)))
        .RequireAuthorization("ReadOnly")
        .WithTags("Dashboard")
        .WithName("GetDashboard")
        .WithOpenApi();

        return endpoints;
    }
}
