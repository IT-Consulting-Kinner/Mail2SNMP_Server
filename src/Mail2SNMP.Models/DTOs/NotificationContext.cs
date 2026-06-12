using Mail2SNMP.Models.Enums;

namespace Mail2SNMP.Models.DTOs;

/// <summary>
/// Context data passed to notification channels containing all information needed to construct a trap or webhook payload.
/// </summary>
public class NotificationContext
{
    /// <summary>
    /// Id of the <see cref="Entities.Event"/> that triggered this notification. Also used as the dedup key so a
    /// retried delivery to the same target is suppressed. Available to templates as the <c>{{EventId}}</c> placeholder.
    /// </summary>
    public long EventId { get; set; }

    /// <summary>Name of the job that produced the event. Available to templates as <c>{{JobName}}</c>.</summary>
    public string JobName { get; set; } = string.Empty;

    /// <summary>Display name of the source mailbox the matched email arrived in. Available to templates as <c>{{Mailbox}}</c>.</summary>
    public string Mailbox { get; set; } = string.Empty;

    /// <summary>Sender (From) address of the matched email. Available to templates as <c>{{From}}</c>; truncated to a safe maximum length when rendered.</summary>
    public string From { get; set; } = string.Empty;

    /// <summary>Subject line of the matched email. Available to templates as <c>{{Subject}}</c>; truncated to a safe maximum length when rendered.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>
    /// Severity assigned by the matching rule (see <see cref="Severity"/>). Available to templates as
    /// <c>{{Severity}}</c>, rendered as the enum name (<c>Information</c>, <c>Warning</c>, <c>Error</c>, <c>Critical</c>).
    /// </summary>
    public Severity Severity { get; set; }

    /// <summary>Name of the rule that matched the email. Available to templates as <c>{{RuleName}}</c>.</summary>
    public string RuleName { get; set; } = string.Empty;

    /// <summary>
    /// Number of times this event has matched within its deduplication window (starts at 1, incremented on each
    /// suppressed duplicate). Available to templates as <c>{{HitCount}}</c>.
    /// </summary>
    public int HitCount { get; set; }

    /// <summary>
    /// UTC time of the event. Available to templates as <c>{{TimestampUtc}}</c>, rendered in ISO-8601 round-trip
    /// (<c>"O"</c>) format.
    /// </summary>
    public DateTime TimestampUtc { get; set; }

    /// <summary>
    /// Effective SNMP trap body template for this notification (per-job override resolved from the job/target),
    /// or <see langword="null"/> to fall back to the target's own template. Not itself a placeholder.
    /// </summary>
    public string? TrapTemplate { get; set; }

    /// <summary>
    /// Effective webhook payload template for this notification (per-job override resolved from the job/target),
    /// or <see langword="null"/> to fall back to the target's own template. Not itself a placeholder.
    /// </summary>
    public string? WebhookTemplate { get; set; }

    /// <summary>
    /// Optional JSON mapping of SNMP OIDs to placeholder values used to construct trap varbinds, or
    /// <see langword="null"/> for defaults. Not itself a placeholder.
    /// </summary>
    public string? OidMapping { get; set; }
}

/// <summary>
/// Information about an available product update returned from the update check feed.
/// </summary>
public class UpdateInfo
{
    /// <summary>Version of the product currently running, as reported locally (e.g. <c>1.0.1</c>).</summary>
    public string CurrentVersion { get; set; } = string.Empty;

    /// <summary>Latest version advertised by the update feed. Equal to <see cref="CurrentVersion"/> when no update is available.</summary>
    public string AvailableVersion { get; set; } = string.Empty;

    /// <summary>URL from which the available release can be downloaded.</summary>
    public string DownloadUrl { get; set; } = string.Empty;

    /// <summary>Publication date of the available release, as a string in the format supplied by the feed.</summary>
    public string PublishDate { get; set; } = string.Empty;
}
