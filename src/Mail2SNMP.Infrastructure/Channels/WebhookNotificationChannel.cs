using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Core.Services;
using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Infrastructure.Security;
using Mail2SNMP.Models.DTOs;
using Mail2SNMP.Models.Entities;
using Mail2SNMP.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Mail2SNMP.Infrastructure.Channels;

/// <summary>
/// Notification channel that delivers events as HTTP POST webhooks.
/// Supports optional HMAC-SHA256 signing (Enterprise) and dead-letter
/// queuing for failed deliveries (Enterprise).
/// </summary>
public class WebhookNotificationChannel : INotificationChannel
{
    private readonly Mail2SnmpDbContext _db;
    private readonly ICredentialEncryptor _encryptor;
    private readonly ILicenseProvider _license;
    private readonly IDeadLetterService _deadLetterService;
    private readonly TemplateEngine _templateEngine;
    private readonly FloodProtectionService _floodProtection;
    private readonly NotificationDedupCache _dedupCache;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly bool _allowPrivateTargets;
    private readonly ILogger<WebhookNotificationChannel> _logger;

    /// <summary>
    /// Stable channel discriminator ('webhook') used to select this channel from the registered
    /// <see cref="INotificationChannel"/> set. Must remain constant: persisted job/channel
    /// assignments reference channels by this value.
    /// </summary>
    public string ChannelName => "webhook";

