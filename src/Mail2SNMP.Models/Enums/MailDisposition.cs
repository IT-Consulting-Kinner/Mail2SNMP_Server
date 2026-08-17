namespace Mail2SNMP.Models.Enums;

/// <summary>
/// UC-5: the processing outcome recorded per inbound mail, so an operator can
/// answer "we emailed an alert at 03:14 and got no trap — why?" directly from
/// the product instead of grepping logs. Stored on
/// <see cref="Entities.ProcessedMail.Disposition"/>.
/// </summary>
public enum MailDisposition
{
    /// <summary>
    /// Legacy rows created before dispositions were recorded (database default),
    /// or a mail whose processing aborted before an outcome was determined.
    /// </summary>
    Unknown = 0,

    /// <summary>The mail did not match the job's rule; no event was created.</summary>
    NoMatch = 1,

    /// <summary>
    /// The mail matched and a new <see cref="Entities.Event"/> was created (see
    /// <see cref="Entities.ProcessedMail.EventId"/>); notifications were dispatched
    /// unless suppressed downstream.
    /// </summary>
    EventCreated = 2,

    /// <summary>
    /// The mail matched but was collapsed into an existing event within the dedup
    /// window — <see cref="Entities.ProcessedMail.EventId"/> points at the existing
    /// event whose hit count was incremented. No new notifications were sent.
    /// </summary>
    Deduplicated = 3,

    /// <summary>
    /// The mail matched during an active maintenance window: the event was created
    /// and immediately suppressed, and no notifications were sent.
    /// </summary>
    MaintenanceSuppressed = 4,

    /// <summary>
    /// H-2: the mail matched the rule, but the job had already used its hourly event
    /// budget (<c>Job.MaxEventsPerHour</c>), so no event was raised and no notification
    /// was sent. Recorded explicitly so a suppressed alert is visible in the Mail Log
    /// instead of looking like a mail that simply never arrived.
    /// </summary>
    RateLimited = 5,
}
