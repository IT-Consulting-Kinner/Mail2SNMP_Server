using System.ComponentModel.DataAnnotations;

namespace Mail2SNMP.Models.Entities;

/// <summary>
/// Scheduled maintenance window that suppresses event creation during planned downtime.
/// </summary>
public class MaintenanceWindow
{
    /// <summary>Surrogate primary key. Identity column assigned by the database.</summary>
    public int Id { get; set; }

    /// <summary>Operator-facing window name shown in the management UI. Required and non-empty.</summary>
    [Required(ErrorMessage = "Name is required.")]
    public string Name { get; set; } = string.Empty;

    /// <summary>UTC start of the suppression window (inclusive). Required.</summary>
    [Required(ErrorMessage = "Start time is required.")]
    public DateTime StartUtc { get; set; }

    /// <summary>UTC end of the suppression window (exclusive). Required; should be after <see cref="StartUtc"/>.</summary>
    [Required(ErrorMessage = "End time is required.")]
    public DateTime EndUtc { get; set; }

    /// <summary>
    /// What the window applies to. Defaults to <c>"All"</c> (suppress everything); other values scope
    /// suppression to a subset (e.g. a particular job or mailbox identifier). Required.
    /// </summary>
    [Required(ErrorMessage = "Scope is required.")]
    public string Scope { get; set; } = "All";

    /// <summary>
    /// Optional cron expression describing a recurring window. When set, <see cref="StartUtc"/>/<see cref="EndUtc"/>
    /// define the duration of each occurrence; <c>null</c> means the window is a single one-off period.
    /// </summary>
    public string? RecurringCron { get; set; }

    /// <summary>Identity (username) of the operator who created the window. Required, used for audit.</summary>
    [Required(ErrorMessage = "Created by is required.")]
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the window was created. Defaults to <see cref="DateTime.UtcNow"/> at construction.</summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Whether the window is enforced. Defaults to <c>true</c>; inactive windows suppress nothing.</summary>
    public bool IsActive { get; set; } = true;
}
