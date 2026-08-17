using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Models.Configuration;
using Mail2SNMP.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
    private readonly bool _allowPrivateTargets;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeadLetterRetryService"/> class.
    /// </summary>
    /// <param name="scopeFactory">Factory used to create a scope per batch for resolving scoped services.</param>
    /// <param name="logger">The logger for claim, retry, and abandonment diagnostics.</param>
    /// <param name="deadLetter">
    /// AR-6: validated <c>DeadLetter</c> options (poll interval, batch size, attempt limit,
    /// lock duration, backoff). Previously read ad hoc from raw <c>IConfiguration</c>, which
    /// bypassed the startup validation every other section goes through.
    /// </param>
    /// <param name="security">Validated <c>Security</c> options; <c>AllowPrivateWebhookTargets</c> controls the SSRF guard.</param>
    public DeadLetterRetryService(
        IServiceScopeFactory scopeFactory,
        ILogger<DeadLetterRetryService> logger,
        IOptions<DeadLetterSettings> deadLetter,
        IOptions<SecuritySettings> security)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _instanceId = $"{Environment.MachineName}-{Environment.ProcessId}";

        // M11: every magic number that affects retry behaviour is configurable.
        var settings = deadLetter.Value;
        _pollInterval = TimeSpan.FromSeconds(settings.PollIntervalSeconds);
        _batchSize = settings.BatchSize;
        _maxAttempts = settings.MaxAttempts;
        // Default raised 5 -> 7 minutes so the out-of-the-box lease clears the safe
        // minimum below (10 x 35s + 60s = 410s) without a startup warning.
        _lockDuration = TimeSpan.FromMinutes(settings.LockDurationMinutes);

        // Verified fix (FN-1 follow-up): the lease must comfortably outlast the
        // worst-case batch (BatchSize x 30s HTTP timeout, plus SNMP DNS timeouts and
        // the final save). If it lapsed mid-batch, a second instance could legally
        // re-claim rows still being processed — duplicate delivery plus a RowVersion
        // conflict that discards the first instance's batch bookkeeping. Floor the
        // effective lease at BatchSize x 35s + 60s headroom.
        var minLease = TimeSpan.FromSeconds(_batchSize * 35 + 60);
        if (_lockDuration < minLease)
        {
            _logger.LogWarning(
                "DeadLetter:LockDurationMinutes ({Configured}) is below the safe minimum for BatchSize {BatchSize}; using {Effective} instead.",
                _lockDuration, _batchSize, minLease);
            _lockDuration = minLease;
        }
        _backoffBaseMinutes = settings.BackoffBaseMinutes;
        _initialDelay = TimeSpan.FromSeconds(settings.InitialDelaySeconds);

        // S1: dead-letter retries must apply the same SSRF guard as the live
        // delivery path. Without this an Operator could create a webhook with
        // a benign URL, let it fail (going to dead letter), then update the
        // URL to 169.254.169.254 — and the retry path would happily fetch it.
        _allowPrivateTargets = security.Value.AllowPrivateWebhookTargets;
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

        // AR-1: publish the real pending depth each cycle (set-from-DB rather than
        // inc/dec so the gauge survives restarts and cross-process writes correctly).
        Mail2SNMP.Infrastructure.Services.Mail2SnmpMetrics.DeadLetterPending.Set(
            await db.DeadLetterEntries.CountAsync(d => d.Status == DeadLetterStatus.Pending, ct));

        // FN-1: Atomically claim AT MOST _batchSize entries, and also reclaim entries
        // whose Locked lease has expired. The previous query flipped EVERY eligible row
        // to Locked but the fetch below only processed _batchSize of them; the surplus
        // stayed Locked owned by this process forever — the re-claim required
        // Status=Pending, so the lock-expiry net never applied, and a worker restart
        // changed the instance id and orphaned them permanently (silent loss of failed
        // webhook deliveries on a routine deploy). Bounding the claim to _batchSize and
        // reclaiming expired locks (Status=Locked AND LockedUntilUtc < now, from any
        // instance) closes both. The IN-subquery form keeps this a single atomic UPDATE
        // on both SQLite (LIMIT) and SQL Server (TOP).
        var lockUntil = now.Add(_lockDuration);
        var pending = (int)DeadLetterStatus.Pending;
        var locked = (int)DeadLetterStatus.Locked;
        var isSqlite = db.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

        FormattableString claimSql;
        if (isSqlite)
        {
            claimSql = $"""
                UPDATE DeadLetterEntries
                SET LockedByInstanceId = {_instanceId}, LockedUntilUtc = {lockUntil}, Status = {locked}
                WHERE Id IN (
                    SELECT Id FROM DeadLetterEntries
                    WHERE ((Status = {pending} AND (LockedUntilUtc IS NULL OR LockedUntilUtc < {now}))
                           OR (Status = {locked} AND LockedUntilUtc < {now}))
                      AND NextRetryUtc <= {now}
                      AND AttemptCount < {_maxAttempts}
                    ORDER BY NextRetryUtc
                    LIMIT {_batchSize})
                """;
        }
        else
        {
            claimSql = $"""
                UPDATE DeadLetterEntries
                SET LockedByInstanceId = {_instanceId}, LockedUntilUtc = {lockUntil}, Status = {locked}
                WHERE Id IN (
                    SELECT TOP ({_batchSize}) Id FROM DeadLetterEntries
                    WHERE ((Status = {pending} AND (LockedUntilUtc IS NULL OR LockedUntilUtc < {now}))
                           OR (Status = {locked} AND LockedUntilUtc < {now}))
                      AND NextRetryUtc <= {now}
                      AND AttemptCount < {_maxAttempts}
                    ORDER BY NextRetryUtc)
                """;
        }
        var claimedCount = await db.Database.ExecuteSqlInterpolatedAsync(claimSql, ct);

        if (claimedCount == 0)
            return;

        _logger.LogInformation("Claimed {Count} dead letter entries for retry", claimedCount);

        // Fetch every entry we currently own and hold Locked. The claim above already
        // bounded the newly-locked set to _batchSize, so we deliberately do NOT re-cap
        // the fetch — capping here would re-strand rows we just locked (fresh lease,
        // never fetched) exactly like the original bug.
        var entries = await db.DeadLetterEntries
            .Include(d => d.WebhookTarget)
            .Include(d => d.SnmpTarget)
            .Where(d => d.LockedByInstanceId == _instanceId && d.Status == DeadLetterStatus.Locked)
            .OrderBy(d => d.NextRetryUtc)
            .ToListAsync(ct);

        // Use the named client registered in DI; the factory manages handler lifetime,
        // so we never create raw HttpClient instances per iteration (avoids socket exhaustion).
        var httpClient = httpClientFactory.CreateClient("DeadLetterRetry");

        // V6: the live delivery path (WebhookNotificationChannel) signs Enterprise
        // payloads with HMAC-SHA256. The retry path must do the same, otherwise a
        // receiver that enforces signature verification rejects every retry (and
        // any accepted retry arrives unauthenticated). Resolve the encryptor and
        // license once per batch.
        var encryptor = scope.ServiceProvider.GetRequiredService<Mail2SNMP.Core.Interfaces.ICredentialEncryptor>();
        var license = scope.ServiceProvider.GetRequiredService<Mail2SNMP.Core.Interfaces.ILicenseProvider>();

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                // UC-3: SNMP entries are retried through the real channel (the trap is
                // rebuilt from the serialized NotificationContext). Handled first;
                // everything below this block is the webhook retry path.
                if (entry.SnmpTargetId is not null)
                {
                    if (entry.SnmpTarget is null || !entry.SnmpTarget.IsActive)
                    {
                        _logger.LogWarning(
                            "DeadLetter {Id}: SnmpTarget {TargetId} inactive or missing. Abandoning.",
                            entry.Id, entry.SnmpTargetId);
                        entry.Status = DeadLetterStatus.Abandoned;
                        entry.LockedUntilUtc = null;
                        entry.LockedByInstanceId = null;
                        continue;
                    }

                    var context = System.Text.Json.JsonSerializer
                        .Deserialize<Mail2SNMP.Models.DTOs.NotificationContext>(entry.PayloadJson);
                    if (context is null)
                    {
                        entry.Status = DeadLetterStatus.Abandoned;
                        entry.LastError = "Stored NotificationContext could not be deserialized.";
                        entry.LockedUntilUtc = null;
                        entry.LockedByInstanceId = null;
                        continue;
                    }
                    // Mark as redelivery so the channel bypasses notification-dedup and
                    // does NOT enqueue a fresh dead-letter entry on failure — this
                    // entry's backoff/abandon lifecycle is owned here.
                    context.IsRedelivery = true;

                    var snmpChannel = scope.ServiceProvider
                        .GetServices<Mail2SNMP.Core.Interfaces.INotificationChannel>()
                        .FirstOrDefault(c => c.ChannelName == Mail2SNMP.Core.Interfaces.INotificationChannel.Snmp);

                    Mail2SNMP.Infrastructure.Services.Mail2SnmpMetrics.DeadLetterRetried.Inc();
                    var sent = snmpChannel != null &&
                               await snmpChannel.SendToSnmpTargetAsync(context, entry.SnmpTarget, ct);
                    if (sent)
                    {
                        db.DeadLetterEntries.Remove(entry);
                        _logger.LogInformation(
                            "DeadLetter {Id} (SNMP) retried successfully to {Host}:{Port} (attempt {Attempt})",
                            entry.Id, entry.SnmpTarget.Host, entry.SnmpTarget.Port, entry.AttemptCount + 1);
                    }
                    else
                    {
                        // Same backoff/abandon handling as the webhook catch-path below.
                        throw new InvalidOperationException(
                            $"SNMP trap redelivery to '{entry.SnmpTarget.Name}' failed (see channel log).");
                    }
                    continue;
                }

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

                // S1: SSRF guard. Re-validate the URL on every retry — the
                // webhook target may have been edited since the dead-letter
                // entry was created, and the live delivery path's R1 check
                // does not protect us here.
                if (!Mail2SNMP.Infrastructure.Security.SsrfGuard.IsSafeOutboundUrl(entry.WebhookTarget.Url, _allowPrivateTargets, out var ssrfReason))
                {
                    _logger.LogWarning(
                        "DeadLetter {Id}: webhook target {Name} ({Url}) blocked by SSRF guard: {Reason}. Abandoning.",
                        entry.Id, entry.WebhookTarget.Name, entry.WebhookTarget.Url, ssrfReason);
                    entry.Status = DeadLetterStatus.Abandoned;
                    entry.LastError = $"SSRF guard rejected target URL: {ssrfReason}";
                    entry.LockedUntilUtc = null;
                    entry.LockedByInstanceId = null;
                    continue;
                }

                // H-7: the request is assembled by the SAME shared builder the
                // first-attempt path uses (V6 signature AND the target's custom
                // headers). The previous hand-rolled copy re-derived the signature but
                // dropped the custom headers, so a receiver that authenticates by
                // header rejected every retry — silently, and only for redeliveries.
                // Dispose content + response explicitly to release sockets promptly.
                using var content = Mail2SNMP.Infrastructure.Channels.WebhookRequestBuilder.BuildContent(
                    entry.WebhookTarget, entry.PayloadJson, encryptor, license, _logger, out var signingFailed);

                if (signingFailed)
                {
                    // Master-key drift: never downgrade a signed delivery to unsigned.
                    entry.Status = DeadLetterStatus.Pending;
                    entry.LockedUntilUtc = null;
                    entry.LockedByInstanceId = null;
                    continue;
                }

                Mail2SNMP.Infrastructure.Services.Mail2SnmpMetrics.DeadLetterRetried.Inc();
                using var response = await httpClient.PostAsync(entry.WebhookTarget.Url, content, ct);
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

                // The batch bookkeeping accumulated so far MUST be persisted before the
                // loop exits. It used to rely on the single SaveChanges after the loop,
                // which the shutdown then skipped (CancellationToken already signalled):
                // entries that had been delivered successfully were never removed, so the
                // next start re-sent every one of them — duplicate traps and webhooks
                // after each restart. Save without the cancelled token so the write
                // actually completes.
                await db.SaveChangesAsync(CancellationToken.None);
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
                    // Exponential backoff: base * 2^(attempt-1) — base configurable via DeadLetter:BackoffBaseMinutes.
                    // N15: reuse the `now` captured at the top of ProcessBatchAsync so all
                    // entries in the same batch get a consistent NextRetryUtc baseline.
                    var backoff = TimeSpan.FromMinutes(_backoffBaseMinutes * Math.Pow(2, entry.AttemptCount - 1));
                    entry.NextRetryUtc = now.Add(backoff);
                    entry.Status = DeadLetterStatus.Pending;
                    _logger.LogWarning(
                        "DeadLetter {Id} retry failed (attempt {Attempt}/{Max}). Next retry at {NextRetry:HH:mm:ss}: {Error}",
                        entry.Id, entry.AttemptCount, _maxAttempts, entry.NextRetryUtc, ex.Message);
                }
            }
            // Persist each entry's outcome immediately rather than once per batch. A
            // batch can run for minutes (BatchSize × HTTP timeout), and a crash or
            // shutdown in the middle used to discard the bookkeeping for every entry
            // processed so far — including the removals of successfully redelivered
            // ones, which were then delivered again on the next cycle.
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }
}
