using System.ComponentModel.DataAnnotations;

namespace Mail2SNMP.Models.Entities;

/// <summary>
/// Polling schedule that drives a job at a configurable interval via the Quartz scheduler.
/// </summary>
public class Schedule
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Name is required.")]
    public string Name { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "Please select a job.")]
    public int JobId { get; set; }

    public Job Job { get; set; } = null!;

    [Range(1, 1440, ErrorMessage = "Interval must be between 1 and 1440 minutes.")]
    public int IntervalMinutes { get; set; } = 5;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? NextRunUtc { get; set; }
    public DateTime? LastRunUtc { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
