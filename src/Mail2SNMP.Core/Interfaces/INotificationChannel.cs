using Mail2SNMP.Models.DTOs;
using Mail2SNMP.Models.Entities;

namespace Mail2SNMP.Core.Interfaces;

/// <summary>
/// Abstraction for a notification delivery channel (e.g., SNMP traps or webhooks).
/// Implementations are registered via DI and selected by the job's target assignments.
/// </summary>
/// <remarks>
/// AR-2/FN-3 contract revision:
/// <list type="bullet">
/// <item>The legacy broadcast <c>SendAsync(NotificationContext)</c> was removed — a
/// repo-wide search found zero callers; every real dispatch goes through the
/// per-target methods below. Its presence misled maintainers into assuming a
/// working generic path existed.</item>
/// <item>Dispatch sites resolve the concrete channel via the <see cref="Snmp"/> /
/// <see cref="Webhook"/> constants instead of scattered string literals, so a
/// rename or new channel cannot silently miss a call site.</item>
/// <item>The per-target send methods return a <b>delivery outcome</b>: <c>true</c>
/// when the notification was dispatched (or an earlier dispatch already covered it
/// via notification-dedup), <c>false</c> when nothing left the process (rate-limit
/// drop, send failure, misconfiguration). Callers use this to decide whether an
/// event may transition to Notified — previously an event was marked Notified even
/// when every send silently failed.</item>
/// </list>
/// </remarks>
public interface INotificationChannel
{
    /// <summary>Well-known <see cref="ChannelName"/> of the SNMP trap channel.</summary>
    const string Snmp = "snmp";

    /// <summary>Well-known <see cref="ChannelName"/> of the HTTP webhook channel.</summary>
    const string Webhook = "webhook";

    /// <summary>
    /// The channel identifier (<see cref="Snmp"/> or <see cref="Webhook"/>).
    /// </summary>
    string ChannelName { get; }

    /// <summary>
    /// Delivers a notification for the given event context to a specific SNMP target.
    /// Default implementation does nothing (for non-SNMP channels).
    /// </summary>
    /// <returns>
    /// <c>true</c> when the trap was handed to the network stack (or notification-dedup
    /// shows an earlier send already covered this event/target pair); <c>false</c> when
    /// the trap was dropped (rate limit, DNS/decrypt failure, licence gate, send error).
    /// </returns>
    Task<bool> SendToSnmpTargetAsync(NotificationContext context, SnmpTarget target, CancellationToken ct = default)
        => Task.FromResult(false);

    /// <summary>
    /// Delivers a notification for the given event context to a specific Webhook target.
    /// Default implementation does nothing (for non-webhook channels).
    /// </summary>
    /// <returns>
    /// <c>true</c> when the webhook POST succeeded (or notification-dedup shows an
    /// earlier send already covered this event/target pair); <c>false</c> when it was
    /// rate-limited or failed — failures are dead-lettered for retry, but the event has
    /// NOT been delivered yet.
    /// </returns>
    Task<bool> SendToWebhookTargetAsync(NotificationContext context, WebhookTarget target, CancellationToken ct = default)
        => Task.FromResult(false);

    /// <summary>
    /// Sends an mail2SNMPEventConfirmedNotification trap. SNMP-only.
    /// Default implementation does nothing.
    /// </summary>
    Task SendEventConfirmedAsync(long eventId, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>
    /// Sends a KeepAlive notification trap to all targets that have it enabled. SNMP-only.
    /// </summary>
    Task SendKeepAliveAsync(CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>
    /// Sends an Update-available notification trap. SNMP-only.
    /// </summary>
    Task SendUpdateAvailableAsync(UpdateInfo info, CancellationToken ct = default)
        => Task.CompletedTask;
}
