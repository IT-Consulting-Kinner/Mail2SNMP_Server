using System.ComponentModel.DataAnnotations;

namespace Mail2SNMP.Models.Entities;

/// <summary>
/// Scheduled maintenance window that suppresses event creation during planned downtime.
/// </summary>
public class MaintenanceWindow
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Name is required.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Start time is required.")]
    public DateTime StartUtc { get; set; }

    [Required(ErrorMessage = "End time is required.")]
    public DateTime EndUtc { get; set; }

    [Required(ErrorMessage = "Scope is required.")]
    public string Scope { get; set; } = "All";

    public string? RecurringCron { get; set; }

    [Required(ErrorMessage = "Created by is required.")]
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
}
