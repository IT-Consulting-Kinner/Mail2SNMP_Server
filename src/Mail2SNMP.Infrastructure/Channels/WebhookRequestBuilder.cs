using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Models.Entities;
using Microsoft.Extensions.Logging;

namespace Mail2SNMP.Infrastructure.Channels;

/// <summary>
/// H-7: single definition of how a webhook request body is assembled — HMAC-SHA256
/// signature (Enterprise) plus the target's configured custom headers.
/// </summary>
/// <remarks>
/// The first-attempt path (<see cref="WebhookNotificationChannel"/>) and the
/// dead-letter retry path (<c>DeadLetterRetryService</c>) previously each built the
/// request themselves and had to stay byte-compatible by discipline alone. They had
/// already drifted: the retry path re-derived the signature but <b>dropped the
/// target's custom headers</b>, so a receiver that authenticates by header rejected
/// every retry — silently, and only for redeliveries. Both paths now call this
/// helper, so the request is assembled identically by construction.
/// </remarks>
public static class WebhookRequestBuilder
{
    /// <summary>
    /// Builds the HTTP content for a webhook delivery: the JSON body, the Enterprise
    /// HMAC signature when the target has a secret, and the target's custom headers.
    /// </summary>
    /// <param name="target">The target whose secret and headers apply.</param>
    /// <param name="json">The serialized payload, sent verbatim (and signed as sent).</param>
    /// <param name="encryptor">Decryptor for the stored signing secret.</param>
    /// <param name="license">License gate — signing is an Enterprise feature.</param>
    /// <param name="logger">Logger for malformed custom-header JSON and signing failures.</param>
    /// <param name="signingFailed">
    /// <c>true</c> when the target expects a signature but it could not be produced
    /// (e.g. master-key drift). Callers must NOT send in that case: an unsigned body
    /// would reach a receiver that enforces verification.
    /// </param>
    /// <returns>Content ready to POST. The caller owns disposal.</returns>
    public static StringContent BuildContent(
        WebhookTarget target,
        string json,
        ICredentialEncryptor encryptor,
        ILicenseProvider license,
        ILogger logger,
        out bool signingFailed)
    {
        signingFailed = false;
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        if (license.IsEnterprise() && !string.IsNullOrEmpty(target.EncryptedSecret))
        {
            try
            {
                var secret = encryptor.Decrypt(target.EncryptedSecret);
                var hmac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(json));
                content.Headers.Add("X-Mail2SNMP-Signature", "sha256=" + Convert.ToHexString(hmac).ToLowerInvariant());
                content.Headers.Add("X-Mail2SNMP-Timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
            }
            catch (Exception ex)
            {
                // A decrypt failure must never silently downgrade a signed delivery to
                // an unsigned one — report it and let the caller skip the attempt.
                logger.LogError(ex,
                    "Failed to sign webhook payload for target {Name}. The delivery will not be attempted unsigned.",
                    target.Name);
                signingFailed = true;
                return content;
            }
        }

        ApplyCustomHeaders(target, content.Headers, logger);
        return content;
    }

    /// <summary>
    /// Applies the target's configured custom headers (a JSON object of name/value
    /// pairs). Malformed JSON is logged and skipped rather than failing the delivery.
    /// </summary>
    private static void ApplyCustomHeaders(WebhookTarget target, HttpHeaders headers, ILogger logger)
    {
        if (string.IsNullOrEmpty(target.Headers)) return;

        try
        {
            var custom = JsonSerializer.Deserialize<Dictionary<string, string>>(target.Headers);
            if (custom is null) return;
            foreach (var (key, value) in custom)
                headers.TryAddWithoutValidation(key, value);
        }
        catch (JsonException ex)
        {
            // N4: surface misconfigured headers instead of dropping them silently.
            logger.LogWarning(ex,
                "Invalid headers JSON for webhook target {Name}. The default headers will be used.",
                target.Name);
        }
    }
}
