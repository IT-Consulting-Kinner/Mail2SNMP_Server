using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Core.Services;
using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Models.DTOs;
using Mail2SNMP.Models.Entities;
using Mail2SNMP.Models.Enums;
using Microsoft.EntityFrameworkCore;
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
    private readonly HttpClient _httpClient;
    private readonly ILogger<WebhookNotificationChannel> _logger;

    public string ChannelName => "webhook";

    public WebhookNotificationChannel(
        Mail2SnmpDbContext db,
        ICredentialEncryptor encryptor,
        ILicenseProvider license,
        IDeadLetterService deadLetterService,
        TemplateEngine templateEngine,
        FloodProtectionService floodProtection,
        NotificationDedupCache dedupCache,
        HttpClient httpClient,
        ILogger<WebhookNotificationChannel> logger)
    {
        _db = db;
        _encryptor = encryptor;
        _license = license;
        _deadLetterService = deadLetterService;
        _templateEngine = templateEngine;
        _floodProtection = floodProtection;
        _dedupCache = dedupCache;
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _logger = logger;
    }

    /// <summary>
    /// Sends webhook notifications to all active webhook targets for the given event.
    /// Applies deduplication and rate-limiting per target. Failed deliveries are
    /// queued as dead letters when running under an Enterprise license.
    /// </summary>
    public async Task SendAsync(NotificationContext context, CancellationToken cancellationToken = default)
    {
        var targets = await _db.WebhookTargets.Where(t => t.IsActive).ToListAsync(cancellationToken);

        foreach (var target in targets)
        {
            var targetKey = $"webhook:{target.Id}";

            if (_dedupCache.IsDuplicate(targetKey, context.EventId))
            {
                _logger.LogDebug("Notification dedup: skipping duplicate webhook to {Target} for event {EventId}", target.Name, context.EventId);
                continue;
            }

            if (_floodProtection.IsRateLimited(targetKey, target.MaxRequestsPerMinute))
            {
                _logger.LogWarning("Webhook to {Target} rate-limited ({Max}/min)", target.Name, target.MaxRequestsPerMinute);
                continue;
            }

            try
            {
                await SendWebhookAsync(target, context, cancellationToken);
                _logger.LogInformation("Webhook sent to {Url} for event {EventId}", target.Url, context.EventId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send webhook to {Url}: {Message}", target.Url, ex.Message);
                await CreateDeadLetterAsync(target, context, ex.Message, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Sends a webhook notification to a specific target (per-job assignment). Applies dedup and rate-limiting.
    /// </summary>
    public async Task SendToWebhookTargetAsync(NotificationContext context, WebhookTarget target, CancellationToken ct = default)
    {
        var targetKey = $"webhook:{target.Id}";

        if (_dedupCache.IsDuplicate(targetKey, context.EventId))
        {
            _logger.LogDebug("Notification dedup: skipping duplicate webhook to {Target} for event {EventId}", target.Name, context.EventId);
            return;
        }

        if (_floodProtection.IsRateLimited(targetKey, target.MaxRequestsPerMinute))
        {
            _logger.LogWarning("Webhook to {Target} rate-limited ({Max}/min)", target.Name, target.MaxRequestsPerMinute);
            return;
        }

        try
        {
            await SendWebhookAsync(target, context, ct);
            _logger.LogInformation("Webhook sent to {Url} for event {EventId}", target.Url, context.EventId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send webhook to {Url}: {Message}", target.Url, ex.Message);
            await CreateDeadLetterAsync(target, context, ex.Message, ct);
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

        // N2: dispose the HttpResponseMessage explicitly so the underlying
        // socket is released back to the connection pool immediately.
        using var response = await _httpClient.PostAsync(target.Url, content, ct);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Creates a dead-letter entry for a failed webhook delivery.
    /// Both editions persist entries; Enterprise adds automatic retry.
    /// </summary>
    private async Task CreateDeadLetterAsync(WebhookTarget target, NotificationContext context, string error, CancellationToken ct)
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
}
