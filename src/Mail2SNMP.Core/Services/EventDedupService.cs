using System.Security.Cryptography;
using System.Text;

namespace Mail2SNMP.Core.Services;

/// <summary>
/// Generates deterministic SHA-256 keys for event deduplication.
/// </summary>
/// <remarks>
/// H-3: the key is derived from what makes two alerts <b>the same alert</b> — the
/// subject and sender for a given job — and deliberately NOT from the Message-ID or
/// the receive time.
///
/// The previous design keyed on the RFC 5322 Message-ID whenever one was present
/// (nearly always), with a subject+sender+minute fallback otherwise. Both are unique
/// per message: two successive "Disk full on srv01" mails from a monitoring system
/// carry different Message-IDs and arrive at different times, so they produced
/// different keys, a brand-new event each time and a HitCount that never left 1.
/// That made <c>Job.DedupWindowMinutes</c> — the whole point of the feature — inert,
/// and left the <c>Deduplicated</c> disposition and the <c>{{HitCount}}</c> template
/// placeholder unreachable.
///
/// Exact re-ingestion of the very same message is a different concern and is already
/// handled upstream by the <c>ProcessedMails</c> claim, so the dedup key does not need
/// to cover it.
/// </remarks>
public static class EventDedupKeyGenerator
{
    /// <summary>
    /// Generates the content-based deduplication key for an alert.
    /// </summary>
    /// <remarks>
    /// Subject and sender are normalized (trimmed, case-folded, inner whitespace
    /// collapsed) so that cosmetic differences — a re-wrapped subject, a differently
    /// cased sender — still collapse into one event.
    /// </remarks>
    /// <param name="subject">The email subject line, or <c>null</c> if absent.</param>
    /// <param name="from">The sender address, or <c>null</c> if absent.</param>
    /// <param name="jobId">The job the event belongs to; keys never collide across jobs.</param>
    /// <returns>A lowercase hexadecimal SHA-256 hash string (64 characters).</returns>
    public static string Generate(string? subject, string? from, int jobId)
    {
        var input = $"{Normalize(subject)}:{Normalize(from)}:{jobId}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Collapses insignificant formatting differences so equivalent alerts hash alike:
    /// trims, lowercases invariantly, and reduces every run of whitespace to one space.
    /// </summary>
    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var sb = new StringBuilder(value.Length);
        var lastWasSpace = false;
        foreach (var ch in value.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace) sb.Append(' ');
                lastWasSpace = true;
            }
            else
            {
                sb.Append(char.ToLowerInvariant(ch));
                lastWasSpace = false;
            }
        }
        return sb.ToString();
    }
}
