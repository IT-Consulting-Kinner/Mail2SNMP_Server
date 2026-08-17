using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Core.Services;
using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Models.DTOs;
using Mail2SNMP.Models.Entities;
using Mail2SNMP.Models.Enums;
using Mail2SNMP.Worker.Models;
using Microsoft.EntityFrameworkCore;

namespace Mail2SNMP.Worker.Services;

/// <summary>
/// Everything that happens to one inbound mail after it has been fetched: claim, rule
/// evaluation, rate limiting, event creation, disposition recording and notification
/// dispatch.
/// </summary>
/// <remarks>
/// <para>
/// Extracted from <see cref="MailPollingService"/>, where this logic was inlined in the
/// IMAP fetch loop behind a live <c>ImapClient</c>. That placement made the product's most
/// correctness-critical code unreachable from a test, and it is where three separate
/// defects survived several reviews: the mail claim was keyed per mailbox rather than per
/// job (so only the first job on a shared mailbox ever fired), the hourly event budget was
/// charged per inspected mail rather than per raised event (so a noisy mailbox starved its
/// own alerts), and a claim row was treated as proof of completion even when the run that
/// wrote it had died mid-way (so the alert was dropped permanently).
/// </para>
/// <para>
/// The IMAP <c>Seen</c> flag is the one piece of shared state this class cannot own — it
/// lives on the server — so the caller supplies a delegate and the pipeline decides
/// <em>whether</em> to call it. Nothing here references MailKit.
/// </para>
/// </remarks>
public sealed class MailProcessingPipeline
{
    private readonly Mail2SnmpDbContext _db;
    private readonly RuleEvaluator _ruleEvaluator;
    private readonly IEventService _eventService;
    private readonly FloodProtectionService _floodProtection;
    private readonly IReadOnlyList<INotificationChannel> _channels;
    private readonly ILogger<MailProcessingPipeline> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MailProcessingPipeline"/> class.
    /// </summary>
    /// <param name="db">Scoped database context used for the claim row and the seen-gating counts.</param>
    /// <param name="ruleEvaluator">Evaluates the job's rule against the mail's fields.</param>
    /// <param name="eventService">Creates, suppresses and transitions events (owns event deduplication).</param>
    /// <param name="floodProtection">Enforces the job's hourly event budget.</param>
    /// <param name="channels">The registered notification channels, selected by name.</param>
    /// <param name="logger">Diagnostic logger for claim resumption, rate limiting and delivery failures.</param>
    /// <remarks>
    /// Notably absent: <c>NotificationDedupCache</c>. It was threaded from the DI scope
    /// through two method signatures into the notification sender and never read — the
    /// channels apply notification dedup themselves. Carrying it along would have implied
    /// a dedup step here that does not exist.
    /// </remarks>
    public MailProcessingPipeline(
        Mail2SnmpDbContext db,
        RuleEvaluator ruleEvaluator,
        IEventService eventService,
        FloodProtectionService floodProtection,
        IEnumerable<INotificationChannel> channels,
        ILogger<MailProcessingPipeline> logger)
    {
        _db = db;
        _ruleEvaluator = ruleEvaluator;
        _eventService = eventService;
        _floodProtection = floodProtection;
        _channels = channels.ToList();
        _logger = logger;
    }

    /// <summary>
    /// Reports whether the job has already spent its hourly event budget, without charging
    /// against it.
    /// </summary>
    /// <remarks>
    /// A read-only pre-flight check. Most inspected mails do not match the job's rule, and
    /// charging the budget for merely looking at them starved noisy mailboxes of their
    /// entire allowance before a single alert was raised. The budget is charged in
    /// <see cref="ProcessAsync"/>, once per event actually raised — which is what
    /// <c>MaxEventsPerHour</c> has always claimed to mean.
    /// </remarks>
    public bool IsEventBudgetExhausted(Job job)
        => _floodProtection.IsEventBudgetExhausted(job.Id, job.MaxEventsPerHour);

