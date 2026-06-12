using System.ComponentModel.DataAnnotations;
using Mail2SNMP.Models.Enums;

namespace Mail2SNMP.Models.Entities;

/// <summary>
/// Email parsing rule that matches incoming emails by field (Subject, From, Body, etc.) and match type (Contains, Regex, etc.).
/// </summary>
public class Rule
{
    /// <summary>Surrogate primary key. Identity column assigned by the database.</summary>
    public int Id { get; set; }

    /// <summary>Operator-facing rule name shown in the management UI. Required and non-empty.</summary>
    [Required(ErrorMessage = "Name is required.")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Which part of the incoming email this rule inspects (subject, sender, body, header, etc.).
    /// See <see cref="RuleFieldType"/>; for <see cref="RuleFieldType.Header"/> the specific header is encoded in <see cref="Criteria"/>.
    /// </summary>
    public RuleFieldType Field { get; set; }

    /// <summary>
    /// How <see cref="Criteria"/> is compared against the selected <see cref="Field"/>
    /// (substring, exact equality, regular expression, prefix or suffix). See <see cref="RuleMatchType"/>.
    /// </summary>
    public RuleMatchType MatchType { get; set; }

    /// <summary>
    /// The pattern or literal compared against the email field. Interpretation depends on
    /// <see cref="MatchType"/> (e.g. a regular expression when <see cref="RuleMatchType.Regex"/>). Required and non-empty.
    /// </summary>
    [Required(ErrorMessage = "Criteria is required.")]
    public string Criteria { get; set; } = string.Empty;

    /// <summary>
    /// Severity stamped onto events generated when this rule matches. Defaults to
    /// <see cref="Severity.Warning"/>. See <see cref="Severity"/>.
    /// </summary>
    public Severity Severity { get; set; } = Severity.Warning;

    /// <summary>Whether the rule participates in matching. Defaults to <c>true</c>; inactive rules are skipped.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Evaluation order relative to other rules. Lower values are evaluated first; used to
    /// resolve which rule wins when several could match the same email.
    /// </summary>
    public int Priority { get; set; }

    /// <summary>UTC timestamp when the rule was created. Defaults to <see cref="DateTime.UtcNow"/> at construction.</summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// G3: Per-rule deduplication window in minutes. When greater than 0, the EventService
    /// suppresses creation of a new event if an event with the same JobId+RuleId+Subject was
    /// created within this many minutes. When 0 or null, the global
    /// <c>Events:DefaultDedupWindowMinutes</c> is used.
    /// </summary>
    public int? DedupWindowMinutes { get; set; }

    /// <summary>
    /// SQL Server <c>rowversion</c> concurrency token. Updated automatically on every save and
    /// used for optimistic concurrency checks; do not set manually.
    /// </summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Jobs that reference this rule. Many-to-many navigation; a rule may be shared by several jobs
    /// and a job may apply several rules.
    /// </summary>
    public ICollection<Job> Jobs { get; set; } = new List<Job>();
}
