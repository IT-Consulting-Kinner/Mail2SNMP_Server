namespace Mail2SNMP.Models.Entities;

/// <summary>
/// Deduplication tracking entry linking a SHA-256 hash key to an event within a job's dedup window.
/// </summary>
public class EventDedup
{
    /// <summary>Surrogate primary key. Identity column assigned by the database.</summary>
    public long Id { get; set; }

    /// <summary>
    /// SHA-256 hex digest of the deduplication key (typically derived from JobId, RuleId and Subject).
    /// Used to look up whether a matching event already exists within the dedup window.
    /// </summary>
    public string DedupKeyHash { get; set; } = string.Empty;

    /// <summary>FK to the <see cref="Job"/> this dedup entry belongs to; dedup keys are scoped per job.</summary>
    public int JobId { get; set; }

    /// <summary>Navigation to the owning <see cref="Job"/> identified by <see cref="JobId"/>.</summary>
    public Job Job { get; set; } = null!;

    /// <summary>FK to the <see cref="Event"/> that this key first produced and that duplicates are folded into.</summary>
    public long EventId { get; set; }

    /// <summary>Navigation to the <see cref="Event"/> identified by <see cref="EventId"/>.</summary>
    public Event Event { get; set; } = null!;

    /// <summary>UTC time the key was first observed. Defaults to <see cref="DateTime.UtcNow"/> at construction.</summary>
    public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// UTC time the key was most recently observed; advanced on each duplicate hit and used to evaluate
    /// the rolling dedup window. Defaults to <see cref="DateTime.UtcNow"/> at construction.
    /// </summary>
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
}
