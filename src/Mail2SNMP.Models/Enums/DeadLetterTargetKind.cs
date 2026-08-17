namespace Mail2SNMP.Models.Enums;

/// <summary>
/// Identifies which kind of notification target a dead-letter entry belongs to.
/// </summary>
/// <remarks>
/// A <c>DeadLetterEntry</c> carries exactly one of <c>WebhookTargetId</c> / <c>SnmpTargetId</c>.
/// Before UC-3 only webhooks could be dead-lettered, so the queue API was written around
/// <c>webhookTargetId</c> and SNMP entries had to be handled one row at a time by callers.
/// This enum lets the service filter and bulk-retry either kind through one code path.
/// </remarks>
public enum DeadLetterTargetKind
{
    /// <summary>The entry references a <c>WebhookTarget</c> (HTTP POST delivery).</summary>
    Webhook,

    /// <summary>The entry references an <c>SnmpTarget</c> (trap delivery, UC-3).</summary>
    Snmp
}
