using System.ComponentModel.DataAnnotations;

namespace Mail2SNMP.Models.Entities;

/// <summary>
/// G6: API key for header-based authentication on the REST API.
/// The plaintext key is shown to the operator EXACTLY ONCE on creation. The DB only
/// stores a SHA-256 hash so a database leak does not expose live keys. Each key carries
/// one or more scopes (comma-separated) that gate which endpoints it may call.
/// </summary>
public class ApiKey
{
    public int Id { get; set; }

    /// <summary>Friendly name shown in the management UI.</summary>
    [Required]
    public string Name { get; set; } = string.Empty;

    /// <summary>SHA-256 hex-encoded hash of the plaintext key. NEVER stores the plaintext.</summary>
    [Required]
    public string KeyHash { get; set; } = string.Empty;

    /// <summary>
    /// First 8 characters of the plaintext key. Shown in the UI for identification
    /// (the rest is irrecoverable). Safe to display because it has insufficient entropy
    /// to brute-force the full key.
    /// </summary>
    public string KeyPrefix { get; set; } = string.Empty;

    /// <summary>
    /// Comma-separated scope list. Currently recognised scopes:
    /// <c>read</c> (GET endpoints), <c>write</c> (POST/PUT/DELETE), <c>admin</c> (full).
    /// </summary>
    public string Scopes { get; set; } = "read";

    public bool IsActive { get; set; } = true;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresUtc { get; set; }
    public DateTime? LastUsedUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
}
