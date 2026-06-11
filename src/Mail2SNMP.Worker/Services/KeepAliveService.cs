using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Models.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mail2SNMP.Worker.Services;

/// <summary>
/// Background service that periodically sends mail2SNMPKeepAliveNotification traps
/// to all SNMP targets that have SendKeepAlive enabled.
///
/// Cluster behavior: in a multi-worker deployment, only the worker instance that
/// currently holds the "primary" lease (smallest InstanceId among active leases)
/// will send KeepAlive traps. This prevents duplicate keep-alive notifications
/// arriving at the monitoring system.
/// </summary>
public class KeepAliveService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KeepAliveSettings _settings;
    private readonly ILogger<KeepAliveService> _logger;
    private readonly string _instanceId;

    public KeepAliveService(
        IServiceScopeFactory scopeFactory,
        IOptions<KeepAliveSettings> settings,
        ILogger<KeepAliveService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
        // Mirrors the format used by HeartbeatService so the lease lookup matches.
        _instanceId = $"{Environment.MachineName}-{Environment.ProcessId}";
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("KeepAliveService disabled via configuration.");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, _settings.IntervalMinutes));
        _logger.LogInformation("KeepAliveService started (interval: {Minutes} min, instance: {Instance})",
            _settings.IntervalMinutes, _instanceId);

        // Wait one interval before sending the first KeepAlive — service start itself
        // is already a "sign of life", and we want to give the cluster time to settle.
        try
        {
            await Task.Delay(interval, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();

                // Peer-review: use the shared PrimaryElection helper instead of an
                // inline copy. Cluster split-brain prevention — only the elected
                // primary (lexicographically smallest active InstanceId) sends
                // KeepAlive; a single worker is implicitly primary. This was the
                // fourth open-coded copy the helper was created to eliminate (N5).
                var leaseService = scope.ServiceProvider.GetRequiredService<IWorkerLeaseService>();
                var isPrimary = await PrimaryElection.IsPrimaryAsync(leaseService, _instanceId, stoppingToken);
                if (!isPrimary)
                {
                    _logger.LogDebug(
                        "KeepAlive skipped — this instance ({This}) is not the cluster primary.",
                        _instanceId);
                }

                if (isPrimary)
                {
                    var channels = scope.ServiceProvider.GetServices<INotificationChannel>();
                    var snmp = channels.FirstOrDefault(c => c.ChannelName == "snmp");
                    if (snmp != null)
                    {
                        await snmp.SendKeepAliveAsync(stoppingToken);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "KeepAlive iteration failed");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
