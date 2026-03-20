using Mail2SNMP.Models.DTOs;
using Mail2SNMP.Models.Enums;

namespace Mail2SNMP.Core.Interfaces;

/// <summary>
/// Provides license information and edition-based feature gating.
/// Community edition has limited mailboxes, jobs, and worker instances.
/// Enterprise edition unlocks SNMP v3, HMAC signing, OIDC/SSO, dead-letter
/// handling, diagnostics export, and removes all limits.
/// </summary>
public interface ILicenseProvider
{
    /// <summary>
    /// Gets the currently loaded license information.
    /// </summary>
    LicenseInfo Current { get; }

    /// <summary>
    /// Returns true if the current license is Enterprise edition.
    /// </summary>
    bool IsEnterprise();

    /// <summary>
    /// Checks whether a specific named feature is enabled in the license.
    /// </summary>
    bool HasFeature(string featureName);

    /// <summary>
    /// Returns the numeric limit for a named resource (e.g., maxmailboxes, maxjobs).
    /// </summary>
    int GetLimit(string limitName);

    /// <summary>
    /// Reloads the license from disk or environment variable.
    /// </summary>
    Task ReloadAsync(CancellationToken cancellationToken = default);
}
