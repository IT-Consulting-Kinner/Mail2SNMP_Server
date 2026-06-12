using Prometheus;

namespace Mail2SNMP.Infrastructure.Services;

/// <summary>
/// Central location for all Prometheus metric definitions.
/// </summary>
public static class Mail2SnmpMetrics
{
    // Event metrics
    /// <summary>
    /// Counter <c>mail2snmp_events_created_total</c>: total events raised since process start.
    /// Labels: <c>job_id</c> (the originating job) and <c>severity</c> (the event severity).
    /// </summary>
    public static readonly Counter EventsCreated = Metrics.CreateCounter(
        "mail2snmp_events_created_total",
        "Total number of events created",
        new CounterConfiguration { LabelNames = new[] { "job_id", "severity" } });

    /// <summary>
    /// Counter <c>mail2snmp_events_notified_total</c>: total events for which at least one
    /// notification was successfully dispatched. Unlabeled.
    /// </summary>
    public static readonly Counter EventsNotified = Metrics.CreateCounter(
        "mail2snmp_events_notified_total",
        "Total number of events successfully notified");

    /// <summary>
    /// Gauge <c>mail2snmp_events_active</c>: current count of active (non-terminal) events.
    /// Rises when events are raised and falls as they are acknowledged/resolved/expired. Unlabeled.
    /// </summary>
    public static readonly Gauge ActiveEvents = Metrics.CreateGauge(
        "mail2snmp_events_active",
        "Current number of active (non-terminal) events");

    // Mail polling metrics
    /// <summary>
    /// Counter <c>mail2snmp_emails_processed_total</c>: total e-mail messages fetched and
    /// processed by the poller/IDLE pipeline. Label: <c>mailbox</c>.
    /// </summary>
    public static readonly Counter EmailsProcessed = Metrics.CreateCounter(
        "mail2snmp_emails_processed_total",
        "Total number of emails processed",
        new CounterConfiguration { LabelNames = new[] { "mailbox" } });

    /// <summary>
    /// Counter <c>mail2snmp_emails_matched_total</c>: total e-mails that matched a rule and
    /// triggered a job. Labels: <c>mailbox</c> and <c>rule</c> (the matched rule).
    /// </summary>
    public static readonly Counter EmailsMatched = Metrics.CreateCounter(
        "mail2snmp_emails_matched_total",
        "Total number of emails that matched a rule",
        new CounterConfiguration { LabelNames = new[] { "mailbox", "rule" } });

    /// <summary>
    /// Counter <c>mail2snmp_emails_duplicate_total</c>: total e-mails skipped because they
    /// were already processed (message-id dedup). Unlabeled.
    /// </summary>
    public static readonly Counter EmailsDuplicate = Metrics.CreateCounter(
        "mail2snmp_emails_duplicate_total",
        "Total number of duplicate emails skipped");

    // Notification metrics
    /// <summary>
    /// Counter <c>mail2snmp_notifications_sent_total</c>: total notifications successfully sent.
    /// Label: <c>channel</c> (the delivery channel, e.g. snmp or webhook).
    /// </summary>
    public static readonly Counter NotificationsSent = Metrics.CreateCounter(
        "mail2snmp_notifications_sent_total",
        "Total notifications sent",
        new CounterConfiguration { LabelNames = new[] { "channel" } });

    /// <summary>
    /// Counter <c>mail2snmp_notifications_failed_total</c>: total notification dispatch failures.
    /// Label: <c>channel</c> (the delivery channel that failed).
    /// </summary>
    public static readonly Counter NotificationsFailed = Metrics.CreateCounter(
        "mail2snmp_notifications_failed_total",
        "Total notification failures",
        new CounterConfiguration { LabelNames = new[] { "channel" } });

    // SNMP trap metrics
    /// <summary>
    /// Counter <c>mail2snmp_snmp_traps_sent_total</c>: total SNMP traps emitted.
    /// Labels: <c>target</c> (the SNMP target) and <c>version</c> (the SNMP protocol version).
    /// </summary>
    public static readonly Counter SnmpTrapsSent = Metrics.CreateCounter(
        "mail2snmp_snmp_traps_sent_total",
        "Total SNMP traps sent",
        new CounterConfiguration { LabelNames = new[] { "target", "version" } });

