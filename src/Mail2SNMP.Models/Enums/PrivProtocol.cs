namespace Mail2SNMP.Models.Enums;

/// <summary>
/// Specifies the privacy (encryption) protocol used for SNMPv3 communication.
/// </summary>
public enum PrivProtocol
{
    /// <summary>No privacy protocol. Messages are sent in plaintext.</summary>
    None,

    /// <summary>CBC-DES encryption protocol.</summary>
    DES,

    /// <summary>CFB128-AES-128 encryption protocol.</summary>
    AES128,

    /// <summary>CFB128-AES-256 encryption protocol, providing stronger encryption than AES-128.</summary>
    AES256
}
