namespace Mail2SNMP.Models.Entities;

/// <summary>
/// Generic key/value setting store for runtime state (e.g. last update notification version).
/// </summary>
public class Setting
{
    /// <summary>Unique setting key; serves as the primary key. Use namespaced identifiers (e.g. <c>update.last_notified_version</c>).</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Stored value as a string, or <c>null</c> if unset. Callers interpret/parse the value per key.</summary>
    public string? Value { get; set; }

    /// <summary>UTC time the value was last written. Defaults to <see cref="DateTime.UtcNow"/> at construction.</summary>
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Well-known <see cref="Key"/> recording the latest product version the operator was already notified about.</summary>
    public const string LastNotifiedUpdateVersion = "update.last_notified_version";
}
