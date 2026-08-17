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
    /// Name of the <see cref="Microsoft.AspNetCore.Authentication.AuthenticationProperties"/>
    /// item stamped at sign-in with the session's true start time. Sliding renewal
    /// rewrites <c>IssuedUtc</c> on every refresh, so <c>IssuedUtc</c> alone can never
    /// trip an absolute-expiry check on an actively-used session; custom property
    /// items survive renewal untouched.
    /// </summary>
    private const string SessionStartKey = "m2s.session_start";

    /// <summary>
    /// SEC-3 / V2: rejects the cookie principal when the backing user has been
    /// deleted or deactivated (<c>IsActive == false</c>) since the cookie was
    /// issued, forcing a fresh sign-in. Attach from every host's
    /// <c>ConfigureApplicationCookie</c> so a deactivated user cannot keep using
    /// a live session against either the Web UI or the standalone API.
    /// </summary>
    /// <remarks>
    /// ASP.NET Identity's default pipeline knows nothing about the custom
    /// <c>IsActive</c> flag, so without this event a disabled account keeps its
    /// sliding-expiration cookie alive indefinitely. Cookie validation runs on
    /// every request; the <c>UserManager</c> lookup is served from the request
    /// scope, so the cost is one indexed PK query.
    /// </remarks>
    /// <param name="options">The cookie options to attach the validation events to.</param>
    /// <param name="absoluteExpiry">
    /// Optional hard ceiling measured from sign-in. When exceeded the principal is
    /// rejected regardless of activity.
    /// </param>
    public static void AttachDeactivatedUserRejection(CookieAuthenticationOptions options, TimeSpan? absoluteExpiry = null)
    {
        // AR-5 (verified fix): stamp the immutable session start at sign-in.
        options.Events.OnSigningIn = context =>
        {
            context.Properties.SetString(SessionStartKey, DateTimeOffset.UtcNow.ToString("O"));
            return Task.CompletedTask;
        };

        options.Events.OnValidatePrincipal = async context =>
        {
            // AR-5: absolute session lifetime. Sliding expiration alone lets an
            // actively-used session live forever. The check uses the sign-in
            // timestamp stamped above (renewal-immune); IssuedUtc is only the
            // fallback for tickets issued before this property existed.
            if (absoluteExpiry is TimeSpan max)
            {
                DateTimeOffset? start = null;
                var stamped = context.Properties.GetString(SessionStartKey);
                if (stamped is not null &&
                    DateTimeOffset.TryParse(stamped, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
                    start = parsed;
                start ??= context.Properties.IssuedUtc;

                if (start is DateTimeOffset issued && DateTimeOffset.UtcNow - issued > max)
                {
                    context.RejectPrincipal();
                    return;
                }
            }

            var userId = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<AppUser>>();
                var user = await userManager.FindByIdAsync(userId);
                if (user is null || !user.IsActive)
                {
                    context.RejectPrincipal();
                    var signInManager = context.HttpContext.RequestServices.GetRequiredService<SignInManager<AppUser>>();
                    await signInManager.SignOutAsync();
                    return;
                }
            }

            // Verified fix: assigning OnValidatePrincipal replaces the delegate that
            // AddIdentity installed — SecurityStampValidator.ValidatePrincipalAsync —
            // which is what invalidates existing sessions after a password change.
            // Chain it explicitly so stamp validation still runs in addition to the
            // checks above (this also closes a pre-existing gap on the Web host).
            await SecurityStampValidator.ValidatePrincipalAsync(context);
        };
    }

    /// <summary>
    /// AR-3: makes cookie auth-failure semantics on <c>/api/*</c> host-independent.
    /// An unauthenticated or unauthorized API request receives a machine-readable
    /// <c>401</c>/<c>403</c> instead of a <c>302</c> redirect to the HTML login
    /// page; browser requests to non-API paths keep the normal redirect flow.
    /// </summary>
    /// <remarks>
    /// Previously the standalone API host returned 401/403 while the Web host
    /// (All-in-One mode) redirected the very same <c>/api/v1</c> request to
    /// <c>/login</c> — an identical client behaved differently depending on a
    /// deployment flag ("works in staging, breaks in prod").
    /// </remarks>
    public static void AttachApiStatusCodeRedirects(CookieAuthenticationOptions options)
    {
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            else
                context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
            else
                context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
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
