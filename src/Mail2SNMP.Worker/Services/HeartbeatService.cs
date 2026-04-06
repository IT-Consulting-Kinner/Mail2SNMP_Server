using Mail2SNMP.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Mail2SNMP.Worker.Services;

/// <summary>
/// Background service that acquires and periodically renews a distributed worker lease.
/// Ensures only one worker instance is active in Community edition (SQLite).
/// Releases the lease on graceful shutdown.
/// </summary>
public class HeartbeatService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HeartbeatService> _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly string _instanceId;

    private static readonly TimeSpan RenewInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan AcquireTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan AcquireRetryDelay = TimeSpan.FromSeconds(5);

    public HeartbeatService(
        IServiceScopeFactory scopeFactory,
        ILogger<HeartbeatService> logger,
        IHostApplicationLifetime lifetime)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _lifetime = lifetime;
        _instanceId = $"{Environment.MachineName}-{Environment.ProcessId}";
    }

    /// <summary>
    /// Acquires the worker lease with retry, then enters a renewal loop that refreshes the lease
    /// at a fixed interval until cancellation is requested.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("HeartbeatService starting for instance {InstanceId}", _instanceId);

        // Acquire lease with retry up to 90 seconds
        var acquired = await TryAcquireWithRetryAsync(stoppingToken);
        if (!acquired)
        {
            _logger.LogCritical(
                "Failed to acquire worker lease within {Timeout}s. Shutting down.",
                AcquireTimeout.TotalSeconds);
            return;
        }

        _logger.LogInformation("Worker lease acquired for instance {InstanceId}", _instanceId);

        // Renew loop
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(RenewInterval, stoppingToken);

                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var leaseService = scope.ServiceProvider.GetRequiredService<IWorkerLeaseService>();

                    // N8: Bound the renewal + license check to 15 seconds so a hung
                    // database or license provider can never block the renew loop
                    // longer than the lease validity (90s).
                    using var opCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    opCts.CancelAfter(TimeSpan.FromSeconds(15));

                    await leaseService.RenewLeaseAsync(_instanceId, opCts.Token);
                    _logger.LogDebug("Lease renewed for instance {InstanceId}", _instanceId);

                    // Check if the current license still allows this worker instance.
                    // On downgrade: only the newest (excess) workers stop — the oldest remain.
                    var license = scope.ServiceProvider.GetRequiredService<ILicenseProvider>();
                    var activeLeases = await leaseService.GetActiveLeasesAsync(opCts.Token);
                    var maxWorkers = license.GetLimit("maxworkerinstances");
                    if (activeLeases.Count > maxWorkers)
                    {
                        // Sort by StartedUtc ascending — the oldest workers get to stay
                        var allowedInstanceIds = activeLeases
                            .OrderBy(l => l.StartedUtc)
                            .Take(maxWorkers)
                            .Select(l => l.InstanceId)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);

                        if (!allowedInstanceIds.Contains(_instanceId))
                        {
                            _logger.LogWarning(
                                "License downgrade detected: {Active} workers active but only {Max} allowed. " +
                                "This instance ({InstanceId}) is among the newest and will shut down gracefully.",
                                activeLeases.Count, maxWorkers, _instanceId);
                            _lifetime.StopApplication();
                            return;
                        }

                        _logger.LogInformation(
                            "License downgrade detected: {Active} workers active but only {Max} allowed. " +
                            "This instance ({InstanceId}) is among the oldest and will remain active.",
                            activeLeases.Count, maxWorkers, _instanceId);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to renew lease for instance {InstanceId}", _instanceId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
    }

    /// <summary>
    /// Releases the distributed worker lease on graceful shutdown so another instance can acquire it.
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("HeartbeatService stopping. Releasing lease for {InstanceId}", _instanceId);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var leaseService = scope.ServiceProvider.GetRequiredService<IWorkerLeaseService>();
            await leaseService.ReleaseLeaseAsync(_instanceId, cancellationToken);
            _logger.LogInformation("Lease released for instance {InstanceId}", _instanceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to release lease for instance {InstanceId}", _instanceId);
        }

        await base.StopAsync(cancellationToken);
    }

    /// <summary>
    /// Attempts to acquire the worker lease, retrying every few seconds until the timeout deadline is reached.
    /// Returns <c>true</c> if the lease was acquired, <c>false</c> if the deadline expired.
    /// </summary>
    private async Task<bool> TryAcquireWithRetryAsync(CancellationToken stoppingToken)
    {
        var deadline = DateTime.UtcNow.Add(AcquireTimeout);

        while (DateTime.UtcNow < deadline && !stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var leaseService = scope.ServiceProvider.GetRequiredService<IWorkerLeaseService>();
                var result = await leaseService.TryAcquireLeaseAsync(_instanceId, stoppingToken);

                if (result)
                    return true;

                _logger.LogWarning(
                    "Lease acquisition failed. Retrying in {Delay}s (deadline: {Deadline:HH:mm:ss})",
                    AcquireRetryDelay.TotalSeconds, deadline);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during lease acquisition attempt");
            }

            await Task.Delay(AcquireRetryDelay, stoppingToken);
        }

        return false;
    }
}
