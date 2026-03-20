namespace Mail2SNMP.Models.Entities;

/// <summary>
/// Stores serialized authentication tickets in the database to keep cookies small.
/// Used as a server-side session store for the cookie authentication handler.
/// </summary>
public class AuthTicket
{
    /// <summary>Session identifier stored in the authentication cookie.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Serialized authentication ticket data (claims, properties, etc.).</summary>
    public byte[] Value { get; set; } = Array.Empty<byte>();

    /// <summary>Timestamp of the last ticket renewal or access.</summary>
    public DateTime? LastActivity { get; set; }

    /// <summary>Absolute expiration time of the ticket. Null means no expiration.</summary>
    public DateTime? ExpiresUtc { get; set; }
}
