using System.ComponentModel.DataAnnotations;
using Mail2SNMP.Models.Enums;

namespace Mail2SNMP.Models.Entities;

/// <summary>
/// Email parsing rule that matches incoming emails by field (Subject, From, Body, etc.) and match type (Contains, Regex, etc.).
/// </summary>
public class Rule
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Name is required.")]
    public string Name { get; set; } = string.Empty;

    public RuleFieldType Field { get; set; }
    public RuleMatchType MatchType { get; set; }

    [Required(ErrorMessage = "Criteria is required.")]
    public string Criteria { get; set; } = string.Empty;
    public Severity Severity { get; set; } = Severity.Warning;
    public bool IsActive { get; set; } = true;
    public int Priority { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// G3: Per-rule deduplication window in minutes. When greater than 0, the EventService
    /// suppresses creation of a new event if an event with the same JobId+RuleId+Subject was
    /// created within this many minutes. When 0 or null, the global
    /// <c>Events:DefaultDedupWindowMinutes</c> is used.
    /// </summary>
    public int? DedupWindowMinutes { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<Job> Jobs { get; set; } = new List<Job>();
}
