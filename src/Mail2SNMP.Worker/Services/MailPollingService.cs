using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Core.Services;
using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Models.Configuration;
using Mail2SNMP.Models.DTOs;
using Mail2SNMP.Models.Entities;
using Mail2SNMP.Models.Enums;
using Mail2SNMP.Worker.Models;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Threading.Channels;

namespace Mail2SNMP.Worker.Services;

/// <summary>
/// Background service that consumes IMAP polling work items from a bounded channel,
/// processes emails against rules, creates events, and sends notifications.
/// </summary>
public class MailPollingService : BackgroundService
{
    private readonly Channel<MailWorkItem> _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MailPollingService> _logger;
    private readonly SemaphoreSlim _imapSemaphore;
    private readonly int _consumerCount;
    private readonly ImapSettings _imapSettings;

    /// <summary>
    /// Initializes a new instance of the <see cref="MailPollingService"/> class.
    /// </summary>
    /// <param name="channel">The bounded channel from which mail work items are consumed.</param>
    /// <param name="scopeFactory">Factory used to create a scope per work item for resolving scoped services.</param>
    /// <param name="logger">The logger for consumer and IMAP-processing diagnostics.</param>
    /// <param name="imapOptions">
    /// AR-6: the validated <c>Imap</c> options (consumer count, connection limit,
    /// timeouts). Injected as <see cref="IOptions{TOptions}"/> instead of re-binding
    /// raw <c>IConfiguration</c>, so the ValidateOnStart pipeline applies and the
    /// service is unit-testable with a plain options value.
    /// </param>
    public MailPollingService(
        Channel<MailWorkItem> channel,
        IServiceScopeFactory scopeFactory,
        ILogger<MailPollingService> logger,
        IOptions<ImapSettings> imapOptions)
    {
        _channel = channel;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _imapSettings = imapOptions.Value;

        _imapSemaphore = new SemaphoreSlim(_imapSettings.MaxConcurrentConnections);
        _consumerCount = _imapSettings.ConsumerTasks;
    }

