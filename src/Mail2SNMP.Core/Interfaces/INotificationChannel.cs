using Mail2SNMP.Models.DTOs;
using Mail2SNMP.Models.Entities;

namespace Mail2SNMP.Core.Interfaces;

/// <summary>
/// Abstraction for a notification delivery channel (e.g., SNMP traps or webhooks).
/// Implementations are registered via DI and selected by the job's target assignments.
/// </summary>
public interface INotificationChannel
{
    /// <summary>
    /// The channel identifier (e.g., "snmp", "webhook").
    /// </summary>
    string ChannelName { get; }

    /// <summary>
    /// Delivers a notification for the given event context to all active targets
    /// managed by this channel. Used by legacy code and event replay.
    /// </summary>
    Task SendAsync(NotificationContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delivers a notification for the given event context to a specific SNMP target.
    /// Default implementation does nothing (for non-SNMP channels).
    /// </summary>
    Task SendToSnmpTargetAsync(NotificationContext context, SnmpTarget target, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>
    /// Delivers a notification for the given event context to a specific Webhook target.
    /// Default implementation does nothing (for non-webhook channels).
    /// </summary>
    Task SendToWebhookTargetAsync(NotificationContext context, WebhookTarget target, CancellationToken ct = default)
        => Task.CompletedTask;
}
