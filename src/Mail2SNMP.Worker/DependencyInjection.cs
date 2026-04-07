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

        var effectiveConnectionString = dbSettings.GetEffectiveConnectionString();
        services.AddQuartz(q =>
        {
            // Use AdoJobStore for SQL Server to enable cluster-safe scheduling.
            // For SQLite, RAMJobStore is used (single-instance, enforced by WorkerLease).
            if (dbSettings.Provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                q.UsePersistentStore(store =>
                {
                    store.UseProperties = true;
                    store.UseSqlServer(effectiveConnectionString);
                    store.UseNewtonsoftJsonSerializer();
                    store.PerformSchemaValidation = false;
                });

                q.UseDefaultThreadPool(tp => tp.MaxConcurrency = 10);

                // N9: build a deterministic-but-unique instance id instead of relying
                // on Quartz's "AUTO" generator. AUTO uses MachineName + timestamp +
                // counter, which can collide in Kubernetes when a pod is recycled
                // and a new pod inherits the same hostname. We combine MachineName
                // with the current process id AND a fresh GUID prefix so duplicates
                // across pods are statistically impossible while still being readable
                // in the QRTZ_SCHEDULER_STATE table for diagnostics.
                q.SetProperty("quartz.jobStore.clustered", "true");
                var quartzInstanceId = $"{Environment.MachineName}-{Environment.ProcessId}-{Guid.NewGuid().ToString("N")[..8]}";
                q.SetProperty("quartz.scheduler.instanceId", quartzInstanceId);
            }
        });
        services.AddQuartzHostedService(options =>
        {
            options.WaitForJobsToComplete = true;
        });

        // Bind KeepAlive + UpdateCheck options
        services.Configure<KeepAliveSettings>(configuration.GetSection("KeepAlive"));
        services.Configure<UpdateCheckSettings>(configuration.GetSection("UpdateCheck"));

        // Named HttpClients for worker background services. Using named clients allows
        // the IHttpClientFactory to manage handler lifetimes correctly and avoids the
        // anti-pattern of creating one HttpClient per call (socket exhaustion).
        services.AddHttpClient("DeadLetterRetry", c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient("UpdateCheck", c => c.Timeout = TimeSpan.FromSeconds(15));

        // Hosted services
        services.AddHostedService<HeartbeatService>();
        services.AddHostedService<ScheduleSyncService>();
        services.AddHostedService<MailPollingService>();
        services.AddHostedService<DeadLetterRetryService>();
        services.AddHostedService<DataRetentionService>();
        services.AddHostedService<KeepAliveService>();
        services.AddHostedService<UpdateCheckService>();
        services.AddHostedService<AutoAcknowledgeService>();
        services.AddHostedService<ImapIdleService>();

        return services;
    }
}
