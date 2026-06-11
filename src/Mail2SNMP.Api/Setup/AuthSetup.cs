using System.Security.Claims;
using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Infrastructure.Security;
using Mail2SNMP.Models.Configuration;
using Mail2SNMP.Models.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Mail2SNMP.Api.Setup;

/// <summary>
/// Peer-review P-2: shared authentication/authorization bootstrap.
///
/// The API and Web hosts previously carried byte-for-byte copies of the Identity
/// configuration, the API-key scheme registration, the server-side ticket store
/// wiring, the (~95-line) OIDC handler including its claim-mapping logic, and the
/// role-based authorization policies. The two copies had already drifted once
/// (the API forgot to add "Oidc" to the policy scheme list — fixed in J6), which
/// is exactly the class of bug a single shared definition prevents.
///
/// Host-specific differences (cookie login paths, the Web-only
/// OnValidatePrincipal deactivated-user check, the Web-only fallback policy) are
/// intentionally left in each Program.cs — only the genuinely identical parts
/// live here.
/// </summary>
public static class AuthSetup
{
    /// <summary>
    /// Registers ASP.NET Identity with the shared password/lockout policy and the
    /// EF Core stores. Identical in both hosts.
    /// </summary>
    public static IServiceCollection AddMail2SnmpIdentityCore(this IServiceCollection services)
    {
        // The EF Core store type stays internal to this method — it is not part of
        // the public signature — so the (tolerated) EF Core 8.0.25/8.0.27 version
        // skew between the hosts does not surface as a CS1705 cross-assembly error.
        services.AddIdentity<AppUser, IdentityRole>(options =>
            {
                options.Password.RequireDigit = true;
                options.Password.RequiredLength = 12;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<Mail2SnmpDbContext>()
            .AddDefaultTokenProviders();

        return services;
    }

    /// <summary>
    /// Wires the server-side session store so the auth ticket lives in the DB and
    /// the cookie stays small (critical for OIDC tokens with many claims).
    /// </summary>
    public static IServiceCollection AddMail2SnmpTicketStore(this IServiceCollection services)
    {
        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .PostConfigure<ITicketStore>((options, store) => options.SessionStore = store);
        return services;
    }

    /// <summary>
    /// Registers the X-Api-Key authentication scheme (additive to cookie auth).
    /// </summary>
    public static IServiceCollection AddMail2SnmpApiKeyScheme(this IServiceCollection services)
    {
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationHandler.SchemeName, _ => { });
        return services;
    }

    /// <summary>
    /// Registers OIDC/SSO authentication when an <see cref="OidcSettings"/> section
    /// with Authority + ClientId is present. Returns <c>true</c> when OIDC was
    /// configured so the caller can add the "Oidc" scheme to its policies.
    /// Throws if the Authority is not an https:// URL (R3).
    /// </summary>
    public static bool TryAddMail2SnmpOidc(this IServiceCollection services, OidcSettings? oidc)
    {
        if (oidc is null || string.IsNullOrEmpty(oidc.Authority) || string.IsNullOrEmpty(oidc.ClientId))
            return false;

        // R3: refuse a plain-HTTP authority — the whole OAuth flow would be on the wire.
        if (!Uri.TryCreate(oidc.Authority, UriKind.Absolute, out var authorityUri) ||
            authorityUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                $"Oidc:Authority must be an https:// URL. Got '{oidc.Authority}'.");
        }

        services.AddAuthentication()
            .AddOpenIdConnect("Oidc", options =>
            {
                options.Authority = oidc.Authority;
                options.ClientId = oidc.ClientId;
                options.ClientSecret = oidc.ClientSecret;
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.SaveTokens = true;
                options.GetClaimsFromUserInfoEndpoint = true;
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("email");
                options.TokenValidationParameters.RoleClaimType = oidc.RoleClaimType;

                options.Events = new OpenIdConnectEvents
                {
                    OnTokenValidated = async context =>
                    {
                        // Gate OIDC behind an Enterprise license at runtime.
                        var license = context.HttpContext.RequestServices.GetRequiredService<ILicenseProvider>();
                        if (!license.IsEnterprise())
                        {
                            context.Fail("OIDC/SSO requires an Enterprise license.");
                            return;
                        }

                        ApplyOidcClaimMapping(context.Principal?.Identity as ClaimsIdentity, oidc);
                        await Task.CompletedTask;
                    }
                };
            });

        return true;
    }

