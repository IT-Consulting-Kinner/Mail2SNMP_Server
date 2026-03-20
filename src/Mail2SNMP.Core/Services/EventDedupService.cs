using System.Security.Cryptography;
using System.Text;

namespace Mail2SNMP.Core.Services;

/// <summary>
/// Generates deterministic SHA-256 hash keys for event deduplication.
/// Keys are derived from message identifiers and mailbox context so that
/// the same email processed more than once produces an identical key.
/// </summary>
public static class EventDedupKeyGenerator
{
    /// <summary>
    /// Generates a deduplication key from the email Message-ID header and the mailbox identifier.
    /// </summary>
    /// <param name="messageId">The RFC 2822 Message-ID of the email.</param>
    /// <param name="mailboxId">The numeric identifier of the monitored mailbox.</param>
    /// <returns>A lowercase hexadecimal SHA-256 hash string.</returns>
    public static string Generate(string messageId, int mailboxId)
    {
        var input = $"{messageId}:{mailboxId}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Generates a fallback deduplication key when the Message-ID is unavailable.
    /// The key is built from the subject, sender, receive time (truncated to the minute), and mailbox identifier.
    /// </summary>
    /// <param name="subject">The email subject line, or <c>null</c> if absent.</param>
    /// <param name="from">The sender address, or <c>null</c> if absent.</param>
    /// <param name="receivedUtc">The UTC date and time the email was received (truncated to the minute internally).</param>
    /// <param name="mailboxId">The numeric identifier of the monitored mailbox.</param>
    /// <returns>A lowercase hexadecimal SHA-256 hash string.</returns>
    public static string GenerateFallback(string? subject, string? from, DateTime receivedUtc, int mailboxId)
    {
        var truncatedTime = new DateTime(receivedUtc.Year, receivedUtc.Month, receivedUtc.Day,
            receivedUtc.Hour, receivedUtc.Minute, 0, DateTimeKind.Utc);
        var input = $"{subject}:{from}:{truncatedTime:O}:{mailboxId}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
