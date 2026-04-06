using Mail2SNMP.Api.Endpoints;
using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Infrastructure;
using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Models.Configuration;
using Mail2SNMP.Models.Entities;
using Mail2SNMP.Models.Enums;
using Mail2SNMP.Web.Components;
using Mail2SNMP.Worker;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
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

    // Identity
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
    });

    // Server-side session store: stores the full auth ticket in the DB so the cookie stays small.
    // Critical for OIDC scenarios where tokens contain many claims.
    builder.Services.AddOptions<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(
        IdentityConstants.ApplicationScheme)
        .PostConfigure<Microsoft.AspNetCore.Authentication.Cookies.ITicketStore>((options, store) =>
            options.SessionStore = store);

    // OIDC/SSO authentication (Enterprise only — requires license + Oidc config section)
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
                        // Gate OIDC behind Enterprise license at runtime
                        var license = context.HttpContext.RequestServices.GetRequiredService<ILicenseProvider>();
                        if (!license.IsEnterprise())
                        {
                            context.Fail("OIDC/SSO requires an Enterprise license.");
                            return;
                        }

                        // Map external role claims to local ASP.NET roles
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

                            // Default to ReadOnly if no role was mapped from external claims
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

        Log.Information("OIDC/SSO authentication configured (Authority: {Authority})", oidcSettings.Authority);
    }

    // Authorization policies – fallback requires authenticated user on ALL pages
    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("Admin", policy => policy.RequireRole("Admin"));
        options.AddPolicy("Operator", policy => policy.RequireRole("Admin", "Operator"));
        options.AddPolicy("ReadOnly", policy => policy.RequireRole("Admin", "Operator", "ReadOnly"));
        options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
    });

    // Cascading auth state for Blazor components
    builder.Services.AddCascadingAuthenticationState();

    // Health checks (v5.8: /health/ready reports unhealthy on master key, DB, or SQLite-in-prod)
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<Mail2SnmpDbContext>("database")
        .AddCheck<Mail2SNMP.Infrastructure.Security.MasterKeyHealthCheck>("master-key")
        .AddCheck<Mail2SNMP.Infrastructure.Security.SqliteProductionHealthCheck>("sqlite-production");

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
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(otelService))
            .WithTracing(t => t
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddSource("Mail2SNMP.*")
                .AddOtlpExporter(o => o.Endpoint = new Uri(otelEndpoint)));
        Log.Information("OpenTelemetry tracing enabled (endpoint: {Endpoint})", otelEndpoint);
    }

    // Wave A (14): Rate-limit the login endpoint (fixed-window per IP).
    // 10 attempts/minute/IP — defence in depth on top of Identity's per-account lockout.
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
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

    // Swagger (available in all environments for API consumers)
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Mail2SNMP API v1"));

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

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
            return Results.Redirect("/login?error=invalid");

        var result = await signInManager.PasswordSignInAsync(
            user, password, rememberMe, lockoutOnFailure: true);

        // Capture client context for audit trail
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString();
        var clientAgent = httpContext.Request.Headers.UserAgent.ToString();

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
