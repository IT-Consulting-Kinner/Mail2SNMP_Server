using System.ComponentModel.DataAnnotations;
using Mail2SNMP.Models.Enums;

namespace Mail2SNMP.Models.Entities;

/// <summary>
/// SNMP trap target configuration supporting v1, v2c, and v3 (AuthPriv) protocols.
/// </summary>
public class SnmpTarget : IValidatableObject
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Name is required.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Host is required.")]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535, ErrorMessage = "Port must be between 1 and 65535.")]
    public int Port { get; set; } = 162;

    public SnmpVersion Version { get; set; } = SnmpVersion.V2c;

    // v1/v2c
    public string? CommunityString { get; set; }

    // v3
    public string? SecurityName { get; set; }
    public AuthProtocol AuthProtocol { get; set; } = AuthProtocol.None;
    public string? EncryptedAuthPassword { get; set; }
    public PrivProtocol PrivProtocol { get; set; } = PrivProtocol.None;
    public string? EncryptedPrivPassword { get; set; }
    public string? EngineId { get; set; }

    public string? EnterpriseTrapOid { get; set; }

    [Range(1, 10000, ErrorMessage = "Max traps per minute must be between 1 and 10,000.")]
    public int MaxTrapsPerMinute { get; set; } = 100;

    /// <summary>
    /// When true, this target receives periodic KeepAlive traps from the worker.
    /// </summary>
    public bool SendKeepAlive { get; set; } = false;

    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Version == SnmpVersion.V1 || Version == SnmpVersion.V2c)
        {
            if (string.IsNullOrWhiteSpace(CommunityString))
                yield return new ValidationResult("Community string is required for SNMP v1/v2c.", new[] { nameof(CommunityString) });
        }
        else if (Version == SnmpVersion.V3)
        {
            if (string.IsNullOrWhiteSpace(SecurityName))
                yield return new ValidationResult("Security name is required for SNMP v3.", new[] { nameof(SecurityName) });
        }
    }
}
