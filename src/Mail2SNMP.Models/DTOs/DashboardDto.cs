namespace Mail2SNMP.Models.DTOs;

/// <summary>
/// Aggregated dashboard statistics including active resource counts, event totals, and system health status.
/// </summary>
public class DashboardDto
{
    public int ActiveMailboxes { get; set; }
    public int ActiveJobs { get; set; }
    public int ActiveSchedules { get; set; }
    public int OpenEvents { get; set; }
    public int PendingDeadLetters { get; set; }
    public bool MaintenanceActive { get; set; }
    public string? MaintenanceWindowName { get; set; }
    public bool IsHealthy { get; set; }
    public string LicenseEdition { get; set; } = "Community";
}
