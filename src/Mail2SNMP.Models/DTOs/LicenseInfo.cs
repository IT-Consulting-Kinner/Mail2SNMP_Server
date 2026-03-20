using Mail2SNMP.Models.Enums;

namespace Mail2SNMP.Models.DTOs;

/// <summary>
/// License information including edition, limits, features, and expiration.
/// </summary>
public class LicenseInfo
{
    public string LicenseId { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public LicenseEdition Edition { get; set; } = LicenseEdition.Community;
    public DateTime? ExpiresUtc { get; set; }
    public int MaxMailboxes { get; set; } = 3;
    public int MaxJobs { get; set; } = 5;
    public int MaxWorkerInstances { get; set; } = 1;
    public string[] Features { get; set; } = Array.Empty<string>();
}