    /// <summary>
    /// Maps external OIDC role claims to local Admin/Operator/ReadOnly roles, then
    /// strips every non-essential claim to keep the cookie small. Pure function of
    /// the identity + settings — shared verbatim by both hosts.
    /// </summary>
    public static void ApplyOidcClaimMapping(ClaimsIdentity? identity, OidcSettings oidc)
    {
        if (identity is null) return;

        // Claim types that carry role information: RoleClaimType + AdditionalRoleClaimTypes.
        var roleClaimTypes = oidc.AdditionalRoleClaimTypes
            .Append(oidc.RoleClaimType)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var roleClaims = identity.Claims.Where(c => roleClaimTypes.Contains(c.Type)).ToList();
        foreach (var claim in roleClaims)
        {
            if (claim.Value.Equals(oidc.AdminClaimValue, StringComparison.OrdinalIgnoreCase))
                identity.AddClaim(new Claim(ClaimTypes.Role, "Admin"));
            else if (claim.Value.Equals(oidc.OperatorClaimValue, StringComparison.OrdinalIgnoreCase))
                identity.AddClaim(new Claim(ClaimTypes.Role, "Operator"));
        }

        // Default to ReadOnly when no role mapped from external claims.
        if (!identity.HasClaim(c => c.Type == ClaimTypes.Role))
            identity.AddClaim(new Claim(ClaimTypes.Role, "ReadOnly"));

        // Cookie-size mitigation: retain only the essentials + configured extras.
        var retainedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ClaimTypes.Role,
            ClaimTypes.NameIdentifier
        };
        foreach (var retainedType in oidc.RetainedClaimTypes)
            retainedTypes.Add(retainedType);

        var claimsToRemove = identity.Claims.Where(c => !retainedTypes.Contains(c.Type)).ToList();
        foreach (var claim in claimsToRemove)
            identity.TryRemoveClaim(claim);
    }

    /// <summary>
    /// Adds the Admin / Operator / ReadOnly authorization policies over the given
    /// authentication schemes. When <paramref name="addFallbackPolicy"/> is true
    /// (Web/Blazor), an authenticated-user fallback policy is also set so Razor
    /// pages without an explicit attribute still require sign-in.
    /// </summary>
    public static IServiceCollection AddMail2SnmpRolePolicies(
        this IServiceCollection services, string[] schemes, bool addFallbackPolicy)
    {
        var builder = services.AddAuthorizationBuilder()
            .AddPolicy("Admin", policy => policy
                .AddAuthenticationSchemes(schemes)
                .RequireAuthenticatedUser()
                .RequireRole("Admin"))
            .AddPolicy("Operator", policy => policy
                .AddAuthenticationSchemes(schemes)
                .RequireAuthenticatedUser()
                .RequireRole("Admin", "Operator"))
            .AddPolicy("ReadOnly", policy => policy
                .AddAuthenticationSchemes(schemes)
                .RequireAuthenticatedUser()
                .RequireRole("Admin", "Operator", "ReadOnly"));

        if (addFallbackPolicy)
            builder.SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build());

        return services;
    }

    /// <summary>
    /// Builds the authentication-scheme list the role policies accept: cookie +
    /// X-Api-Key, plus "Oidc" when <paramref name="oidcEnabled"/> is true.
    /// </summary>
    public static string[] BuildAuthSchemes(bool oidcEnabled)
    {
        var schemes = new List<string>
        {
            IdentityConstants.ApplicationScheme,
            ApiKeyAuthenticationHandler.SchemeName
        };
        if (oidcEnabled)
            schemes.Add("Oidc");
        return schemes.ToArray();
    }
}
