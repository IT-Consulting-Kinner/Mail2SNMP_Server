namespace Mail2SNMP.Models.Enums;

/// <summary>
/// Specifies the authentication protocol used for SNMPv3 communication.
/// </summary>
public enum AuthProtocol
{
    /// <summary>No authentication protocol. Used when SNMPv3 security level does not require authentication.</summary>
    None,

    /// <summary>HMAC-MD5-96 authentication protocol.</summary>
    MD5,

    /// <summary>HMAC-SHA-96 authentication protocol.</summary>
    SHA,

    /// <summary>HMAC-SHA-256 authentication protocol, providing stronger security than SHA.</summary>
    SHA256,

    /// <summary>HMAC-SHA-512 authentication protocol, providing the strongest available authentication.</summary>
    SHA512
}
