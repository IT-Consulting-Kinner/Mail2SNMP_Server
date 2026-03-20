namespace Mail2SNMP.Models.Entities;

/// <summary>
/// Deduplication tracking entry linking a SHA-256 hash key to an event within a job's dedup window.
/// </summary>
public class EventDedup
{
    public long Id { get; set; }
    public string DedupKeyHash { get; set; } = string.Empty;
    public int JobId { get; set; }
    public Job Job { get; set; } = null!;
    public long EventId { get; set; }
    public Event Event { get; set; } = null!;
    public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
}