    /// <summary>
    /// Starts the configured number of consumer tasks that read from the bounded channel in parallel.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "MailPollingService starting with {ConsumerCount} consumers, IMAP connection limit {ImapLimit}",
            _consumerCount, _imapSemaphore.CurrentCount);

        var consumers = new Task[_consumerCount];

        for (int i = 0; i < _consumerCount; i++)
        {
            var consumerId = i;
            // Wrap each consumer in a self-restarting supervisor so that an unhandled
            // exception in the read loop or scope creation does not silently kill the
            // consumer (which would degrade throughput without any warning).
            consumers[i] = Task.Run(() => SuperviseConsumerAsync(consumerId, stoppingToken), stoppingToken);
        }

        try
        {
            await Task.WhenAll(consumers);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("MailPollingService shutting down gracefully");
        }
    }

    // I5: BackgroundService base does not dispose us, so the kernel SemaphoreSlim
    // handle would leak across hot-reloads or test restarts. Override Dispose to
    // release it explicitly. Safe to call multiple times — SemaphoreSlim.Dispose
    // is idempotent.
    /// <summary>
    /// Disposes the IMAP concurrency semaphore that the <see cref="BackgroundService"/> base class does not
    /// own, preventing a kernel handle leak across host restarts. Safe to call multiple times.
    /// </summary>
    public override void Dispose()
    {
        _imapSemaphore.Dispose();
        base.Dispose();
    }

    /// <summary>
    /// Supervisor that restarts a crashed consumer task with a short backoff. Without this
    /// wrapper, an unhandled exception thrown outside the inner try/catch (e.g. during
    /// channel read or scope creation) would silently kill the consumer until the entire
    /// service shuts down.
    /// </summary>
    private async Task SuperviseConsumerAsync(int consumerId, CancellationToken ct)
    {
        // N14: backoff is configurable via Imap:ConsumerRestartBackoffSeconds /
        // Imap:ConsumerRestartMaxBackoffSeconds in appsettings.json.
        var initial = TimeSpan.FromSeconds(Math.Max(1, _imapSettings.ConsumerRestartBackoffSeconds));
        var max = TimeSpan.FromSeconds(Math.Max(initial.TotalSeconds, _imapSettings.ConsumerRestartMaxBackoffSeconds));
        var backoff = initial;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ConsumeAsync(consumerId, ct);
                // Normal exit (channel closed or cancellation) — stop restarting.
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Consumer {ConsumerId} crashed with an unhandled exception. Restarting in {Backoff}s.",
                    consumerId, backoff.TotalSeconds);
                try
                {
                    await Task.Delay(backoff, ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                // Mild exponential backoff capped at the configured maximum.
                backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, max.TotalSeconds));
            }
        }
    }

    /// <summary>
    /// Reads work items from the channel in a loop and delegates each to <see cref="ProcessWorkItemAsync"/>.
    /// Runs until the channel completes or cancellation is requested.
    /// </summary>
    private async Task ConsumeAsync(int consumerId, CancellationToken ct)
    {
        _logger.LogDebug("Consumer {ConsumerId} started", consumerId);

        try
        {
            await foreach (var workItem in _channel.Reader.ReadAllAsync(ct))
            {
                try
                {
                    await ProcessWorkItemAsync(workItem, consumerId, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Consumer {ConsumerId} failed processing Job {JobId}, Mailbox {MailboxId}",
                        consumerId, workItem.JobId, workItem.MailboxId);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal shutdown
        }

        _logger.LogDebug("Consumer {ConsumerId} stopped", consumerId);
    }

    /// <summary>
    /// Acquires the IMAP semaphore, resolves scoped services, validates the job/rule/mailbox,
    /// checks maintenance windows and flood protection, then delegates to IMAP fetching.
    /// </summary>
    private async Task ProcessWorkItemAsync(MailWorkItem workItem, int consumerId, CancellationToken ct)
    {
        _logger.LogDebug(
            "Consumer {ConsumerId} processing Job {JobId}, Mailbox {MailboxId}, Schedule {ScheduleId}",
            consumerId, workItem.JobId, workItem.MailboxId, workItem.ScheduleId);

        await _imapSemaphore.WaitAsync(ct);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;

            var jobService = sp.GetRequiredService<IJobService>();
            var mailboxService = sp.GetRequiredService<IMailboxService>();
            var maintenanceService = sp.GetRequiredService<IMaintenanceWindowService>();
            var ruleEvaluator = sp.GetRequiredService<RuleEvaluator>();
            var eventService = sp.GetRequiredService<IEventService>();
            var notificationChannels = sp.GetRequiredService<IEnumerable<INotificationChannel>>();
            var floodProtection = sp.GetRequiredService<FloodProtectionService>();
            var dedupCache = sp.GetRequiredService<NotificationDedupCache>();
            var credentialEncryptor = sp.GetRequiredService<ICredentialEncryptor>();
            var dbContext = sp.GetRequiredService<Mail2SnmpDbContext>();

            // Load the job with its rule and mailbox
            var job = await jobService.GetByIdAsync(workItem.JobId, ct);
            if (job is null)
            {
                _logger.LogWarning("Job {JobId} not found. Skipping.", workItem.JobId);
                return;
            }

            if (!job.IsActive)
            {
                _logger.LogDebug("Job {JobId} is inactive. Skipping.", workItem.JobId);
                return;
            }

            // Check maintenance window
            var inMaintenance = await maintenanceService.IsInMaintenanceAsync(job.Id, ct);

            // H-2: pre-flight budget check must NOT consume budget — this runs once per
            // poll pass before a single mail has been looked at. Charging it here meant a
            // one-minute schedule spent 60 of the hourly allowance on polling alone.
            if (floodProtection.IsEventBudgetExhausted(job.Id, job.MaxEventsPerHour))
            {
                Mail2SNMP.Infrastructure.Services.Mail2SnmpMetrics.RateLimitHits.WithLabels("events-per-hour").Inc();
                _logger.LogWarning("Job {JobId} has exceeded event rate limit. Skipping.", job.Id);
                return;
            }

            var rule = job.Rule;
            if (rule is null || !rule.IsActive)
            {
                _logger.LogDebug("Rule for Job {JobId} is null or inactive. Skipping.", job.Id);
                return;
            }

            var mailbox = job.Mailbox;
            if (mailbox is null || !mailbox.IsActive)
            {
                _logger.LogDebug("Mailbox for Job {JobId} is null or inactive. Skipping.", job.Id);
                return;
            }

            // Connect to IMAP and fetch unseen emails
            await FetchAndProcessEmailsAsync(job, mailbox, rule, credentialEncryptor, ruleEvaluator,
                eventService, notificationChannels, floodProtection, dedupCache, mailboxService, dbContext, inMaintenance, ct);
        }
        finally
        {
            _imapSemaphore.Release();
        }
    }

    /// <summary>
    /// Connects to the IMAP server, fetches unseen emails from the configured folder,
    /// evaluates each message against the rule, creates events for matches, records
    /// processed messages for idempotency, and marks messages as seen.
    /// </summary>
    private async Task FetchAndProcessEmailsAsync(
        Job job, Mailbox mailbox, Rule rule,
        ICredentialEncryptor credentialEncryptor,
        RuleEvaluator ruleEvaluator,
        IEventService eventService,
        IEnumerable<INotificationChannel> notificationChannels,
        FloodProtectionService floodProtection,
        NotificationDedupCache dedupCache,
        IMailboxService mailboxService,
        Mail2SnmpDbContext dbContext,
        bool inMaintenance,
        CancellationToken ct)
    {
        using var imapClient = new ImapClient();

        // AR-1: tracks whether we got past connect+authenticate — drives the
        // active-connections gauge (finally) and the connection-errors counter (catch).
        var imapConnected = false;
        try
        {
            // Connect to mailbox.
            // SEC-1: mandatory STARTTLS (StartTls) on the non-SSL path so a stripped
            // STARTTLS capability fails the connection instead of silently downgrading
            // the IMAP login (username + decrypted password) to cleartext.
            var sslOptions = mailbox.UseSsl
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTls;

            // M11: configurable IMAP connect timeout. The connect-only window covers
            // the TCP handshake + TLS negotiation + LOGIN; subsequent operations use
            // the parent token (with operation timeout enforced separately).
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _imapSettings.ConnectTimeoutSeconds * 3)));

            await imapClient.ConnectAsync(mailbox.Host, mailbox.Port, sslOptions, connectCts.Token);

            // Decrypt password — fail fast on master key mismatch (v5.8: never use raw value)
            string password;
            try
            {
                password = credentialEncryptor.Decrypt(mailbox.EncryptedPassword);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to decrypt password for mailbox {Name}. " +
                    "This indicates a master key mismatch. Re-enter the password via the Web UI or restore the correct master.key file.",
                    mailbox.Name);
                throw new InvalidOperationException(
                    $"Credential decryption failed for mailbox '{mailbox.Name}'. Check the master key configuration.", ex);
            }

            await imapClient.AuthenticateAsync(mailbox.Username, password, connectCts.Token);
            imapConnected = true;
            Mail2SNMP.Infrastructure.Services.Mail2SnmpMetrics.ImapActiveConnections.Inc();

            _logger.LogDebug("Connected to IMAP server {Host}:{Port} for mailbox {Name}",
                mailbox.Host, mailbox.Port, mailbox.Name);

            // T6: Bound the folder open + search to ImapSettings.OperationTimeoutSeconds
            // (default 60 s). A hung IMAP server can otherwise block the consumer until
            // the parent stoppingToken fires (which only happens on graceful shutdown).
            using var opCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            opCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _imapSettings.OperationTimeoutSeconds)));

            // Open the configured folder
            var folder = await imapClient.GetFolderAsync(mailbox.Folder, opCts.Token);
            await folder.OpenAsync(FolderAccess.ReadWrite, opCts.Token);

            // Search for unseen messages
            var uids = await folder.SearchAsync(SearchQuery.NotSeen, opCts.Token);

            _logger.LogInformation("Found {Count} unseen emails in mailbox {Name}/{Folder}",
                uids.Count, mailbox.Name, mailbox.Folder);

            // PF-2: bound a single poll pass so a backlogged inbox cannot monopolize
            // this consumer task + IMAP slot for the entire drain. Oldest UIDs first;
            // the remainder stays unseen and is picked up by the next cycle.
            if (_imapSettings.MaxMessagesPerPoll > 0 && uids.Count > _imapSettings.MaxMessagesPerPoll)
            {
                // Verified fix: placeholder count must equal argument count — a
                // formatter-based log consumer string.Format-crashes on a mismatch.
                _logger.LogWarning(
                    "Mailbox {Name}: {Total} unseen messages exceed MaxMessagesPerPoll ({Cap}). " +
                    "Processing the oldest {Cap2}; the rest will be handled next cycle.",
                    mailbox.Name, uids.Count, _imapSettings.MaxMessagesPerPoll, _imapSettings.MaxMessagesPerPoll);
                uids = uids.Take(_imapSettings.MaxMessagesPerPoll).ToList();
            }

            var matchCount = 0;

            foreach (var uid in uids)
            {
                // H-2: per-message check is also read-only — most inspected mails do not
                // match the rule, and charging them starved noisy mailboxes of their
                // entire event budget before a single alert was raised.
                if (floodProtection.IsEventBudgetExhausted(job.Id, job.MaxEventsPerHour))
                {
                    Mail2SNMP.Infrastructure.Services.Mail2SnmpMetrics.RateLimitHits.WithLabels("events-per-hour").Inc();
                    _logger.LogWarning("Job {JobId} hit event rate limit during processing. Stopping.", job.Id);
                    break;
                }

                ct.ThrowIfCancellationRequested();

                try
                {
                    // PF-2: per-message fetch timeout. The parent token only fires on
                    // shutdown, so a single hung fetch could previously stall the
                    // consumer indefinitely. The timeout is caught HERE (not by the
                    // outer OperationCanceledException filters) so it skips only this
                    // message instead of masquerading as a shutdown.
                    using var msgCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    msgCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _imapSettings.OperationTimeoutSeconds)));
                    MimeKit.MimeMessage message;
                    try
                    {
                        message = await folder.GetMessageAsync(uid, msgCts.Token);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        _logger.LogWarning(
                            "Fetching message UID {Uid} from mailbox {Name} timed out after {Timeout}s. " +
                            "Leaving it unseen for the next cycle.",
                            uid, mailbox.Name, _imapSettings.OperationTimeoutSeconds);
                        continue;
                    }

                    // V8: bound untrusted email fields before they reach rule
                    // evaluation, the database, the UI and CSV exports. The DB
                    // column lengths (HasMaxLength) are a no-op on SQLite, so an
                    // attacker could otherwise store a multi-megabyte subject.
                    // 500/256 match the SQL Server column caps; the body is bounded
                    // to 1 MB for rule matching to prevent memory blow-up on a
                    // crafted giant message.
                    const int MaxSubjectChars = 500;
                    const int MaxFromChars = 256;
                    const int MaxBodyChars = 1_048_576;
                    var from = Truncate(message.From?.ToString() ?? string.Empty, MaxFromChars);
                    var subject = Truncate(message.Subject ?? string.Empty, MaxSubjectChars);
                    var body = Truncate(message.TextBody ?? message.HtmlBody ?? string.Empty, MaxBodyChars);

                    // O1: build a stable claim key. RFC 5322 makes the Message-ID
                    // header optional, so a small fraction of mails arrive without
                    // one. Without a fallback those mails would skip the N3 atomic-
                    // claim path entirely and a 4-node cluster would re-run rule
                    // evaluation + notifications four times for each (only the
                    // duplicate Event would be caught by EventDedup). The synthetic
                    // key is deterministic across nodes — every node sees the same
                    // (UID, internal-date) tuple from the same IMAP folder, so the
                    // ProcessedMails UNIQUE(MessageId, MailboxId) constraint still
                    // closes the race exactly like the headered case.
                    var messageId = message.MessageId;
                    var claimKey = !string.IsNullOrEmpty(messageId)
                        ? messageId
                        : $"synthetic:{uid}:{message.Date.UtcDateTime:yyyyMMddHHmmss}";

                    // N3 + O1: Atomic claim. Previously we did SELECT-then-process-
                    // then-INSERT, which left a TOCTOU window where two nodes could
                    // both pass the SELECT and run the entire processing pipeline
                    // before the UNIQUE constraint tripped on the second INSERT.
                    // Now we INSERT a placeholder ProcessedMail row FIRST and only
                    // proceed if the insert succeeded — the loser of the race
                    // catches the UNIQUE-constraint exception and skips the email
                    // entirely. The claim key falls back to a synthetic value when
                    // Message-ID is missing (see above).
                    // UC-5: the claim row doubles as the per-mail disposition record.
                    // Keep the tracked instance so the outcome can be stamped on it
                    // after rule evaluation and event creation complete.
                    // H-1: the claim is scoped to THIS job, so every job on the mailbox
                    // evaluates every message independently.
                    var claim = new ProcessedMail
                    {
                        MessageId = claimKey,
                        MailboxId = mailbox.Id,
                        JobId = job.Id,
                        From = from,
                        Subject = subject,
                        ReceivedUtc = message.Date.UtcDateTime,
                        ProcessedUtc = DateTime.UtcNow
                    };
                    dbContext.ProcessedMails.Add(claim);
                    try
                    {
                        await dbContext.SaveChangesAsync(ct);
                    }
                    catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        // Detach the failed insert so the next iteration starts clean.
                        foreach (var entry in dbContext.ChangeTracker.Entries<ProcessedMail>().ToList())
                            entry.State = EntityState.Detached;

                        // H-4: a claim row is NOT proof that the mail was handled — the row
                        // is committed before processing, so a transient failure mid-way
                        // leaves it behind with Disposition = Unknown. Treating that as
                        // "someone else did it" silently and permanently dropped the alert.
                        // Re-claim the incomplete row and process it now; only a stamped
                        // row (any disposition but Unknown) means the work is really done.
                        var existing = await dbContext.ProcessedMails
                            .FirstOrDefaultAsync(p => p.MessageId == claimKey
                                                   && p.MailboxId == mailbox.Id
                                                   && p.JobId == job.Id, ct);

                        if (existing is null || existing.Disposition != MailDisposition.Unknown)
                        {
                            Mail2SNMP.Infrastructure.Services.Mail2SnmpMetrics.EmailsDuplicate.Inc();
                            _logger.LogDebug(
                                "Email already processed for Job {JobId} (ClaimKey={ClaimKey}, Mailbox={Name}). Skipping.",
                                job.Id, claimKey, mailbox.Name);
                            await MarkSeenWhenAllJobsDoneAsync(dbContext, folder, uid, mailbox.Id, claimKey, ct);
                            continue;
                        }

                        _logger.LogWarning(
                            "Resuming mail {ClaimKey} for Job {JobId}: a previous attempt claimed it but never " +
                            "recorded an outcome (likely a transient failure). Re-processing.",
                            claimKey, job.Id);
                        existing.ProcessedUtc = DateTime.UtcNow;
                        claim = existing;
                    }

                    // Build headers dictionary
                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var header in message.Headers)
                    {
                        headers[header.Field] = header.Value;
                    }

                    // Evaluate rule against this email
                    var matched = ruleEvaluator.Evaluate(rule, from, subject, body, headers);

                    if (matched)
                    {
                        // H-2: THIS is where the hourly event budget is actually charged —
                        // one unit per event the job raises, which is what
                        // MaxEventsPerHour has always claimed to mean. If the budget is
                        // gone the mail is recorded as rate-limited (so the Mail Log can
                        // explain the missing alert) instead of vanishing.
                        if (floodProtection.IsEventRateLimited(job.Id, job.MaxEventsPerHour))
                        {
                            Mail2SNMP.Infrastructure.Services.Mail2SnmpMetrics.RateLimitHits.WithLabels("events-per-hour").Inc();
                            claim.Disposition = MailDisposition.RateLimited;
                            await dbContext.SaveChangesAsync(ct);
                            _logger.LogWarning(
                                "Job {JobId} reached its hourly event limit ({Max}); mail '{Subject}' was not raised as an event.",
                                job.Id, job.MaxEventsPerHour, subject);
                            await MarkSeenWhenAllJobsDoneAsync(dbContext, folder, uid, mailbox.Id, claimKey, ct);
                            continue;
                        }

                        matchCount++;
                        Mail2SNMP.Infrastructure.Services.Mail2SnmpMetrics.EmailsMatched.WithLabels(mailbox.Name, rule.Name).Inc();
                        // K3: per-mail logging is Debug — Information would explode the log under
                        // any meaningful mail volume. The end-of-batch summary at the bottom of
                        // FetchAndProcessEmailsAsync is what operators actually want to see.
                        _logger.LogDebug(
                            "Rule '{RuleName}' matched email (UID={Uid}, From={From}, Subject={Subject}) in Job {JobId}",
                            rule.Name, uid, from, subject, job.Id);

                        // Create event (EventService handles dedup via EventDedup table)
                        var evt = new Event
                        {
                            JobId = job.Id,
                            State = EventState.New,
                            Severity = rule.Severity,
                            RuleName = rule.Name,
                            Subject = subject,
                            MailFrom = from,
                            MessageId = messageId,
                            CreatedUtc = DateTime.UtcNow
                        };

                        evt = await eventService.CreateAsync(evt, ct);

                        _logger.LogDebug(
                            "Event {EventId} created for Job {JobId} (Rule: {RuleName}, Severity: {Severity})",
                            evt.Id, job.Id, rule.Name, evt.Severity);

                        // UC-5: stamp the disposition. HitCount > 1 means EventService
                        // collapsed this mail into an existing event (dedup) instead
                        // of creating a new one.
                        claim.EventId = evt.Id;
                        claim.Disposition = inMaintenance ? MailDisposition.MaintenanceSuppressed
                            : evt.HitCount > 1 ? MailDisposition.Deduplicated
                            : MailDisposition.EventCreated;

                        if (inMaintenance)
                        {
                            // During maintenance: suppress the event, skip notifications
                            await eventService.SuppressAsync(evt.Id, ct);
                            _logger.LogDebug(
                                "Event {EventId} suppressed during maintenance window for Job {JobId}",
                                evt.Id, job.Id);
                        }
                        else
                        {
                            // Send notifications through configured channels
                            await SendNotificationsAsync(job, rule, evt, from, subject,
                                notificationChannels, dedupCache, eventService, ct);
                        }
                    }
                    else
                    {
                        // UC-5: record the no-match outcome so a "why no alarm for
                        // mail X?" question is answerable from the Mail Log.
                        claim.Disposition = MailDisposition.NoMatch;
                    }

                    // UC-5: persist the disposition stamped above (the claim row was
                    // inserted before processing; this updates it with the outcome).
                    await dbContext.SaveChangesAsync(ct);

                    // H-1: flag Seen only once EVERY active job on this mailbox has
                    // processed the message — otherwise the first job to finish would
                    // hide the mail from its siblings' NotSeen search.
                    await MarkSeenWhenAllJobsDoneAsync(dbContext, folder, uid, mailbox.Id, claimKey, ct);
                    Mail2SNMP.Infrastructure.Services.Mail2SnmpMetrics.EmailsProcessed.WithLabels(mailbox.Name).Inc();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Error processing email UID {Uid} in mailbox {Name}", uid, mailbox.Name);
                }
            }

            _logger.LogInformation(
                "Processed {Total} emails, {Matched} matched rule '{RuleName}' for Job {JobId}",
                uids.Count, matchCount, rule.Name, job.Id);

            // Update last checked timestamp
            mailbox.LastCheckedUtc = DateTime.UtcNow;
            mailbox.LastError = null;
            await mailboxService.UpdateAsync(mailbox, ct);

            // Use a short timeout token derived from the caller so a hanging IMAP server
            // cannot block the worker shutdown indefinitely.
            using var disconnectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            disconnectCts.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                await imapClient.DisconnectAsync(true, disconnectCts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("IMAP disconnect timed out for mailbox {Name}; closing socket.", mailbox.Name);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // AR-1: a failure before the connected flag was set is a connect/auth error.
            if (!imapConnected)
                Mail2SNMP.Infrastructure.Services.Mail2SnmpMetrics.ImapConnectionErrors.WithLabels(mailbox.Name).Inc();

            _logger.LogError(ex, "IMAP processing failed for mailbox {Name}: {Error}", mailbox.Name, ex.Message);

            // Update mailbox with error info
            try
            {
                mailbox.LastCheckedUtc = DateTime.UtcNow;
                mailbox.LastError = ex.Message;
                await mailboxService.UpdateAsync(mailbox, ct);
            }
            catch (Exception updateEx)
            {
                _logger.LogWarning(updateEx, "Failed to update mailbox error status for {Name}", mailbox.Name);
            }

            throw;
        }
        finally
        {
            if (imapConnected)
                Mail2SNMP.Infrastructure.Services.Mail2SnmpMetrics.ImapActiveConnections.Dec();
        }
    }

    /// <summary>
    /// Sends notifications to the job's assigned SNMP and Webhook targets,
    /// and transitions the event state to Notified once at least one succeeds.
    /// </summary>
    private async Task SendNotificationsAsync(
        Job job, Rule rule, Event evt,
        string from, string subject,
        IEnumerable<INotificationChannel> notificationChannels,
        NotificationDedupCache dedupCache,
        IEventService eventService,
        CancellationToken ct)
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

        var anySuccess = false;
        var channels = notificationChannels.ToList();
        var snmpChannel = channels.FirstOrDefault(c => c.ChannelName == INotificationChannel.Snmp);
        var webhookChannel = channels.FirstOrDefault(c => c.ChannelName == INotificationChannel.Webhook);

        // FN-3: the channels report a delivery outcome. anySuccess is only set when a
        // notification actually left the process (or an earlier one already covered
        // the event/target pair), so an event whose every send silently failed —
        // e.g. a v3 target under a Community license, DNS failure, master-key drift —
        // stays New instead of being falsely marked Notified.

        // Send to assigned SNMP targets.
        // UC-4: severity routing — targets only receive events at or above their
        // configured MinSeverity ("page the NOC only for Critical").
        foreach (var jst in job.JobSnmpTargets.Where(t =>
                     t.SnmpTarget.IsActive && evt.Severity >= t.SnmpTarget.MinSeverity))
        {
            try
            {
                if (snmpChannel != null &&
                    await snmpChannel.SendToSnmpTargetAsync(context, jst.SnmpTarget, ct))
                {
                    anySuccess = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send SNMP trap to {Target} for Event {EventId}",
                    jst.SnmpTarget.Name, evt.Id);
            }
        }

        // Send to assigned Webhook targets (UC-4: same severity routing as above).
        foreach (var jwt in job.JobWebhookTargets.Where(t =>
                     t.WebhookTarget.IsActive && evt.Severity >= t.WebhookTarget.MinSeverity))
        {
            try
            {
                if (webhookChannel != null &&
                    await webhookChannel.SendToWebhookTargetAsync(context, jwt.WebhookTarget, ct))
                {
                    anySuccess = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send webhook to {Target} for Event {EventId}",
                    jwt.WebhookTarget.Name, evt.Id);
            }
        }

        // Transition event state to Notified after at least one channel succeeded
        if (anySuccess && evt.State == EventState.New)
        {
            try
            {
                await eventService.MarkAsNotifiedAsync(evt.Id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to mark Event {EventId} as Notified", evt.Id);
            }
        }
    }

    /// <summary>
    /// H-1: flags an IMAP message <c>Seen</c> only once every currently-active job on the
    /// mailbox has recorded a completed claim for it.
    /// </summary>
    /// <remarks>
    /// The IMAP <c>Seen</c> flag is shared state across all jobs polling a mailbox: the
    /// poll query is <c>SearchQuery.NotSeen</c>, so whichever job flags the message first
    /// hides it from every sibling job. Deferring the flag until the completed-claim count
    /// reaches the active-job count lets several rules share one mailbox. Deactivating or
    /// deleting a job lowers the expected count, so a message can never be stranded unseen
    /// by a job that no longer exists.
    /// </remarks>
    private async Task MarkSeenWhenAllJobsDoneAsync(
        Mail2SnmpDbContext dbContext, IMailFolder folder, UniqueId uid,
        int mailboxId, string claimKey, CancellationToken ct)
    {
        var activeJobs = await dbContext.Jobs
            .CountAsync(j => j.MailboxId == mailboxId && j.IsActive, ct);

        var completedClaims = await dbContext.ProcessedMails
            .CountAsync(p => p.MailboxId == mailboxId
                          && p.MessageId == claimKey
                          && p.Disposition != MailDisposition.Unknown, ct);

        if (completedClaims >= activeJobs)
        {
            await folder.AddFlagsAsync(uid, MessageFlags.Seen, true, ct);
        }
        else
        {
            _logger.LogDebug(
                "Mail {ClaimKey} stays unseen: {Done}/{Total} active jobs on mailbox {MailboxId} have processed it.",
                claimKey, completedClaims, activeJobs, mailboxId);
        }
    }

    // V8: hard cap on untrusted string length. Returns the input unchanged when
    // within the limit, otherwise the first <paramref name="max"/> characters.
    private static string Truncate(string value, int max)
        => string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max];
}
