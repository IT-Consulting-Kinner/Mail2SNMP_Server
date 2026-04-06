using Mail2SNMP.Api.Endpoints;
using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Infrastructure;
using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Models.Configuration;
using Mail2SNMP.Models.Entities;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Mail2SNMP.Infrastructure.Logging;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Prometheus;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Mail2SNMP API");

    var builder = WebApplication.CreateBuilder(args);

    // ── Serilog ────────────────────────────────────────────────────────────
    builder.Host.UseSerilog((context, services, configuration) =>
    {
        SerilogConfigurator.Configure(configuration, context.Configuration);
        configuration.ReadFrom.Services(services);
    });

    // ── Infrastructure (DbContext, services, channels, etc.) ───────────────
    builder.Services.AddMail2SnmpInfrastructure(builder.Configuration);

    // ── Identity ───────────────────────────────────────────────────────────
    builder.Services.AddIdentity<AppUser, IdentityRole>(options =>
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

    // ── Cookie Authentication ──────────────────────────────────────────────
    builder.Services.ConfigureApplicationCookie(options =>
    {
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.Name = "Mail2SNMP.Auth";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.LoginPath = "/api/v1/auth/login";
        options.AccessDeniedPath = "/api/v1/auth/access-denied";
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });

    // ── Server-side session store ────────────────────────────────────────────
    // Stores the full auth ticket in the DB so the cookie stays small.
    // Critical for OIDC scenarios where tokens contain many claims.
    builder.Services.AddOptions<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(
        IdentityConstants.ApplicationScheme)
        .PostConfigure<Microsoft.AspNetCore.Authentication.Cookies.ITicketStore>((options, store) =>
            options.SessionStore = store);

    // ── OIDC/SSO (Enterprise only) ─────────────────────────────────────────
    var oidcSettings = builder.Configuration.GetSection("Oidc").Get<OidcSettings>();
    if (oidcSettings is not null && !string.IsNullOrEmpty(oidcSettings.Authority) && !string.IsNullOrEmpty(oidcSettings.ClientId))
    {
        builder.Services.AddAuthentication()
            .AddOpenIdConnect("Oidc", options =>
            {
                options.Authority = oidcSettings.Authority;
                options.ClientId = oidcSettings.ClientId;
                options.ClientSecret = oidcSettings.ClientSecret;
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.SaveTokens = true;
                options.GetClaimsFromUserInfoEndpoint = true;
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("email");
                options.TokenValidationParameters.RoleClaimType = oidcSettings.RoleClaimType;

                options.Events = new OpenIdConnectEvents
                {
                    OnTokenValidated = async context =>
                    {
                        var license = context.HttpContext.RequestServices.GetRequiredService<ILicenseProvider>();
                        if (!license.IsEnterprise())
                        {
                            context.Fail("OIDC/SSO requires an Enterprise license.");
                            return;
                        }

                        var claimsIdentity = context.Principal?.Identity as System.Security.Claims.ClaimsIdentity;
                        if (claimsIdentity is not null)
                        {
                            // Build set of claim types that carry role information:
                            // RoleClaimType ("roles") + AdditionalRoleClaimTypes ("role")
                            var roleClaimTypes = oidcSettings.AdditionalRoleClaimTypes
                                .Append(oidcSettings.RoleClaimType)
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToHashSet(StringComparer.OrdinalIgnoreCase);

                            var roleClaims = context.Principal!.Claims
                                .Where(c => roleClaimTypes.Contains(c.Type))
                                .ToList();

                            foreach (var claim in roleClaims)
                            {
                                if (claim.Value.Equals(oidcSettings.AdminClaimValue, StringComparison.OrdinalIgnoreCase))
                                    claimsIdentity.AddClaim(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "Admin"));
                                else if (claim.Value.Equals(oidcSettings.OperatorClaimValue, StringComparison.OrdinalIgnoreCase))
                                    claimsIdentity.AddClaim(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "Operator"));
                            }

                            if (!claimsIdentity.HasClaim(c => c.Type == System.Security.Claims.ClaimTypes.Role))
                                claimsIdentity.AddClaim(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "ReadOnly"));
                        }

                        // Cookie size mitigation: strip all claims except the essentials
                        if (claimsIdentity is not null)
                        {
                            var retainedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                            {
                                System.Security.Claims.ClaimTypes.Role,
                                System.Security.Claims.ClaimTypes.NameIdentifier
                            };
                            foreach (var retainedType in oidcSettings.RetainedClaimTypes)
                                retainedTypes.Add(retainedType);

                            var claimsToRemove = claimsIdentity.Claims
                                .Where(c => !retainedTypes.Contains(c.Type))
                                .ToList();
                            foreach (var claim in claimsToRemove)
                                claimsIdentity.TryRemoveClaim(claim);
                        }

                        await Task.CompletedTask;
                    }
                };
            });

        Log.Information("OIDC/SSO authentication configured for API (Authority: {Authority})", oidcSettings.Authority);
    }

    // ── Authorization Policies ─────────────────────────────────────────────
    builder.Services.AddAuthorizationBuilder()
        .AddPolicy("Admin", policy => policy.RequireRole("Admin"))
        .AddPolicy("Operator", policy => policy.RequireRole("Admin", "Operator"))
        .AddPolicy("ReadOnly", policy => policy.RequireRole("Admin", "Operator", "ReadOnly"));

    // ── CORS ───────────────────────────────────────────────────────────────
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
                          ?? ["https://localhost:5173"];
            policy.WithOrigins(origins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        });
    });

    // ── Antiforgery ────────────────────────────────────────────────────────
    builder.Services.AddAntiforgery(options =>
    {
        options.HeaderName = "X-XSRF-TOKEN";
        options.Cookie.Name = "Mail2SNMP.Antiforgery";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
    });

    // ── Swagger / OpenAPI ──────────────────────────────────────────────────
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
        {
            Title = "Mail2SNMP API",
            Version = "v1",
            Description = "REST API for the Mail2SNMP mail-to-trap gateway"
        });
    });

    // ── Health Checks ──────────────────────────────────────────────────────
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<Mail2SnmpDbContext>("database");

    var app = builder.Build();

    // ── Database schema check ─────────────────────────────────────────────
    // Schema creation is handled exclusively by 'mail2snmp db migrate'.
    // Here we only verify the database is reachable and the schema exists.
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<Mail2SnmpDbContext>();
        try
        {
            var canConnect = await db.Database.CanConnectAsync();
            if (!canConnect)
            {
                Log.Fatal("Cannot connect to the database. Run 'mail2snmp db migrate' to initialize the schema.");
                return;
            }

            // Quick schema sanity check
            _ = await db.Jobs.AnyAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex,
                "Database schema check failed. The schema may not exist yet. " +
                "Run 'mail2snmp db migrate' to create or update it.");
            return;
        }
    }

    // ── Middleware pipeline ─────────────────────────────────────────────────
    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "Mail2SNMP API v1");
        });
    }

    app.UseHttpsRedirection();
    app.UseCors();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseAntiforgery();

    // ── Prometheus Metrics (gated by config) ────────────────────────────────
    var metricsSettings = builder.Configuration.GetSection("Metrics").Get<MetricsSettings>();
    if (metricsSettings?.Enabled == true)
    {
        app.UseHttpMetrics();
        app.MapMetrics("/metrics").AllowAnonymous();
        Log.Information("Prometheus metrics endpoint enabled at /metrics");
    }

    // ── Health check endpoints ─────────────────────────────────────────────
    app.MapHealthChecks("/health/ready").AllowAnonymous();
    app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = _ => false // always healthy if the process is running
    }).AllowAnonymous();

    // ── Map all API endpoint groups ────────────────────────────────────────
    app.MapMailboxEndpoints();
    app.MapRuleEndpoints();
    app.MapJobEndpoints();
    app.MapScheduleEndpoints();
    app.MapSnmpTargetEndpoints();
    app.MapWebhookTargetEndpoints();
    app.MapEventEndpoints();
    app.MapAuditEndpoints();
    app.MapMaintenanceWindowEndpoints();
    app.MapDashboardEndpoints();
    app.MapLicenseEndpoints();
    app.MapDeadLetterEndpoints();
    app.MapWorkerEndpoints();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// Marker class for WebApplicationFactory<Program> in integration tests
namespace Mail2SNMP.Api
{
    public partial class Program { }
}
