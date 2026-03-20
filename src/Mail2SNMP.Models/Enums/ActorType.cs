namespace Mail2SNMP.Models.Enums;

/// <summary>
/// Identifies the kind of actor that initiated an operation.
/// </summary>
public enum ActorType
{
    /// <summary>A human user who performed the action interactively.</summary>
    User,

    /// <summary>A background service or daemon that performed the action automatically.</summary>
    Service,

    /// <summary>The system itself, such as an internal scheduler or startup routine.</summary>
    System
}
