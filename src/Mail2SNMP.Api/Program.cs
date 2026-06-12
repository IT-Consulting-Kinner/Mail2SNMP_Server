using Mail2SNMP.Api.Endpoints;
using Mail2SNMP.Api.Setup;
using Mail2SNMP.Infrastructure;
using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Models.Configuration;
using Microsoft.EntityFrameworkCore;
using Mail2SNMP.Infrastructure.Logging;
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
    // P-2: shared bootstrap — see AuthSetup. Host-specific cookie config stays below.
    builder.Services.AddMail2SnmpIdentityCore();

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

    // ── Server-side session store + X-Api-Key scheme (P-2: shared bootstrap) ──
    // I1: the X-Api-Key scheme must be wired up here too — the API has its own DI
    // container, so registering it only in the Web project left API clients unable
    // to use API keys.
    builder.Services.AddMail2SnmpTicketStore();
    builder.Services.AddMail2SnmpApiKeyScheme();

    // ── OIDC/SSO (Enterprise only, P-2: shared bootstrap) ──────────────────
    var oidcSettings = builder.Configuration.GetSection("Oidc").Get<OidcSettings>();
    var oidcEnabled = builder.Services.TryAddMail2SnmpOidc(oidcSettings);
    if (oidcEnabled)
        Log.Information("OIDC/SSO authentication configured for API (Authority: {Authority})", oidcSettings!.Authority);

    // ── Authorization Policies ─────────────────────────────────────────────
    // I1/J6: policies must accept every scheme that is actually wired up — cookie
    // + X-Api-Key, plus "Oidc" only when the OIDC block ran. (No Blazor fallback
    // policy on the API.)
    builder.Services.AddMail2SnmpRolePolicies(
        Mail2SNMP.Api.Setup.AuthSetup.BuildAuthSchemes(oidcEnabled), addFallbackPolicy: false);

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

    // S2: strip the Server header (mirrors the Web project). Don't advertise the
    // ASP.NET Core version to attackers running banner-grabbing tools.
    app.Use(async (ctx, next) =>
    {
        ctx.Response.Headers.Remove("Server");
        await next();
    });

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
    /// <summary>
    /// Public marker partial for the top-level program entry point, exposed so that
    /// integration tests can reference it via <c>WebApplicationFactory&lt;Program&gt;</c>.
    /// </summary>
    /// <remarks>
    /// The actual host configuration lives in the top-level statements of this file;
    /// this declaration exists only to give the implicitly-generated <c>Program</c>
    /// class a public surface for the test host.
    /// </remarks>
    public partial class Program { }
}
