using System.ComponentModel.DataAnnotations;
using Mail2SNMP.Models.Enums;

namespace Mail2SNMP.Models.Entities;

/// <summary>
/// SNMP trap target configuration supporting v1, v2c, and v3 (AuthPriv) protocols.
/// </summary>
public class SnmpTarget : IValidatableObject
{
    /// <summary>Surrogate primary key. Database-generated identity; zero on a not-yet-persisted instance.</summary>
    public int Id { get; set; }

    /// <summary>Operator-facing display name for the target. Required, 1–200 characters.</summary>
    [Required(ErrorMessage = "Name is required.")]
    [StringLength(200, MinimumLength = 1, ErrorMessage = "Name must be between 1 and 200 characters.")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Hostname or IP address of the SNMP trap receiver. Required, 1–500 characters.</summary>
    [Required(ErrorMessage = "Host is required.")]
    [StringLength(500, MinimumLength = 1, ErrorMessage = "Host must be between 1 and 500 characters.")]
    public string Host { get; set; } = string.Empty;

    /// <summary>UDP port of the trap receiver. Range 1–65535; defaults to 162, the IANA SNMP-trap port.</summary>
    [Range(1, 65535, ErrorMessage = "Port must be between 1 and 65535.")]
    public int Port { get; set; } = 162;

    /// <summary>
    /// SNMP protocol version. Determines which credential fields apply: v1/v2c use
    /// <see cref="EncryptedCommunityString"/>; v3 uses the security-name and auth/priv fields.
    /// Defaults to <see cref="SnmpVersion.V2c"/>. See <see cref="Validate"/> for the enforced rules.
    /// </summary>
    public SnmpVersion Version { get; set; } = SnmpVersion.V2c;

    // R2: Encrypted community string (v1/v2c). Stored as AES-GCM ciphertext via the
    // J1 EnsureEncrypted funnel in SnmpTargetService.Create/UpdateAsync, exactly like
    // the v3 auth/priv passwords below. The Razor edit form binds to a separate
    // plaintext property and only assigns when the user supplied a new value, so the
    // ciphertext at rest is never round-tripped through the UI. The cap is generous
    // (1024 chars) because AES-GCM ciphertext expands the input.
    /// <summary>
    /// AES-GCM <b>ciphertext</b> of the SNMP v1/v2c community string (never plaintext at rest).
    /// Required for v1/v2c per <see cref="Validate"/>; ignored for v3. The 1024-char cap is deliberately
    /// generous because AES-GCM encoding expands the input. <c>null</c> when no community string is set.
    /// </summary>
    [StringLength(1024, ErrorMessage = "Encrypted community string must not exceed 1024 characters.")]
    public string? EncryptedCommunityString { get; set; }

    // v3

    /// <summary>
    /// SNMP v3 USM security (user) name. Required for v3 per <see cref="Validate"/>; ignored for v1/v2c.
    /// Plaintext, max 200 characters. <c>null</c> when not using v3.
    /// </summary>
    [StringLength(200, ErrorMessage = "Security name must not exceed 200 characters.")]
    public string? SecurityName { get; set; }

    /// <summary>SNMP v3 authentication algorithm. Defaults to <see cref="AuthProtocol.None"/> (no authentication).</summary>
    public AuthProtocol AuthProtocol { get; set; } = AuthProtocol.None;

    /// <summary>
    /// AES-GCM <b>ciphertext</b> of the SNMP v3 authentication password (never plaintext at rest).
    /// <c>null</c> when <see cref="AuthProtocol"/> is <see cref="AuthProtocol.None"/> or no password is set.
    /// </summary>
    public string? EncryptedAuthPassword { get; set; }

    /// <summary>SNMP v3 privacy (encryption) algorithm. Defaults to <see cref="PrivProtocol.None"/> (no encryption).</summary>
    public PrivProtocol PrivProtocol { get; set; } = PrivProtocol.None;

    /// <summary>
    /// AES-GCM <b>ciphertext</b> of the SNMP v3 privacy password (never plaintext at rest).
    /// <c>null</c> when <see cref="PrivProtocol"/> is <see cref="PrivProtocol.None"/> or no password is set.
    /// </summary>
    public string? EncryptedPrivPassword { get; set; }

    /// <summary>
    /// SNMP v3 authoritative engine ID (hex string). When <c>null</c> the engine ID is discovered
    /// from the receiver. Max 200 characters.
    /// </summary>
    [StringLength(200, ErrorMessage = "Engine ID must not exceed 200 characters.")]
    public string? EngineId { get; set; }

    /// <summary>
    /// Enterprise-specific trap OID in dotted-decimal notation (e.g. <c>1.3.6.1.4.1.61376.1.2.0.1</c>).
    /// When <c>null</c> or empty a default OID is used. Validated against a dotted-decimal pattern; max 500 characters.
    /// </summary>
    [StringLength(500, ErrorMessage = "Enterprise trap OID must not exceed 500 characters.")]
    [RegularExpression(@"^(\d+\.)+\d+$|^$", ErrorMessage = "Enterprise trap OID must be a dotted-decimal OID (e.g. 1.3.6.1.4.1.61376.1.2.0.1).")]
    public string? EnterpriseTrapOid { get; set; }

    /// <summary>
    /// Per-target rate limit: maximum traps emitted to this receiver within a rolling one-minute window.
    /// Range 1–10,000; defaults to 100.
    /// </summary>
    [Range(1, 10000, ErrorMessage = "Max traps per minute must be between 1 and 10,000.")]
    public int MaxTrapsPerMinute { get; set; } = 100;

    /// <summary>
    /// When true, this target receives periodic KeepAlive traps from the worker.
    /// </summary>
    public bool SendKeepAlive { get; set; } = false;

    /// <summary>Whether the target is eligible to receive traps. Inactive targets are skipped by the worker. Defaults to <c>true</c>.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>UTC timestamp recorded when the target was created. Defaults to the current UTC time.</summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>EF Core optimistic-concurrency token; updated by the database on each row change to detect lost updates.</summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Cross-field validation enforcing version-dependent credential requirements: v1/v2c require a
    /// community string (<see cref="EncryptedCommunityString"/>), and v3 requires a
    /// <see cref="SecurityName"/>. Invoked by the validation pipeline; yields one
    /// <see cref="ValidationResult"/> per violation.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Version == SnmpVersion.V1 || Version == SnmpVersion.V2c)
        {
            if (string.IsNullOrWhiteSpace(EncryptedCommunityString))
                yield return new ValidationResult("Community string is required for SNMP v1/v2c.", new[] { nameof(EncryptedCommunityString) });
        }
        else if (Version == SnmpVersion.V3)
        {
            if (string.IsNullOrWhiteSpace(SecurityName))
                yield return new ValidationResult("Security name is required for SNMP v3.", new[] { nameof(SecurityName) });
        }
    }
}
