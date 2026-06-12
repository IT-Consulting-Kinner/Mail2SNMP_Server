using Mail2SNMP.Models.Enums;

namespace Mail2SNMP.Models.DTOs;

/// <summary>
/// License information including edition, limits, features, and expiration.
/// </summary>
public class LicenseInfo
{
    /// <summary>Unique identifier of the license record/key. Empty string for the implicit default (unlicensed) Community state.</summary>
    public string LicenseId { get; set; } = string.Empty;

    /// <summary>Name of the customer the license is issued to. Empty when running unlicensed.</summary>
    public string CustomerName { get; set; } = string.Empty;

    /// <summary>
    /// Product edition granted by the license (see <see cref="LicenseEdition"/>). Defaults to
    /// <see cref="LicenseEdition.Community"/>, which enables core functionality only.
    /// </summary>
    public LicenseEdition Edition { get; set; } = LicenseEdition.Community;

    /// <summary>UTC expiry timestamp of the license, or <see langword="null"/> for a perpetual/non-expiring license (including the Community default).</summary>
    public DateTime? ExpiresUtc { get; set; }

    /// <summary>Maximum number of mailboxes permitted under this license. Defaults to 3 for the Community edition.</summary>
    public int MaxMailboxes { get; set; } = 3;

    /// <summary>Maximum number of jobs permitted under this license. Defaults to 5 for the Community edition.</summary>
    public int MaxJobs { get; set; } = 5;

    /// <summary>Maximum number of concurrent worker instances permitted under this license. Defaults to 1 for the Community edition.</summary>
    public int MaxWorkerInstances { get; set; } = 1;

    /// <summary>Names of optional/premium feature flags enabled by this license. Empty array when no add-on features are granted.</summary>
    public string[] Features { get; set; } = Array.Empty<string>();
}
