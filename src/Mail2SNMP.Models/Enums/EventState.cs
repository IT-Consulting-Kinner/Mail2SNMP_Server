namespace Mail2SNMP.Models.Enums;

/// <summary>
/// Represents the lifecycle state of a monitoring event.
/// </summary>
public enum EventState
{
    /// <summary>Event created but not yet delivered to any notification channel.</summary>
    New,

    /// <summary>Event delivered to at least one notification channel.</summary>
    Notified,

    /// <summary>Event acknowledged by an operator or automated process.</summary>
    Acknowledged,

    /// <summary>Event resolved, indicating the underlying condition has been addressed.</summary>
    Resolved,

    /// <summary>Event suppressed by a rule and will not trigger further notifications.</summary>
    Suppressed,

    /// <summary>Event expired because it was not acted upon within the configured time window.</summary>
    Expired
}
