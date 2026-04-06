namespace Mail2SNMP.Models.Entities;

/// <summary>
/// Generic key/value setting store for runtime state (e.g. last update notification version).
/// </summary>
public class Setting
{
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public const string LastNotifiedUpdateVersion = "update.last_notified_version";
}
