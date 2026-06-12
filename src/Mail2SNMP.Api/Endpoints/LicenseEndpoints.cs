using Mail2SNMP.Core.Interfaces;

namespace Mail2SNMP.Api.Endpoints;

/// <summary>
/// REST API endpoints for inspecting and reloading the current product license.
/// </summary>
public static class LicenseEndpoints
{
    /// <summary>
    /// Registers the <c>/api/v1/license</c> route group.
    /// </summary>
    /// <remarks>
    /// Maps <c>GET /</c> (return the current license details) requiring the
    /// <c>ReadOnly</c> policy, and <c>POST /reload</c> (re-read the license from disk)
    /// requiring the <c>Admin</c> policy.
    /// </remarks>
    /// <param name="endpoints">The route builder to register the endpoints on.</param>
    /// <returns>The same <paramref name="endpoints"/> builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapLicenseEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/license")
            .WithTags("License");

        group.MapGet("/", (ILicenseProvider licenseProvider) =>
        {
            return Results.Ok(licenseProvider.Current);
        })
        .RequireAuthorization("ReadOnly")
        .WithName("GetLicenseInfo")
        .WithOpenApi();

        group.MapPost("/reload", async (ILicenseProvider licenseProvider, CancellationToken ct) =>
        {
            await licenseProvider.ReloadAsync(ct);
            return Results.Ok(new { Message = "License reloaded", License = licenseProvider.Current });
        })
        .RequireAuthorization("Admin")
        .WithName("ReloadLicense")
        .WithOpenApi();

        return endpoints;
    }
}
