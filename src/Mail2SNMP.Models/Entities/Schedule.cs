using System.ComponentModel.DataAnnotations;

namespace Mail2SNMP.Models.Entities;

/// <summary>
/// Polling schedule that drives a job at a configurable interval via the Quartz scheduler.
/// </summary>
public class Schedule
{
    /// <summary>Surrogate primary key. Identity column assigned by the database.</summary>
    public int Id { get; set; }

    /// <summary>Operator-facing schedule name shown in the management UI. Required and non-empty.</summary>
    [Required(ErrorMessage = "Name is required.")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// FK to the <see cref="Job"/> this schedule polls. Must be a positive value (a job must be selected);
    /// the range validation doubles as a "required" check in the UI.
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "Please select a job.")]
    public int JobId { get; set; }

    /// <summary>Navigation to the owning <see cref="Job"/> identified by <see cref="JobId"/>.</summary>
    public Job Job { get; set; } = null!;

    /// <summary>
    /// Polling interval in minutes between job runs. Must be between 1 and 1440 (one day); defaults to 5.
    /// </summary>
    [Range(1, 1440, ErrorMessage = "Interval must be between 1 and 1440 minutes.")]
    public int IntervalMinutes { get; set; } = 5;

    /// <summary>Whether the schedule is armed. Defaults to <c>true</c>; inactive schedules are not fired by Quartz.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>UTC timestamp when the schedule was created. Defaults to <see cref="DateTime.UtcNow"/> at construction.</summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>UTC time of the next scheduled run, or <c>null</c> if not yet computed or inactive.</summary>
    public DateTime? NextRunUtc { get; set; }

    /// <summary>UTC time of the most recent run, or <c>null</c> if the schedule has never fired.</summary>
    public DateTime? LastRunUtc { get; set; }

    /// <summary>
    /// SQL Server <c>rowversion</c> concurrency token. Updated automatically on every save and
    /// used for optimistic concurrency checks; do not set manually.
    /// </summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
