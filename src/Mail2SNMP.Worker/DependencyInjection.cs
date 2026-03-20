using Mail2SNMP.Models.Configuration;
using Mail2SNMP.Worker.Models;
using Mail2SNMP.Worker.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using System.Threading.Channels;

namespace Mail2SNMP.Worker;

/// <summary>
/// Extension methods for registering all Mail2SNMP Worker services into the DI container.
/// Used by both the standalone Worker host and the All-in-One Web host.
/// </summary>
public static class WorkerDependencyInjection
{
    /// <summary>
    /// Registers the Quartz scheduler, bounded channel, and all background hosted services
    /// required for mail polling, dead-letter retry, data retention, and worker lease management.
    /// </summary>
    public static IServiceCollection AddMail2SnmpWorkerServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Bounded channel shared between Quartz jobs (producers) and MailPollingService (consumers)
        var imapSettings = configuration
            .GetSection("Imap")
            .Get<ImapSettings>() ?? new ImapSettings();

        services.AddSingleton(Channel.CreateBounded<MailWorkItem>(
            new BoundedChannelOptions(imapSettings.ChannelBoundedCapacity)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = false,
                SingleWriter = false
            }));

        // Register ImapSettings so ImapPollingJob can check capacity for misfire logging
        services.Configure<ImapSettings>(configuration.GetSection("Imap"));

        // Quartz scheduler with DI
        var dbSettings = configuration
            .GetSection("Database")
            .Get<DatabaseSettings>() ?? new DatabaseSettings();

        services.AddQuartz(q =>
        {
            // Use AdoJobStore for SQL Server to enable cluster-safe scheduling.
            // For SQLite, RAMJobStore is used (single-instance, enforced by WorkerLease).
            if (dbSettings.Provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                q.UsePersistentStore(store =>
                {
                    store.UseProperties = true;
                    store.UseSqlServer(dbSettings.ConnectionString);
                    store.UseNewtonsoftJsonSerializer();
                    store.PerformSchemaValidation = false;
                });

                q.UseDefaultThreadPool(tp => tp.MaxConcurrency = 10);

                // Enable clustering so multiple worker instances share the same job store
                q.SetProperty("quartz.jobStore.clustered", "true");
                q.SetProperty("quartz.scheduler.instanceId", "AUTO");
            }
        });
        services.AddQuartzHostedService(options =>
        {
            options.WaitForJobsToComplete = true;
        });

        // Hosted services
        services.AddHostedService<HeartbeatService>();
        services.AddHostedService<ScheduleSyncService>();
        services.AddHostedService<MailPollingService>();
        services.AddHostedService<DeadLetterRetryService>();
        services.AddHostedService<DataRetentionService>();

        return services;
    }
}
