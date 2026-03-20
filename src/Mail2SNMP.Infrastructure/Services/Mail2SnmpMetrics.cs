using Prometheus;

namespace Mail2SNMP.Infrastructure.Services;

/// <summary>
/// Central location for all Prometheus metric definitions.
/// </summary>
public static class Mail2SnmpMetrics
{
    // Event metrics
    public static readonly Counter EventsCreated = Metrics.CreateCounter(
        "mail2snmp_events_created_total",
        "Total number of events created",
        new CounterConfiguration { LabelNames = new[] { "job_id", "severity" } });

    public static readonly Counter EventsNotified = Metrics.CreateCounter(
        "mail2snmp_events_notified_total",
        "Total number of events successfully notified");

    public static readonly Gauge ActiveEvents = Metrics.CreateGauge(
        "mail2snmp_events_active",
        "Current number of active (non-terminal) events");

    // Mail polling metrics
    public static readonly Counter EmailsProcessed = Metrics.CreateCounter(
        "mail2snmp_emails_processed_total",
        "Total number of emails processed",
        new CounterConfiguration { LabelNames = new[] { "mailbox" } });

    public static readonly Counter EmailsMatched = Metrics.CreateCounter(
        "mail2snmp_emails_matched_total",
        "Total number of emails that matched a rule",
        new CounterConfiguration { LabelNames = new[] { "mailbox", "rule" } });

    public static readonly Counter EmailsDuplicate = Metrics.CreateCounter(
        "mail2snmp_emails_duplicate_total",
        "Total number of duplicate emails skipped");

    // Notification metrics
    public static readonly Counter NotificationsSent = Metrics.CreateCounter(
        "mail2snmp_notifications_sent_total",
        "Total notifications sent",
        new CounterConfiguration { LabelNames = new[] { "channel" } });

    public static readonly Counter NotificationsFailed = Metrics.CreateCounter(
        "mail2snmp_notifications_failed_total",
        "Total notification failures",
        new CounterConfiguration { LabelNames = new[] { "channel" } });

    // SNMP trap metrics
    public static readonly Counter SnmpTrapsSent = Metrics.CreateCounter(
        "mail2snmp_snmp_traps_sent_total",
        "Total SNMP traps sent",
        new CounterConfiguration { LabelNames = new[] { "target", "version" } });

    // Webhook metrics
    public static readonly Counter WebhookRequestsSent = Metrics.CreateCounter(
        "mail2snmp_webhook_requests_sent_total",
        "Total webhook requests sent",
        new CounterConfiguration { LabelNames = new[] { "target" } });

    // Dead letter metrics (v5.8 names: mail2snmp_webhook_deadletter_*)
    public static readonly Counter WebhookDeadLetterTotal = Metrics.CreateCounter(
        "mail2snmp_webhook_deadletter_total",
        "Total webhook deliveries sent to dead letter");

    public static readonly Gauge DeadLetterPending = Metrics.CreateGauge(
        "mail2snmp_webhook_deadletter_pending",
        "Number of pending dead letter entries");

    public static readonly Counter DeadLetterRetried = Metrics.CreateCounter(
        "mail2snmp_deadletter_retried_total",
        "Total dead letter retry attempts");

    // IMAP connection metrics
    public static readonly Gauge ImapActiveConnections = Metrics.CreateGauge(
        "mail2snmp_imap_active_connections",
        "Current number of active IMAP connections");

    public static readonly Counter ImapConnectionErrors = Metrics.CreateCounter(
        "mail2snmp_imap_connection_errors_total",
        "Total IMAP connection errors",
        new CounterConfiguration { LabelNames = new[] { "mailbox" } });

    // Channel overflow (v5.8: mail2snmp_channel_overflow_total)
    public static readonly Counter ChannelOverflow = Metrics.CreateCounter(
        "mail2snmp_channel_overflow_total",
        "Total number of channel overflow events (work items dropped due to full channel)");

    // Rate limiting (v5.8: mail2snmp_traps_rate_limited_total)
    public static readonly Counter TrapsRateLimited = Metrics.CreateCounter(
        "mail2snmp_traps_rate_limited_total",
        "Total number of SNMP traps dropped due to rate limiting");

    public static readonly Counter RateLimitHits = Metrics.CreateCounter(
        "mail2snmp_rate_limit_hits_total",
        "Total number of rate limit hits",
        new CounterConfiguration { LabelNames = new[] { "type" } });

    // Data retention
    public static readonly Counter RetentionDeleted = Metrics.CreateCounter(
        "mail2snmp_retention_deleted_total",
        "Total records deleted by data retention",
        new CounterConfiguration { LabelNames = new[] { "entity" } });
}