    /// <summary>
    /// Runs one inbound mail through the full pipeline and records its outcome.
    /// </summary>
    /// <param name="job">The job whose rule and targets apply. Its target assignments must be loaded.</param>
    /// <param name="mailbox">The mailbox the mail was fetched from.</param>
    /// <param name="rule">The job's rule.</param>
    /// <param name="mail">The fetched message, reduced to the fields needed here.</param>
    /// <param name="inMaintenance">Whether an active maintenance window currently suppresses notifications.</param>
    /// <param name="markSeenAsync">
    /// Flags the mail <c>Seen</c> on the server. Invoked only once every active job on the
    /// mailbox has recorded a completed claim — see <see cref="TryMarkSeenAsync"/>.
    /// </param>
    /// <param name="ct">Token used to cancel the operation.</param>
    /// <returns>The recorded disposition, the resulting event id, and whether the mail was flagged seen.</returns>
    public async Task<MailProcessingOutcome> ProcessAsync(
        Job job,
        Mailbox mailbox,
        Rule rule,
        InboundMail mail,
        bool inMaintenance,
        Func<CancellationToken, Task> markSeenAsync,
        CancellationToken ct)
    {
        var (claim, alreadyDone) = await ClaimAsync(job, mailbox, mail, ct);

        if (alreadyDone)
        {
            Infrastructure.Services.Mail2SnmpMetrics.EmailsDuplicate.Inc();
            _logger.LogDebug(
                "Email already processed for Job {JobId} (ClaimKey={ClaimKey}, Mailbox={Name}). Skipping.",
                job.Id, mail.ClaimKey, mailbox.Name);
            var seen = await TryMarkSeenAsync(mailbox.Id, mail.ClaimKey, markSeenAsync, ct);
            return new MailProcessingOutcome(Disposition: null, EventId: null, MarkedSeen: seen);
        }

        var matched = _ruleEvaluator.Evaluate(rule, mail.From, mail.Subject, mail.Body, mail.Headers);

        if (!matched)
        {
            // Recording the no-match outcome is what makes "why did I get no alarm for
            // mail X?" answerable from the Mail Log instead of from the server logs.
            claim.Disposition = MailDisposition.NoMatch;
            await _db.SaveChangesAsync(ct);
            var seen = await TryMarkSeenAsync(mailbox.Id, mail.ClaimKey, markSeenAsync, ct);
            Infrastructure.Services.Mail2SnmpMetrics.EmailsProcessed.WithLabels(mailbox.Name).Inc();
            return new MailProcessingOutcome(MailDisposition.NoMatch, EventId: null, seen);
        }

        // The budget is charged here — once per event the job raises.
        if (_floodProtection.IsEventRateLimited(job.Id, job.MaxEventsPerHour))
        {
            Infrastructure.Services.Mail2SnmpMetrics.RateLimitHits.WithLabels("events-per-hour").Inc();
            claim.Disposition = MailDisposition.RateLimited;
            await _db.SaveChangesAsync(ct);
            _logger.LogWarning(
                "Job {JobId} reached its hourly event limit ({Max}); mail '{Subject}' was not raised as an event.",
                job.Id, job.MaxEventsPerHour, mail.Subject);
            var seen = await TryMarkSeenAsync(mailbox.Id, mail.ClaimKey, markSeenAsync, ct);
            return new MailProcessingOutcome(MailDisposition.RateLimited, EventId: null, seen);
        }

        Infrastructure.Services.Mail2SnmpMetrics.EmailsMatched.WithLabels(mailbox.Name, rule.Name).Inc();
        // Per-mail logging stays at Debug: Information would drown the log under any
        // meaningful mail volume. The caller's end-of-batch summary is what operators want.
        _logger.LogDebug(
            "Rule '{RuleName}' matched email (From={From}, Subject={Subject}) in Job {JobId}",
            rule.Name, mail.From, mail.Subject, job.Id);

        var evt = await _eventService.CreateAsync(new Event
        {
            JobId = job.Id,
            State = EventState.New,
            Severity = rule.Severity,
            RuleName = rule.Name,
            Subject = mail.Subject,
            MailFrom = mail.From,
            MessageId = mail.MessageId,
            CreatedUtc = DateTime.UtcNow
        }, ct);

        _logger.LogDebug(
            "Event {EventId} created for Job {JobId} (Rule: {RuleName}, Severity: {Severity})",
            evt.Id, job.Id, rule.Name, evt.Severity);

        // HitCount > 1 means EventService collapsed this mail into an existing event
        // rather than creating a new one.
        claim.EventId = evt.Id;
        claim.Disposition = inMaintenance ? MailDisposition.MaintenanceSuppressed
            : evt.HitCount > 1 ? MailDisposition.Deduplicated
            : MailDisposition.EventCreated;

        if (inMaintenance)
        {
            await _eventService.SuppressAsync(evt.Id, ct);
            _logger.LogDebug(
                "Event {EventId} suppressed during maintenance window for Job {JobId}",
                evt.Id, job.Id);
        }
        else
        {
            await SendNotificationsAsync(job, rule, evt, mail.From, mail.Subject, ct);
        }

        await _db.SaveChangesAsync(ct);
        var markedSeen = await TryMarkSeenAsync(mailbox.Id, mail.ClaimKey, markSeenAsync, ct);
        Infrastructure.Services.Mail2SnmpMetrics.EmailsProcessed.WithLabels(mailbox.Name).Inc();

        return new MailProcessingOutcome(claim.Disposition, evt.Id, markedSeen);
    }