    /// <summary>
    /// Initializes the channel with its collaborators.
    /// </summary>
    /// <param name="db">Database context used to load active webhook targets.</param>
    /// <param name="encryptor">Decryptor for the AES-GCM-encrypted per-target HMAC signing secret.</param>
    /// <param name="license">License gate; HMAC request signing is applied only under an Enterprise license.</param>
    /// <param name="deadLetterService">Persists failed deliveries as dead letters (Enterprise additionally retries them).</param>
    /// <param name="templateEngine">Renders the JSON payload body from the configured template.</param>
    /// <param name="floodProtection">Per-target rate limiter enforcing each target's maximum requests-per-minute.</param>
    /// <param name="dedupCache">Suppresses duplicate webhooks for the same target/event pair.</param>
    /// <param name="httpClientFactory">Supplies the named "WebhookSend" <see cref="System.Net.Http.HttpClient"/> with correctly managed handler lifetime.</param>
    /// <param name="configuration">
    /// Read for <c>Security:AllowPrivateWebhookTargets</c>. When false (the default), the SSRF guard blocks
    /// delivery to loopback / link-local / RFC1918 / cloud-metadata addresses so cloud deployments are safe by default.
    /// </param>
    /// <param name="logger">Diagnostic logger for delivery failures, SSRF blocks, and malformed configuration.</param>
    public WebhookNotificationChannel(
        Mail2SnmpDbContext db,
        ICredentialEncryptor encryptor,
        ILicenseProvider license,
        IDeadLetterService deadLetterService,
        TemplateEngine templateEngine,
        FloodProtectionService floodProtection,
        NotificationDedupCache dedupCache,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<WebhookNotificationChannel> logger)
    {
        _db = db;
        _encryptor = encryptor;
        _license = license;
        _deadLetterService = deadLetterService;
        _templateEngine = templateEngine;
        // R1: opt-in flag for operators who legitimately need internal webhook
        // targets (e.g. on-prem Splunk on 10.x). Defaults to false so cloud
        // deployments are safe by default.
        _allowPrivateTargets = configuration.GetValue<bool>("Security:AllowPrivateWebhookTargets");
        _floodProtection = floodProtection;
        _dedupCache = dedupCache;
        // T5: use the named "WebhookSend" client registered in DI instead of mutating
        // an injected HttpClient. The factory handles handler lifetime correctly.
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    // AR-2: the legacy broadcast SendAsync(NotificationContext) was removed — it had
    // zero call sites (all real dispatch is per-target via the job's assignments)
    // and its presence suggested a working generic path that did not exist.

    /// <summary>
    /// Sends a webhook notification to a specific target (per-job assignment). Applies dedup and rate-limiting.
    /// </summary>
    /// <returns>
    /// FN-3: <c>true</c> when the POST succeeded (or an earlier send already covered
    /// this event/target via notification-dedup); <c>false</c> on rate-limit drop or
    /// failure — failures are dead-lettered for retry, but the event has not been
    /// delivered yet, so callers must not mark it Notified on this basis.
    /// </returns>
    public async Task<bool> SendToWebhookTargetAsync(NotificationContext context, WebhookTarget target, CancellationToken ct = default)
    {
        var targetKey = $"webhook:{target.Id}";

        if (_dedupCache.IsDuplicate(targetKey, context.EventId))
        {
            _logger.LogDebug("Notification dedup: skipping duplicate webhook to {Target} for event {EventId}", target.Name, context.EventId);
            // An earlier send already covered this event/target pair — delivered.
            return true;
        }

        if (_floodProtection.IsRateLimited(targetKey, target.MaxRequestsPerMinute))
        {
            Services.Mail2SnmpMetrics.RateLimitHits.WithLabels("webhook-target").Inc();
            _logger.LogWarning("Webhook to {Target} rate-limited ({Max}/min)", target.Name, target.MaxRequestsPerMinute);
            return false;
        }

        try
        {
            await SendWebhookAsync(target, context, ct);
            Services.Mail2SnmpMetrics.NotificationsSent.WithLabels("webhook").Inc();
            Services.Mail2SnmpMetrics.WebhookRequestsSent.WithLabels(target.Name).Inc();
            _logger.LogInformation("Webhook sent to {Url} for event {EventId}", target.Url, context.EventId);
            return true;
        }
        catch (Exception ex)
        {
            Services.Mail2SnmpMetrics.NotificationsFailed.WithLabels("webhook").Inc();
            _logger.LogError(ex, "Failed to send webhook to {Url}: {Message}", target.Url, ex.Message);
            await CreateDeadLetterAsync(target, context, ex.Message, ct);
            return false;
        }
    }

    /// <summary>
    /// Constructs and sends a single webhook HTTP POST with optional HMAC-SHA256
    /// signature (Enterprise) and custom headers.
    /// </summary>
    private async Task SendWebhookAsync(WebhookTarget target, NotificationContext context, CancellationToken ct)
    {
        var payload = _templateEngine.BuildPayload(context, context.WebhookTemplate ?? target.PayloadTemplate);
        var json = JsonSerializer.Serialize(payload);
        // N2: dispose StringContent explicitly to release its internal buffer.
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Add HMAC signature for Enterprise
        if (_license.IsEnterprise() && !string.IsNullOrEmpty(target.EncryptedSecret))
        {
            var secret = _encryptor.Decrypt(target.EncryptedSecret);
            var bodyBytes = Encoding.UTF8.GetBytes(json);
            var hmac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), bodyBytes);
            var signature = "sha256=" + Convert.ToHexString(hmac).ToLowerInvariant();
            content.Headers.Add("X-Mail2SNMP-Signature", signature);
            content.Headers.Add("X-Mail2SNMP-Timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
        }

        // Add custom headers
        if (!string.IsNullOrEmpty(target.Headers))
        {
            try
            {
                var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(target.Headers);
                if (headers != null)
                {
                    foreach (var (key, value) in headers)
                        content.Headers.TryAddWithoutValidation(key, value);
                }
            }
            catch (JsonException ex)
            {
                // N4: log the malformed-headers error so the operator notices
                // misconfigured custom headers instead of silently dropping them.
                _logger.LogWarning(ex,
                    "Invalid headers JSON for webhook target {Name}. The default headers will be used.",
                    target.Name);
            }
        }

        // R1: SSRF guard. Refuse to POST to loopback / link-local / RFC1918 /
        // cloud metadata addresses unless the operator has explicitly opted in.
        // The check resolves the host via DNS so an attacker cannot tunnel a
        // public name into 169.254.169.254.
        if (!SsrfGuard.IsSafeOutboundUrl(target.Url, _allowPrivateTargets, out var reason))
        {
            _logger.LogWarning(
                "Webhook target {Name} ({Url}) blocked by SSRF guard: {Reason}",
                target.Name, target.Url, reason);
            throw new InvalidOperationException(
                $"Webhook target '{target.Name}' was blocked by the SSRF guard: {reason}");
        }

        // N2: dispose the HttpResponseMessage explicitly so the underlying
        // socket is released back to the connection pool immediately.
        var httpClient = _httpClientFactory.CreateClient("WebhookSend");
        using var response = await httpClient.PostAsync(target.Url, content, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Creates a dead-letter entry for a failed webhook delivery.
    /// Both editions persist entries; Enterprise adds automatic retry.
    /// </summary>
    private async Task CreateDeadLetterAsync(WebhookTarget target, NotificationContext context, string error, CancellationToken ct)
    {
        // V-fix: mirror the SNMP channel's guards. A redelivery's lifecycle is owned
        // by the retry service, and a synthetic test event (negative EventId, UC-7)
        // has no Events row — inserting it would violate the required EventId FK,
        // throw from inside the caller's catch block, break the Task<bool> delivery
        // contract and crash the test-send report.
        if (context.IsRedelivery || context.EventId <= 0) return;
        try
        {
            var payload = _templateEngine.BuildPayload(context, context.WebhookTemplate ?? target.PayloadTemplate);
            await _deadLetterService.CreateAsync(new DeadLetterEntry
            {
                WebhookTargetId = target.Id,
                EventId = context.EventId,
                PayloadJson = JsonSerializer.Serialize(payload),
                LastError = error,
                AttemptCount = 1,
                Status = DeadLetterStatus.Pending
            }, ct);
        }
        catch (Exception dlEx)
        {
            // Dead-lettering is best-effort — it must never mask the original
            // delivery failure.
            _logger.LogError(dlEx, "Failed to dead-letter webhook for target {Name}, event {EventId}", target.Name, context.EventId);
        }
    }
}
