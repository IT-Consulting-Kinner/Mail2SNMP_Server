namespace Mail2SNMP.Models.Enums;

/// <summary>
/// Specifies the SNMP protocol version used for trap delivery.
/// </summary>
public enum SnmpVersion
{
    /// <summary>SNMP version 1, the original protocol with basic community-string security.</summary>
    V1,

    /// <summary>SNMP version 2c, adding improved error handling and bulk operations with community-string security.</summary>
    V2c,

    /// <summary>SNMP version 3, providing authentication, encryption, and access control.</summary>
    V3
}