    // Webhook metrics
    /// <summary>
    /// Counter <c>mail2snmp_webhook_requests_sent_total</c>: total outbound webhook HTTP
    /// requests dispatched. Label: <c>target</c> (the webhook target).
    /// </summary>
    public static readonly Counter WebhookRequestsSent = Metrics.CreateCounter(
        "mail2snmp_webhook_requests_sent_total",
        "Total webhook requests sent",
        new CounterConfiguration { LabelNames = new[] { "target" } });

    // Dead letter metrics (v5.8 names: mail2snmp_webhook_deadletter_*)
    /// <summary>
    /// Counter <c>mail2snmp_webhook_deadletter_total</c>: total webhook deliveries that
    /// exhausted retries and were written to the dead-letter store. Unlabeled.
    /// </summary>
    public static readonly Counter WebhookDeadLetterTotal = Metrics.CreateCounter(
        "mail2snmp_webhook_deadletter_total",
        "Total webhook deliveries sent to dead letter");

    /// <summary>
    /// Gauge <c>mail2snmp_webhook_deadletter_pending</c>: current number of dead-letter
    /// entries awaiting retry. Unlabeled.
    /// </summary>
    public static readonly Gauge DeadLetterPending = Metrics.CreateGauge(
        "mail2snmp_webhook_deadletter_pending",
        "Number of pending dead letter entries");

    /// <summary>
    /// Counter <c>mail2snmp_deadletter_retried_total</c>: total dead-letter retry attempts
    /// performed by the retry worker (regardless of outcome). Unlabeled.
    /// </summary>
    public static readonly Counter DeadLetterRetried = Metrics.CreateCounter(
        "mail2snmp_deadletter_retried_total",
        "Total dead letter retry attempts");

    // IMAP connection metrics
    /// <summary>
    /// Gauge <c>mail2snmp_imap_active_connections</c>: current number of open IMAP
    /// connections across all mailboxes. Unlabeled.
    /// </summary>
    public static readonly Gauge ImapActiveConnections = Metrics.CreateGauge(
        "mail2snmp_imap_active_connections",
        "Current number of active IMAP connections");

    /// <summary>
    /// Counter <c>mail2snmp_imap_connection_errors_total</c>: total IMAP connect/authenticate
    /// failures. Label: <c>mailbox</c> (the mailbox whose connection failed).
    /// </summary>
    public static readonly Counter ImapConnectionErrors = Metrics.CreateCounter(
        "mail2snmp_imap_connection_errors_total",
        "Total IMAP connection errors",
        new CounterConfiguration { LabelNames = new[] { "mailbox" } });

    // Channel overflow (v5.8: mail2snmp_channel_overflow_total)
    /// <summary>
    /// Counter <c>mail2snmp_channel_overflow_total</c>: total work items dropped because the
    /// bounded producer/consumer channel was full (see
    /// <see cref="Mail2SNMP.Models.Configuration.ImapSettings.ChannelBoundedCapacity"/>).
    /// A rising value signals the consumers cannot keep up with inbound mail. Unlabeled.
    /// </summary>
    public static readonly Counter ChannelOverflow = Metrics.CreateCounter(
        "mail2snmp_channel_overflow_total",
        "Total number of channel overflow events (work items dropped due to full channel)");

    // Rate limiting (v5.8: mail2snmp_traps_rate_limited_total)
    /// <summary>
    /// Counter <c>mail2snmp_traps_rate_limited_total</c>: total SNMP traps dropped because a
    /// rate limit was exceeded. Unlabeled.
    /// </summary>
    public static readonly Counter TrapsRateLimited = Metrics.CreateCounter(
        "mail2snmp_traps_rate_limited_total",
        "Total number of SNMP traps dropped due to rate limiting");

    /// <summary>
    /// Counter <c>mail2snmp_rate_limit_hits_total</c>: total times a rate limit was hit.
    /// Label: <c>type</c> (which limiter fired, e.g. events-per-hour or active-events).
    /// </summary>
    public static readonly Counter RateLimitHits = Metrics.CreateCounter(
        "mail2snmp_rate_limit_hits_total",
        "Total number of rate limit hits",
        new CounterConfiguration { LabelNames = new[] { "type" } });

    // Data retention
    /// <summary>
    /// Counter <c>mail2snmp_retention_deleted_total</c>: total records purged by the data
    /// retention sweep. Label: <c>entity</c> (the entity type purged, e.g. audit or deadletter).
    /// </summary>
    public static readonly Counter RetentionDeleted = Metrics.CreateCounter(
        "mail2snmp_retention_deleted_total",
        "Total records deleted by data retention",
        new CounterConfiguration { LabelNames = new[] { "entity" } });
}
