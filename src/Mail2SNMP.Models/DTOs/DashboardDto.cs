namespace Mail2SNMP.Models.DTOs;

/// <summary>
/// Aggregated dashboard statistics including active resource counts, event totals, and system health status.
/// </summary>
public class DashboardDto
{
    /// <summary>Count of mailboxes currently marked active (eligible for polling).</summary>
    public int ActiveMailboxes { get; set; }

    /// <summary>Count of jobs currently marked active (enabled for processing).</summary>
    public int ActiveJobs { get; set; }

    /// <summary>Count of schedules currently marked active (governing when their jobs run).</summary>
    public int ActiveSchedules { get; set; }

    /// <summary>Number of events that are still open (not yet resolved) and therefore require attention.</summary>
    public int OpenEvents { get; set; }

    /// <summary>Number of dead-letter entries awaiting reprocessing or manual intervention after delivery failures.</summary>
    public int PendingDeadLetters { get; set; }

    /// <summary>
    /// When <see langword="true"/>, a maintenance window is currently in effect and notification delivery is
    /// suppressed/paused. See <see cref="MaintenanceWindowName"/> for which one.
    /// </summary>
    public bool MaintenanceActive { get; set; }

    /// <summary>Name of the active maintenance window when <see cref="MaintenanceActive"/> is <see langword="true"/>; otherwise <see langword="null"/>.</summary>
    public string? MaintenanceWindowName { get; set; }

    /// <summary>Overall system health flag. <see langword="true"/> when core subsystems are operating normally; <see langword="false"/> signals a degraded state surfaced on the dashboard.</summary>
    public bool IsHealthy { get; set; }

    /// <summary>
    /// Current license edition name shown on the dashboard, defaulting to <c>Community</c>. Mirrors the
    /// <see cref="Enums.LicenseEdition"/> value as a string (<c>Community</c> or <c>Enterprise</c>).
    /// </summary>
    public string LicenseEdition { get; set; } = "Community";
}
