using Mail2SNMP.Models.Enums;

namespace Mail2SNMP.Models.DTOs;

/// <summary>
/// Context data passed to notification channels containing all information needed to construct a trap or webhook payload.
/// </summary>
public class NotificationContext
{
    public long EventId { get; set; }
    public string JobName { get; set; } = string.Empty;
    public string Mailbox { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public Severity Severity { get; set; }
    public string RuleName { get; set; } = string.Empty;
    public int HitCount { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string? TrapTemplate { get; set; }
    public string? WebhookTemplate { get; set; }
    public string? OidMapping { get; set; }
}

/// <summary>
/// Information about an available product update returned from the update check feed.
/// </summary>
public class UpdateInfo
{
    public string CurrentVersion { get; set; } = string.Empty;
    public string AvailableVersion { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string PublishDate { get; set; } = string.Empty;
}
