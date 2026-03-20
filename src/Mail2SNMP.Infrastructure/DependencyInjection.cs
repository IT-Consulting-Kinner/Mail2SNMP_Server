using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Core.Services;
using Mail2SNMP.Infrastructure.Channels;
using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Infrastructure.Security;
using Mail2SNMP.Infrastructure.Services;
using Mail2SNMP.Models.Configuration;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Mail2SNMP.Infrastructure;

/// <summary>
/// Extension methods for registering all Mail2SNMP Infrastructure services into the DI container.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers the database context, credential encryption, license provider, and all business services.
    /// </summary>
    public static IServiceCollection AddMail2SnmpInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Database with automatic CRUD audit interceptor (v5.8)
        var dbSettings = configuration.GetSection("Database").Get<DatabaseSettings>() ?? new DatabaseSettings();
        services.AddSingleton<AuditSaveChangesInterceptor>();
        services.AddDbContext<Mail2SnmpDbContext>((sp, options) =>
        {
            if (dbSettings.Provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
                options.UseSqlServer(dbSettings.ConnectionString);
            else
                options.UseSqlite(dbSettings.ConnectionString);

            options.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
        });

        // Credential encryption with startup probe-decrypt (v5.8: fail fast on master key mismatch)
        services.AddSingleton<ICredentialEncryptor>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<AesGcmCredentialEncryptor>>();
            var keyPath = configuration["Security:MasterKeyPath"] ?? MasterKeyProvider.GetDefaultKeyPath();
            var envKey = Environment.GetEnvironmentVariable("MAIL2SNMP_MASTER_KEY");
            byte[] key;
            if (!string.IsNullOrEmpty(envKey))
                key = Convert.FromBase64String(envKey);
            else
                key = MasterKeyProvider.LoadOrCreate(keyPath, sp.GetRequiredService<ILogger<AesGcmCredentialEncryptor>>());
            var encryptor = new AesGcmCredentialEncryptor(key, logger);

            // Startup probe-decrypt: verify the master key works by encrypting and decrypting a test value.
            // This catches key corruption or misconfiguration at startup rather than at first credential use.
            var probe = encryptor.Encrypt("probe-decrypt-test");
            if (!encryptor.TryDecrypt(probe, out var result) || result != "probe-decrypt-test")
            {
                logger.LogCritical("Master key probe-decrypt FAILED. The encryption subsystem is broken. Aborting startup.");
                throw new InvalidOperationException(
                    "Master key probe-decrypt failed. Cannot start with a broken encryption subsystem. " +
                    "Check the master key file or MAIL2SNMP_MASTER_KEY environment variable.");
            }
            logger.LogInformation("Master key probe-decrypt succeeded. Encryption subsystem is operational.");
            return encryptor;
        });

        // License (v5.8: unsigned tokens only accepted in Development via DOTNET_ENVIRONMENT / ASPNETCORE_ENVIRONMENT)
        services.AddSingleton<ILicenseProvider>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<LicenseValidator>>();
            var licensePath = configuration["License:FilePath"];
            var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                      ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                      ?? "Production";
            var allowUnsigned = env.Equals("Development", StringComparison.OrdinalIgnoreCase);
            return new LicenseValidator(logger, licensePath, allowUnsigned);
        });

        // Core services
        services.AddSingleton<TemplateEngine>();
        services.AddSingleton<FloodProtectionService>();
        services.AddSingleton<NotificationDedupCache>();
        services.AddSingleton<RuleEvaluator>();

        // Business services
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IMailboxService, MailboxService>();
        services.AddScoped<IRuleService, RuleService>();
        services.AddScoped<IJobService, JobService>();
        services.AddScoped<IScheduleService, ScheduleService>();
        services.AddScoped<IEventService, EventService>();
        services.AddScoped<ISnmpTargetService, SnmpTargetService>();
        services.AddScoped<IWebhookTargetService, WebhookTargetService>();
        services.AddScoped<IMaintenanceWindowService, MaintenanceWindowService>();
        services.AddScoped<IDeadLetterService, DeadLetterService>();
        services.AddScoped<IWorkerLeaseService, WorkerLeaseService>();

        // Server-side session store for authentication cookies (keeps cookies small for OIDC scenarios)
        services.AddSingleton<ITicketStore, DbTicketStore>();

        // HTTP client factory (used by WebhookTargetService and WebhookNotificationChannel)
        services.AddHttpClient();

        // Notification channels
        services.AddScoped<INotificationChannel, SnmpNotificationChannel>();
        services.AddHttpClient<WebhookNotificationChannel>();
        services.AddScoped<INotificationChannel, WebhookNotificationChannel>();

        return services;
    }
}
