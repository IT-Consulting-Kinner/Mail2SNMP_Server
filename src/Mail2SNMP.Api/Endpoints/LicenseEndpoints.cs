using Mail2SNMP.Core.Interfaces;

namespace Mail2SNMP.Api.Endpoints;

public static class LicenseEndpoints
{
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
