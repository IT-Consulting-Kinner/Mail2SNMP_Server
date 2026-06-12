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
    /// <summary>Surrogate primary key. Identity column assigned by the database.</summary>
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

    /// <summary>Whether the key is accepted for authentication. Defaults to <c>true</c>; set <c>false</c> to revoke without deleting.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>UTC timestamp when the key was created. Defaults to <see cref="DateTime.UtcNow"/> at construction.</summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>UTC time after which the key is rejected, or <c>null</c> for a key that never expires.</summary>
    public DateTime? ExpiresUtc { get; set; }

    /// <summary>UTC time the key was last successfully used to authenticate, or <c>null</c> if never used.</summary>
    public DateTime? LastUsedUtc { get; set; }

    /// <summary>Identity (username) of the operator who created the key. Retained for audit.</summary>
    public string CreatedBy { get; set; } = string.Empty;
}
