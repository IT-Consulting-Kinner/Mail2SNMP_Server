using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Mail2SNMP.Models.Entities;

/// <summary>
/// A polling job that links a mailbox to a parsing rule and one or more notification targets.
/// Targets are assigned per-job via the JobSnmpTargets and JobWebhookTargets join tables.
/// </summary>
public class Job
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Name is required.")]
    public string Name { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "Please select a mailbox.")]
    public int MailboxId { get; set; }

    public Mailbox Mailbox { get; set; } = null!;

    [Range(1, int.MaxValue, ErrorMessage = "Please select a rule.")]
    public int RuleId { get; set; }

    public Rule Rule { get; set; } = null!;

    /// <summary>
    /// Backward-compatible computed property: derives channel names from the assigned targets.
    /// Not mapped to the database — the join tables are the source of truth.
    /// </summary>
    [NotMapped]
    public string Channels
    {
        get
        {
            var parts = new List<string>();
            if (JobSnmpTargets.Count > 0) parts.Add("snmp");
            if (JobWebhookTargets.Count > 0) parts.Add("webhook");
            return parts.Count > 0 ? string.Join(",", parts) : "none";
        }
    }

    public string? TrapTemplate { get; set; }
    public string? WebhookTemplate { get; set; }
    public string? OidMapping { get; set; }

    [Range(1, 10000, ErrorMessage = "Max events per hour must be between 1 and 10,000.")]
    public int MaxEventsPerHour { get; set; } = 50;

    [Range(1, 100000, ErrorMessage = "Max active events must be between 1 and 100,000.")]
    public int MaxActiveEvents { get; set; } = 200;

    [Range(0, 1440, ErrorMessage = "Dedup window must be between 0 and 1440 minutes.")]
    public int DedupWindowMinutes { get; set; } = 30;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<Schedule> Schedules { get; set; } = new List<Schedule>();
    public ICollection<Event> Events { get; set; } = new List<Event>();

    // Per-job target assignments (many-to-many via join tables)
    public ICollection<JobSnmpTarget> JobSnmpTargets { get; set; } = new List<JobSnmpTarget>();
    public ICollection<JobWebhookTarget> JobWebhookTargets { get; set; } = new List<JobWebhookTarget>();
}
