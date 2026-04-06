using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text;

namespace Mail2SNMP.Worker.Services;

/// <summary>
/// Background service that retries failed webhook deliveries from the dead letter queue.
/// Uses row-level locking (LockedUntilUtc + LockedByInstanceId) for cluster-safe operation:
/// only one instance processes each entry at a time.
/// </summary>
public class DeadLetterRetryService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DeadLetterRetryService> _logger;
    private readonly string _instanceId;
    private readonly TimeSpan _pollInterval;
    private readonly int _batchSize;
    private readonly int _maxAttempts;
    private readonly TimeSpan _lockDuration;
    private readonly int _backoffBaseMinutes;
    private readonly TimeSpan _initialDelay;

    public DeadLetterRetryService(
        IServiceScopeFactory scopeFactory,
        ILogger<DeadLetterRetryService> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _instanceId = $"{Environment.MachineName}-{Environment.ProcessId}";

        // M11: every magic number that affects retry behaviour is now configurable.
        var section = configuration.GetSection("DeadLetter");
        _pollInterval = TimeSpan.FromSeconds(section.GetValue("PollIntervalSeconds", 900));
        _batchSize = section.GetValue("BatchSize", 10);
        _maxAttempts = section.GetValue("MaxAttempts", 10);
        _lockDuration = TimeSpan.FromMinutes(section.GetValue("LockDurationMinutes", 5));
        _backoffBaseMinutes = section.GetValue("BackoffBaseMinutes", 15);
        _initialDelay = TimeSpan.FromSeconds(section.GetValue("InitialDelaySeconds", 15));
    }

    /// <summary>
    /// Waits for initial startup, then enters a polling loop that processes dead letter batches at a fixed interval.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "DeadLetterRetryService starting (Instance={InstanceId}, Poll={PollInterval}s, Batch={BatchSize}, MaxAttempts={MaxAttempts})",
            _instanceId, _pollInterval.TotalSeconds, _batchSize, _maxAttempts);

        // Dead letter auto-retry is an Enterprise-only feature.
        // Wrap the scope in a `using` block so it (and any scoped services it resolves) is
        // disposed immediately after the license check.
        using (var licenseScope = _scopeFactory.CreateScope())
        {
            var licenseProvider = licenseScope.ServiceProvider
                .GetRequiredService<Mail2SNMP.Core.Interfaces.ILicenseProvider>();
            if (!licenseProvider.IsEnterprise())
            {
                _logger.LogInformation("DeadLetterRetryService disabled — auto-retry requires an Enterprise license");
                return;
            }
        }

        // Initial delay to let other services start (configurable via DeadLetter:InitialDelaySeconds)
        await Task.Delay(_initialDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in DeadLetterRetryService processing loop");
            }

            await Task.Delay(_pollInterval, stoppingToken);
        }

        _logger.LogInformation("DeadLetterRetryService stopped");
    }

    /// <summary>
    /// Atomically claims pending dead letter entries via row-level locking, retries each webhook delivery,
    /// and applies exponential backoff on failure or marks entries as abandoned after max attempts.
    /// </summary>
    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Mail2SnmpDbContext>();
        var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
        var now = DateTime.UtcNow;

        // Atomically claim entries using raw SQL UPDATE with WHERE conditions.
        // This ensures only one worker instance processes each entry — no TOCTOU race.
        var claimedCount = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE DeadLetterEntries
            SET LockedByInstanceId = {_instanceId},
                LockedUntilUtc = {now.Add(_lockDuration)},
                Status = {(int)DeadLetterStatus.Locked}
            WHERE Status = {(int)DeadLetterStatus.Pending}
              AND (LockedUntilUtc IS NULL OR LockedUntilUtc < {now})
              AND NextRetryUtc <= {now}
              AND AttemptCount < {_maxAttempts}
            """, ct);

        if (claimedCount == 0)
            return;

        _logger.LogInformation("Claimed {Count} dead letter entries for retry", claimedCount);

        // Fetch the entries we just locked
        var entries = await db.DeadLetterEntries
            .Include(d => d.WebhookTarget)
            .Where(d => d.LockedByInstanceId == _instanceId && d.Status == DeadLetterStatus.Locked)
            .Take(_batchSize)
            .ToListAsync(ct);

        // Use the named client registered in DI; the factory manages handler lifetime,
        // so we never create raw HttpClient instances per iteration (avoids socket exhaustion).
        var httpClient = httpClientFactory.CreateClient("DeadLetterRetry");

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (entry.WebhookTarget is null || !entry.WebhookTarget.IsActive)
                {
                    _logger.LogWarning(
                        "DeadLetter {Id}: WebhookTarget {TargetId} inactive or missing. Abandoning.",
                        entry.Id, entry.WebhookTargetId);
                    entry.Status = DeadLetterStatus.Abandoned;
                    entry.LockedUntilUtc = null;
                    entry.LockedByInstanceId = null;
                    continue;
                }

                // Retry the webhook delivery
                var content = new StringContent(entry.PayloadJson, Encoding.UTF8, "application/json");
                var response = await httpClient.PostAsync(entry.WebhookTarget.Url, content, ct);
                response.EnsureSuccessStatusCode();

                // Success: remove the dead letter entry
                db.DeadLetterEntries.Remove(entry);
                _logger.LogInformation(
                    "DeadLetter {Id} retried successfully to {Url} (attempt {Attempt})",
                    entry.Id, entry.WebhookTarget.Url, entry.AttemptCount + 1);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Release lock on shutdown
                entry.Status = DeadLetterStatus.Pending;
                entry.LockedUntilUtc = null;
                entry.LockedByInstanceId = null;
                break;
            }
            catch (Exception ex)
            {
                entry.AttemptCount++;
                entry.LastError = ex.Message;
                entry.LockedUntilUtc = null;
                entry.LockedByInstanceId = null;

                if (entry.AttemptCount >= _maxAttempts)
                {
                    entry.Status = DeadLetterStatus.Abandoned;
                    _logger.LogWarning(
                        "DeadLetter {Id} abandoned after {Attempts} attempts: {Error}",
                        entry.Id, entry.AttemptCount, ex.Message);
                }
                else
                {
                    // Exponential backoff: 15min, 30min, 60min, 120min...
                    // Exponential backoff: base * 2^(attempt-1) — base configurable via DeadLetter:BackoffBaseMinutes.
                    var backoff = TimeSpan.FromMinutes(_backoffBaseMinutes * Math.Pow(2, entry.AttemptCount - 1));
                    entry.NextRetryUtc = DateTime.UtcNow.Add(backoff);
                    entry.Status = DeadLetterStatus.Pending;
                    _logger.LogWarning(
                        "DeadLetter {Id} retry failed (attempt {Attempt}/{Max}). Next retry at {NextRetry:HH:mm:ss}: {Error}",
                        entry.Id, entry.AttemptCount, _maxAttempts, entry.NextRetryUtc, ex.Message);
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
