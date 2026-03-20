using System.Security.Cryptography;
using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Serilog;

namespace Mail2SNMP.Tests.Integration;

/// <summary>
/// Custom WebApplicationFactory that replaces external dependencies with in-memory/mock alternatives
/// for fast, isolated integration testing of the REST API pipeline.
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Mail2SNMP.Api.Program>
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly string _tempKeyPath;

    public TestWebApplicationFactory()
    {
        // Create a temporary master key file so startup probe-decrypt succeeds
        _tempKeyPath = Path.Combine(Path.GetTempPath(), $"mail2snmp-test-{Guid.NewGuid()}.key");
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        File.WriteAllBytes(_tempKeyPath, key);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Reset the static Serilog logger to prevent "The logger is already frozen" errors.
        // Program.cs calls CreateBootstrapLogger() + UseSerilog() which freezes the
        // ReloadableLogger on host build. This reset ensures a clean slate.
        Log.CloseAndFlush();
        Log.Logger = new LoggerConfiguration().MinimumLevel.Warning().CreateBootstrapLogger();

        builder.UseEnvironment("Development");

        // Provide configuration before the app starts so AddMail2SnmpInfrastructure uses InMemory DB
        // and the master key file exists for the probe-decrypt
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Sqlite",
                ["Database:ConnectionString"] = $"Data Source=:memory:",
                ["Security:MasterKeyPath"] = _tempKeyPath,
            });
        });

        builder.ConfigureServices(services =>
        {
            // Replace DbContext with InMemory database (overrides the Sqlite/SqlServer from Infrastructure)
            services.RemoveAll<DbContextOptions<Mail2SnmpDbContext>>();
            services.RemoveAll<Mail2SnmpDbContext>();
            services.RemoveAll<AuditSaveChangesInterceptor>();
            services.AddSingleton<AuditSaveChangesInterceptor>();

            services.AddDbContext<Mail2SnmpDbContext>((sp, options) =>
            {
                options.UseInMemoryDatabase(_dbName);
                options.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning));
            });

            // Replace credential encryptor with test key (override the one from Infrastructure)
            services.RemoveAll<ICredentialEncryptor>();
            var testKey = File.ReadAllBytes(_tempKeyPath);
            services.AddSingleton<ICredentialEncryptor>(
                new AesGcmCredentialEncryptor(testKey, NullLogger<AesGcmCredentialEncryptor>.Instance));

            // Replace license provider with mock (generous limits for testing)
            services.RemoveAll<ILicenseProvider>();
            var license = Substitute.For<ILicenseProvider>();
            license.GetLimit("maxmailboxes").Returns(100);
            license.GetLimit("maxjobs").Returns(100);
            license.GetLimit("maxworkerinstances").Returns(10);
            license.Current.Returns(new Mail2SNMP.Models.DTOs.LicenseInfo
            {
                Edition = Mail2SNMP.Models.Enums.LicenseEdition.Community
            });
            license.IsEnterprise().Returns(false);
            services.AddSingleton(license);

            // Bypass authorization for integration tests — all requests are treated as authenticated Admin
            services.AddSingleton<IPolicyEvaluator, BypassPolicyEvaluator>();

            // NOTE: Do NOT call services.BuildServiceProvider() here — it prematurely
            // freezes the Serilog ReloadableLogger, causing "already frozen" on host build.
        });
    }

    /// <summary>
    /// Ensures the InMemory database schema is created after the host is fully built,
    /// avoiding the premature BuildServiceProvider() call in ConfigureServices.
    /// </summary>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Mail2SnmpDbContext>();
        db.Database.EnsureCreated();

        return host;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (File.Exists(_tempKeyPath))
            File.Delete(_tempKeyPath);
    }
}

/// <summary>
/// Bypasses authorization in integration tests by always returning Success for any policy.
/// All requests are treated as an authenticated Admin user.
/// </summary>
internal class BypassPolicyEvaluator : IPolicyEvaluator
{
    public Task<AuthenticateResult> AuthenticateAsync(AuthorizationPolicy policy, HttpContext context)
    {
        var identity = new System.Security.Claims.ClaimsIdentity(new[]
        {
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "integration-test"),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "Admin"),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "Operator"),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "ReadOnly"),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "test-user-id"),
        }, "IntegrationTest");

        var principal = new System.Security.Claims.ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "IntegrationTest");
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    public Task<PolicyAuthorizationResult> AuthorizeAsync(
        AuthorizationPolicy policy, AuthenticateResult authenticationResult, HttpContext context, object? resource)
    {
        return Task.FromResult(PolicyAuthorizationResult.Success());
    }
}
