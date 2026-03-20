namespace Mail2SNMP.Models.Enums;

/// <summary>
/// Represents the outcome of an audited operation.
/// </summary>
public enum AuditResult
{
    /// <summary>The operation completed successfully.</summary>
    Success,

    /// <summary>The operation failed due to an error or unexpected condition.</summary>
    Failure,

    /// <summary>The operation was denied due to insufficient permissions or policy rules.</summary>
    Denied
}
