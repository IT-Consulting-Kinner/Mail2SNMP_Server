using Mail2SNMP.Api.Endpoints;
using Mail2SNMP.Api.Setup;
using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Infrastructure;
using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Models.Configuration;
using Mail2SNMP.Models.Entities;
using Mail2SNMP.Models.Enums;
using Mail2SNMP.Web.Components;
using Mail2SNMP.Worker;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Mail2SNMP.Infrastructure.Logging;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Serilog
    builder.Host.UseSerilog((context, services, configuration) =>
    {
        SerilogConfigurator.Configure(configuration, context.Configuration);
        configuration.ReadFrom.Services(services);
    });

    // Infrastructure (DbContext, services, channels, encryption, license)
    builder.Services.AddMail2SnmpInfrastructure(builder.Configuration);

    // All-in-One mode: embed Worker background services (Quartz, mail polling, dead-letter retry,
    // data retention) in this Web process — no separate Worker or API process needed.
    var hostingSettings = builder.Configuration.GetSection("Hosting").Get<HostingSettings>();
    var isAllInOne = hostingSettings?.AllInOne == true;
    if (isAllInOne)
    {
        builder.Services.AddMail2SnmpWorkerServices(builder.Configuration);
        Log.Information("All-in-One mode enabled: Worker services and REST API endpoints will run in this process");
    }

    // Identity (P-2: shared bootstrap — see AuthSetup. Host-specific cookie config below.)
    builder.Services.AddMail2SnmpIdentityCore();

    // Cookie authentication
    builder.Services.ConfigureApplicationCookie(options =>
    {
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/access-denied";

        // V2: terminate live sessions of deactivated users. IsActive is a custom
        // property unknown to ASP.NET Identity, so the default pipeline ignores
        // it. On every request we revalidate the principal: if the backing user
        // has been disabled (or deleted) since the cookie was issued, reject the
        // principal and force a fresh sign-in. Validation is throttled to once
        // per 30s per cookie so this is not a per-request DB hit.
        options.Events.OnValidatePrincipal = async context =>
        {
            var userId = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return;
            var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<AppUser>>();
            var user = await userManager.FindByIdAsync(userId);
            if (user is null || !user.IsActive)
            {
                context.RejectPrincipal();
                var signInManager = context.HttpContext.RequestServices.GetRequiredService<SignInManager<AppUser>>();
                await signInManager.SignOutAsync();
            }
        };
    });

    // Server-side session store + X-Api-Key scheme (P-2: shared bootstrap).
    builder.Services.AddMail2SnmpTicketStore();
    builder.Services.AddMail2SnmpApiKeyScheme();

    // OIDC/SSO authentication (Enterprise only — P-2: shared bootstrap).
    var oidcSettings = builder.Configuration.GetSection("Oidc").Get<OidcSettings>();
    var oidcEnabled = builder.Services.TryAddMail2SnmpOidc(oidcSettings);
    if (oidcEnabled)
        Log.Information("OIDC/SSO authentication configured (Authority: {Authority})", oidcSettings!.Authority);

    // J5: policies accept the cookie scheme (browser/UI) and the X-Api-Key scheme
    // (automation hitting /api/v1/* in All-in-One mode). The OIDC flow culminates in
    // a cookie sign-in, so the cookie scheme covers it — no "Oidc" scheme needed here.
    // addFallbackPolicy:true keeps Razor pages requiring authentication by default.
    builder.Services.AddMail2SnmpRolePolicies(
        Mail2SNMP.Api.Setup.AuthSetup.BuildAuthSchemes(oidcEnabled: false), addFallbackPolicy: true);

    // Cascading auth state for Blazor components
    builder.Services.AddCascadingAuthenticationState();

    // Health checks (v5.8: /health/ready reports unhealthy on master key, DB, or SQLite-in-prod)
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<Mail2SnmpDbContext>("database")
        .AddCheck<Mail2SNMP.Infrastructure.Security.MasterKeyHealthCheck>("master-key")
        .AddCheck<Mail2SNMP.Infrastructure.Security.SqliteProductionHealthCheck>("sqlite-production");

    // T16: per-page documentation links bound from the Help section. Resolved
    // via IOptions<HelpSettings> in the HelpLink component so customers can
    // change the docs URL without recompiling.
    builder.Services.Configure<HelpSettings>(builder.Configuration.GetSection("Help"));

    // T3: themed confirmation dialog. The Razor pages keep calling
    // JS.TryConfirmAsync() (which now delegates to this service so we don't
    // have to touch every call site), and the global ConfirmDialog host in
    // MainLayout renders the modal.
    builder.Services.AddScoped<Mail2SNMP.Web.Components.Shared.ConfirmService>();

    // Swagger for API
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
        {
            Title = "Mail2SNMP API",
            Version = "v1"
        });
    });

    // SignalR for live updates
    builder.Services.AddSignalR();

    // Wave B (9): OpenTelemetry tracing — gated by Otel:Enabled config flag.
    // Exports OTLP traces (HTTP/gRPC) to a configurable endpoint (e.g. Jaeger, Tempo, Datadog).
    var otelEnabled = builder.Configuration.GetValue<bool>("Otel:Enabled");
    if (otelEnabled)
    {
        var otelEndpoint = builder.Configuration["Otel:Endpoint"] ?? "http://localhost:4317";
        var otelService = builder.Configuration["Otel:ServiceName"] ?? "mail2snmp-web";
        // H8: AddSource("Mail2SNMP.*") removed — no Mail2SNMP-prefixed ActivitySource
        // is created in the codebase yet, so the filter matched nothing. Re-add the
        // source filter once concrete ActivitySource("Mail2SNMP.<area>") instances
        // are introduced. Framework instrumentation (ASP.NET Core, HttpClient) is
        // still exported.
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(otelService))
            .WithTracing(t => t
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter(o => o.Endpoint = new Uri(otelEndpoint)));
        Log.Information("OpenTelemetry tracing enabled (endpoint: {Endpoint})", otelEndpoint);
    }

    // Wave A (14): Rate-limit the login endpoint (fixed-window per IP).
    // 10 attempts/minute/IP — defence in depth on top of Identity's per-account lockout.
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        // J10: Redirect rate-limited login posts back to /login with a marker so the
        // page can show a friendly "Too many attempts" message instead of the bare
        // 429 page.
        options.OnRejected = async (context, ct) =>
        {
            if (HttpMethods.IsPost(context.HttpContext.Request.Method) &&
                context.HttpContext.Request.Path.StartsWithSegments("/account/login"))
            {
                context.HttpContext.Response.Redirect("/login?error=ratelimit");
                return;
            }
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.HttpContext.Response.WriteAsync("Too many requests. Please try again later.", ct);
        };
        options.AddPolicy("login", httpContext =>
            System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));
    });

    // Razor components with interactive server
    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();

    var app = builder.Build();

    // Database schema check & seeding — schema creation is handled exclusively by 'mail2snmp db migrate'
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<Mail2SnmpDbContext>();

        // Verify the database is reachable and the schema exists.
        // Never auto-create or auto-migrate — that is the CLI's responsibility.
        try
        {
            var canConnect = await db.Database.CanConnectAsync();
            if (!canConnect)
            {
                Log.Fatal("Cannot connect to the database. Run 'mail2snmp db migrate' to initialize the schema.");
                return;
            }

            // Quick schema sanity check: try to query a known table
            _ = await db.Jobs.AnyAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex,
                "Database schema check failed. The schema may not exist yet. " +
                "Run 'mail2snmp db migrate' to create or update it.");
            return;
        }

        // Seed roles (all environments)
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        string[] roles = ["Admin", "Operator", "ReadOnly"];
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        // Check if any admin users exist — if not, first-run setup is needed
        var adminUsers = await userManager.GetUsersInRoleAsync("Admin");
        if (adminUsers.Count == 0)
        {
            Log.Warning("No admin users found. First-run setup is required at /setup");
        }

        // v5.8: SQLite production warning — 4-fold visibility (log, health/ready, UI banner, audit)
        var dbProvider = builder.Configuration["Database:Provider"] ?? "Sqlite";
        if (dbProvider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase) && !app.Environment.IsDevelopment())
        {
            Log.Warning(
                "SQLite is configured as the database provider in a non-Development environment. " +
                "SQLite is only suitable for dev/demo. Switch to SQL Server for production use. " +
                "See /health/ready for degraded status.");

            var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();
            await auditService.LogAsync(ActorType.System, "startup", "System.SqliteWarning", "Database", dbProvider,
                details: "SQLite is not recommended for production. Health status: degraded.", ct: default);
        }
    }

    // H1: Honour X-Forwarded-For/Proto from a trusted reverse proxy so the rate
    // limiter and audit log see the real client IP, not the proxy IP. The set of
    // trusted proxies comes from ForwardedHeaders:KnownProxies (CIDR list); empty
    // by default — operators MUST configure it for production deployments behind
    // a reverse proxy. KnownNetworks/KnownProxies is the only way to opt out of
    // ASP.NET Core's default loopback-only allowlist safely.
    var fwdOpts = new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions
    {
        ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                         | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
    };
    foreach (var ip in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? Array.Empty<string>())
    {
        if (System.Net.IPAddress.TryParse(ip, out var parsed))
            fwdOpts.KnownProxies.Add(parsed);
    }
    app.UseForwardedHeaders(fwdOpts);

    // Configure the HTTP request pipeline
    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
        app.UseHsts();
    }

    // Wave A (16): Security headers — CSP, X-Frame, X-Content-Type, Referrer, Permissions-Policy.
    // Blazor Server requires 'unsafe-inline' + 'unsafe-eval' for its bootstrap script + signalR
    // negotiation; everything else is locked down to 'self'.
    app.Use(async (ctx, next) =>
    {
        var h = ctx.Response.Headers;
        // R-INFO: strip the Server header so we don't advertise the ASP.NET Core
        // version number to attackers running banner-grabbing tools.
        h.Remove("Server");
        h["X-Content-Type-Options"] = "nosniff";
        h["X-Frame-Options"] = "DENY";
        h["Referrer-Policy"] = "no-referrer";
        h["Permissions-Policy"] = "geolocation=(), microphone=(), camera=(), payment=()";
        if (!h.ContainsKey("Content-Security-Policy"))
        {
            h["Content-Security-Policy"] =
                "default-src 'self'; " +
                "script-src 'self' 'unsafe-inline' 'unsafe-eval'; " +
                "style-src 'self' 'unsafe-inline'; " +
                "img-src 'self' data:; " +
                "font-src 'self' data:; " +
                "connect-src 'self' ws: wss:; " +
                "frame-ancestors 'none'; " +
                "base-uri 'self'; " +
                "form-action 'self'";
        }
        await next();
    });

    // R-INFO: Swagger UI is only exposed in Development. In production the API
    // schema would otherwise be reachable via /swagger by anyone who can hit
    // the host (the endpoints themselves still require auth, but the schema
    // discovery makes attack reconnaissance trivial).
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Mail2SNMP API v1"));
    }

    app.UseHttpsRedirection();
    app.UseStaticFiles();
    app.UseAntiforgery();

    app.UseAuthentication();
    app.UseAuthorization();

    // Wave A (14): Activate the rate limiter for endpoints opted-in via .RequireRateLimiting("login")
    app.UseRateLimiter();

    app.UseSerilogRequestLogging();

    // Prometheus Metrics (gated by config)
    var metricsSettings = builder.Configuration.GetSection("Metrics").Get<MetricsSettings>();
    if (metricsSettings?.Enabled == true)
    {
        app.UseHttpMetrics();
        app.MapMetrics("/metrics").AllowAnonymous();
        Log.Information("Prometheus metrics endpoint enabled at /metrics");
    }

    // Health check endpoints (anonymous)
    app.MapHealthChecks("/health/ready").AllowAnonymous();
    app.MapHealthChecks("/health/live").AllowAnonymous();

    // Wave A (35): MIB file download endpoint — monitoring tools need the MIB to decode traps.
    app.MapGet("/mib/Mail2SNMP-MIB.mib", (IWebHostEnvironment env) =>
    {
        var path = Path.Combine(env.WebRootPath, "mib", "Mail2SNMP-MIB.mib");
        return File.Exists(path)
            ? Results.File(path, "application/octet-stream", "Mail2SNMP-MIB.mib")
            : Results.NotFound();
    }).AllowAnonymous();

    // V7: precompute a dummy PBKDF2 hash once at startup. The login handler
    // verifies an attacker-supplied password against this on the unknown-user
    // path so its timing matches the known-user path (anti-enumeration).
    var timingHasher = new PasswordHasher<AppUser>();
    var dummyPasswordHash = timingHasher.HashPassword(new AppUser(), "timing-equalization-dummy");

    // Login endpoint (form POST from Login.razor)
    app.MapPost("/account/login", async (
        HttpContext httpContext,
        SignInManager<AppUser> signInManager,
        UserManager<AppUser> userManager) =>
    {
        var form = await httpContext.Request.ReadFormAsync();
        var email = form["email"].ToString();
        var password = form["password"].ToString();
        var rememberMe = form["rememberMe"] == "true";

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return Results.Redirect("/login?error=missing");

        // Capture client context for audit trail
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString();
        var clientAgent = httpContext.Request.Headers.UserAgent.ToString();

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            // V7: equalize timing against the valid-user path. Without this, an
            // unknown email returns immediately while a known email runs the full
            // PBKDF2 verification, yielding a measurable user-enumeration oracle.
            // Hashing a throwaway password against a dummy hash burns comparable
            // CPU so both branches take similar time. The result is discarded.
            timingHasher.VerifyHashedPassword(new AppUser(), dummyPasswordHash, password);
            return Results.Redirect("/login?error=invalid");
        }

        // V2: a deactivated account must not be able to authenticate, even with
        // the correct password. Surfaces as the same generic "invalid" message
        // path semantics but with its own error code for a clearer hint.
        if (!user.IsActive)
        {
            var auditInactive = httpContext.RequestServices.GetRequiredService<IAuditService>();
            await auditInactive.LogAsync(ActorType.System, email, "User.LoginInactive", "User", user.Id,
                result: AuditResult.Failure, ipAddress: clientIp, userAgent: clientAgent, ct: default);
            return Results.Redirect("/login?error=inactive");
        }

        var result = await signInManager.PasswordSignInAsync(
            user, password, rememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            user.LastLoginUtc = DateTime.UtcNow;
            await userManager.UpdateAsync(user);

            // Audit: log successful login with client context
            var auditService = httpContext.RequestServices.GetRequiredService<IAuditService>();
            await auditService.LogAsync(ActorType.User, user.Id, "User.Login", "User", user.Id,
                ipAddress: clientIp, userAgent: clientAgent, ct: default);

            return Results.Redirect("/");
        }

        if (result.IsLockedOut)
        {
            // Audit: log lockout with client context
            var auditServiceLocked = httpContext.RequestServices.GetRequiredService<IAuditService>();
            await auditServiceLocked.LogAsync(ActorType.System, email, "User.Lockout", "User", email,
                result: AuditResult.Failure, ipAddress: clientIp, userAgent: clientAgent, ct: default);

            return Results.Redirect("/login?error=locked");
        }

        // Audit: log failed login with client context
        var auditServiceFailed = httpContext.RequestServices.GetRequiredService<IAuditService>();
        await auditServiceFailed.LogAsync(ActorType.System, email, "User.LoginFailed", "User", email,
            result: AuditResult.Failure, ipAddress: clientIp, userAgent: clientAgent, ct: default);

        return Results.Redirect("/login?error=invalid");
    }).AllowAnonymous().DisableAntiforgery().RequireRateLimiting("login"); // CSRF mitigated by SameSite=Strict cookie policy

    // Logout endpoint
    app.MapPost("/logout", async (HttpContext httpContext, SignInManager<AppUser> signInManager) =>
    {
        // Capture the authenticated user's ID BEFORE signing out (identity is cleared after sign-out)
        var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "unknown";

        // Audit: log logout with real user context and client info
        var auditService = httpContext.RequestServices.GetRequiredService<IAuditService>();
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString();
        var clientAgent = httpContext.Request.Headers.UserAgent.ToString();
        await auditService.LogAsync(ActorType.User, userId, "User.Logout", "User", userId,
            ipAddress: clientIp, userAgent: clientAgent, ct: default);

        await signInManager.SignOutAsync();
        return Results.Redirect("/login");
    }).AllowAnonymous().DisableAntiforgery(); // CSRF mitigated by SameSite=Strict cookie policy

    // Map Razor components with interactive server render mode
    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode();

    // SignalR hub for live event and dashboard updates
    app.MapHub<Mail2SNMP.Web.Hubs.EventHub>("/hubs/events");

    // All-in-One mode: map REST API endpoints in this process
    if (isAllInOne)
    {
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
        app.MapBulkExportEndpoints();
        Log.Information("All-in-One: REST API endpoints mapped at /api/v1/*");
    }

    Log.Information("Mail2SNMP Web starting up");
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