    /// <summary>
    /// Atomically claims the mail for this job by inserting its <see cref="ProcessedMail"/>
    /// row before any processing happens.
    /// </summary>
    /// <returns>
    /// The claim row to stamp with the outcome, and whether another run has already
    /// completed this mail (in which case the caller must skip it).
    /// </returns>
    /// <remarks>
    /// <para>
    /// Insert-first, not select-then-insert: the older shape left a window in which two
    /// cluster nodes both passed the check and ran the entire pipeline before the unique
    /// constraint tripped on the second insert.
    /// </para>
    /// <para>
    /// A claim row is <em>not</em> proof that the mail was handled. It is committed before
    /// processing, so a transient failure part-way through leaves it behind with
    /// <see cref="MailDisposition.Unknown"/>. Treating that as "someone else did it"
    /// dropped the alert silently and permanently, so an unstamped row is re-claimed and
    /// processed here instead.
    /// </para>
    /// </remarks>
    private async Task<(ProcessedMail Claim, bool AlreadyDone)> ClaimAsync(
        Job job, Mailbox mailbox, InboundMail mail, CancellationToken ct)
    {
        var claim = new ProcessedMail
        {
            MessageId = mail.ClaimKey,
            MailboxId = mailbox.Id,
            JobId = job.Id,
            From = mail.From,
            Subject = mail.Subject,
            ReceivedUtc = mail.ReceivedUtc,
            ProcessedUtc = DateTime.UtcNow
        };

        _db.ProcessedMails.Add(claim);
        try
        {
            await _db.SaveChangesAsync(ct);
            return (claim, false);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Detach the failed insert so the next mail starts from a clean tracker.
            foreach (var entry in _db.ChangeTracker.Entries<ProcessedMail>().ToList())
                entry.State = EntityState.Detached;

            var existing = await _db.ProcessedMails
                .FirstOrDefaultAsync(p => p.MessageId == mail.ClaimKey
                                       && p.MailboxId == mailbox.Id
                                       && p.JobId == job.Id, ct);

            if (existing is null || existing.Disposition != MailDisposition.Unknown)
                return (claim, true);

            _logger.LogWarning(
                "Resuming mail {ClaimKey} for Job {JobId}: a previous attempt claimed it but never " +
                "recorded an outcome (likely a transient failure). Re-processing.",
                mail.ClaimKey, job.Id);
            existing.ProcessedUtc = DateTime.UtcNow;
            return (existing, false);
        }
    }

