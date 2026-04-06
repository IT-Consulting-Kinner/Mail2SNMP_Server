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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MimeKit;
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

    public MailPollingService(
        Channel<MailWorkItem> channel,
        IServiceScopeFactory scopeFactory,
        ILogger<MailPollingService> logger,
        IConfiguration configuration)
    {
        _channel = channel;
        _scopeFactory = scopeFactory;
        _logger = logger;

        _imapSettings = configuration
            .GetSection("Imap")
            .Get<ImapSettings>() ?? new ImapSettings();

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

            // Check flood protection
            if (floodProtection.IsEventRateLimited(job.Id, job.MaxEventsPerHour))
            {
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

        try
        {
            // Connect to mailbox
            var sslOptions = mailbox.UseSsl
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTlsWhenAvailable;

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

            var matchCount = 0;

            foreach (var uid in uids)
            {
                // Re-check flood protection per message
                if (floodProtection.IsEventRateLimited(job.Id, job.MaxEventsPerHour))
                {
                    _logger.LogWarning("Job {JobId} hit event rate limit during processing. Stopping.", job.Id);
                    break;
                }

                ct.ThrowIfCancellationRequested();

                try
                {
                    var message = await folder.GetMessageAsync(uid, ct);

                    var from = message.From?.ToString() ?? string.Empty;
                    var subject = message.Subject ?? string.Empty;
                    var body = message.TextBody ?? message.HtmlBody ?? string.Empty;
                    var messageId = message.MessageId;

                    // Idempotency: skip emails already processed (prevents duplicates across cluster instances)
                    if (!string.IsNullOrEmpty(messageId))
                    {
                        var alreadyProcessed = await dbContext.ProcessedMails
                            .AnyAsync(p => p.MessageId == messageId && p.MailboxId == mailbox.Id, ct);
                        if (alreadyProcessed)
                        {
                            _logger.LogDebug(
                                "Email already processed (MessageId={MessageId}, Mailbox={Name}). Skipping.",
                                messageId, mailbox.Name);
                            await folder.AddFlagsAsync(uid, MessageFlags.Seen, true, ct);
                            continue;
                        }
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
                        matchCount++;
                        _logger.LogInformation(
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

                        _logger.LogInformation(
                            "Event {EventId} created for Job {JobId} (Rule: {RuleName}, Severity: {Severity})",
                            evt.Id, job.Id, rule.Name, evt.Severity);

                        if (inMaintenance)
                        {
                            // During maintenance: suppress the event, skip notifications
                            await eventService.SuppressAsync(evt.Id, ct);
                            _logger.LogInformation(
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

                    // Record this email as processed (idempotency across cluster instances)
                    if (!string.IsNullOrEmpty(messageId))
                    {
                        dbContext.ProcessedMails.Add(new ProcessedMail
                        {
                            MessageId = messageId,
                            MailboxId = mailbox.Id,
                            From = from,
                            Subject = subject,
                            ReceivedUtc = message.Date.UtcDateTime,
                            ProcessedUtc = DateTime.UtcNow
                        });

                        try
                        {
                            await dbContext.SaveChangesAsync(ct);
                        }
                        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            // Another instance already recorded this message — safe to ignore
                            _logger.LogDebug(
                                "ProcessedMail duplicate insert (MessageId={MessageId}, Mailbox={Name}). Another instance processed it.",
                                messageId, mailbox.Name);
                        }
                    }

                    // Mark message as seen after processing
                    await folder.AddFlagsAsync(uid, MessageFlags.Seen, true, ct);
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
        var snmpChannel = channels.FirstOrDefault(c => c.ChannelName == "snmp");
        var webhookChannel = channels.FirstOrDefault(c => c.ChannelName == "webhook");

        // Send to assigned SNMP targets
        foreach (var jst in job.JobSnmpTargets.Where(t => t.SnmpTarget.IsActive))
        {
            try
            {
                if (snmpChannel != null)
                {
                    await snmpChannel.SendToSnmpTargetAsync(context, jst.SnmpTarget, ct);
                    anySuccess = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send SNMP trap to {Target} for Event {EventId}",
                    jst.SnmpTarget.Name, evt.Id);
            }
        }

        // Send to assigned Webhook targets
        foreach (var jwt in job.JobWebhookTargets.Where(t => t.WebhookTarget.IsActive))
        {
            try
            {
                if (webhookChannel != null)
                {
                    await webhookChannel.SendToWebhookTargetAsync(context, jwt.WebhookTarget, ct);
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
}