    /// <summary>
    /// Recognizes the provider-specific unique-constraint violation that signals a lost
    /// claim race.
    /// </summary>
    private static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException?.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) == true
        || ex.InnerException?.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Invokes the caller's seen-marker only once every currently-active job on the mailbox
    /// has recorded a completed claim for this mail.
    /// </summary>
    /// <remarks>
    /// The IMAP <c>Seen</c> flag is shared across every job polling a mailbox, and the poll
    /// query is <c>NotSeen</c> — so whichever job flags the mail first hides it from all its
    /// siblings. Deferring the flag until the completed-claim count reaches the active-job
    /// count lets several rules share one mailbox. Because the expected count is read live,
    /// deactivating or deleting a job lowers it, so a mail can never be stranded unseen by a
    /// job that no longer exists.
    /// </remarks>
    /// <returns><c>true</c> when the mail was flagged seen.</returns>
    private async Task<bool> TryMarkSeenAsync(
        int mailboxId, string claimKey, Func<CancellationToken, Task> markSeenAsync, CancellationToken ct)
    {
        var activeJobs = await _db.Jobs
            .CountAsync(j => j.MailboxId == mailboxId && j.IsActive, ct);

        var completedClaims = await _db.ProcessedMails
            .CountAsync(p => p.MailboxId == mailboxId
                          && p.MessageId == claimKey
                          && p.Disposition != MailDisposition.Unknown, ct);

        if (completedClaims < activeJobs)
        {
            _logger.LogDebug(
                "Mail {ClaimKey} stays unseen: {Done}/{Total} active jobs on mailbox {MailboxId} have processed it.",
                claimKey, completedClaims, activeJobs, mailboxId);
            return false;
        }

        await markSeenAsync(ct);
        return true;
    }

    /// <summary>
    /// Sends the event to the job's assigned SNMP and webhook targets and transitions it to
    /// <see cref="EventState.Notified"/> once at least one channel actually delivered.
    /// </summary>
    /// <remarks>
    /// The channels report a delivery outcome, and the transition depends on it: an event
    /// whose every send silently failed — a v3 target under a Community license, a DNS
    /// failure, master-key drift — stays <see cref="EventState.New"/> rather than being
    /// falsely reported as notified. Targets below their configured <c>MinSeverity</c> are
    /// skipped ("page the NOC only for Critical").
    /// </remarks>
    private async Task SendNotificationsAsync(
        Job job, Rule rule, Event evt, string from, string subject, CancellationToken ct)
    {
        var context = new NotificationContext
        {
            EventId = evt.Id,
            JobName = job.Name,
            Mailbox = job.Mailbox?.Name ?? string.Empty,
            From = from,
            Subject = subject,
            Severity = evt.Severity,
            RuleName = rule.Name,
            HitCount = evt.HitCount,
            TimestampUtc = evt.CreatedUtc,
            TrapTemplate = job.TrapTemplate,
            WebhookTemplate = job.WebhookTemplate,
            OidMapping = job.OidMapping
        };

        var snmpChannel = _channels.FirstOrDefault(c => c.ChannelName == INotificationChannel.Snmp);
        var webhookChannel = _channels.FirstOrDefault(c => c.ChannelName == INotificationChannel.Webhook);
        var anySuccess = false;

        foreach (var jst in job.JobSnmpTargets.Where(t =>
                     t.SnmpTarget.IsActive && evt.Severity >= t.SnmpTarget.MinSeverity))
        {
            try
            {
                if (snmpChannel != null && await snmpChannel.SendToSnmpTargetAsync(context, jst.SnmpTarget, ct))
                    anySuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send SNMP trap to {Target} for Event {EventId}",
                    jst.SnmpTarget.Name, evt.Id);
            }
        }

        foreach (var jwt in job.JobWebhookTargets.Where(t =>
                     t.WebhookTarget.IsActive && evt.Severity >= t.WebhookTarget.MinSeverity))
        {
            try
            {
                if (webhookChannel != null && await webhookChannel.SendToWebhookTargetAsync(context, jwt.WebhookTarget, ct))
                    anySuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send webhook to {Target} for Event {EventId}",
                    jwt.WebhookTarget.Name, evt.Id);
            }
        }

        if (anySuccess && evt.State == EventState.New)
        {
            try
            {
                await _eventService.MarkAsNotifiedAsync(evt.Id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to mark Event {EventId} as Notified", evt.Id);
            }
        }
    }
}
