using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Core.Services;
using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Infrastructure.Services;
using Mail2SNMP.Models.DTOs;
using Mail2SNMP.Models.Entities;
using Mail2SNMP.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Mail2SNMP.Tests.Infrastructure;

/// <summary>
/// Review finding: the four features shipped in 1.1.0 — UC-4 severity routing, UC-5
/// per-mail disposition, UC-3 SNMP dead-lettering and UC-7 Test Send — went out with no
/// automated coverage at all. These tests pin their observable contracts so a later
/// refactor cannot quietly undo them.
/// </summary>
public class Release110FeatureTests : IDisposable
{
    private readonly Mail2SnmpDbContext _db;
    private readonly IAuditService _audit = Substitute.For<IAuditService>();
    private readonly ILicenseProvider _license = Substitute.For<ILicenseProvider>();

    /// <summary>Records which targets a channel was actually asked to deliver to.</summary>
    private sealed class RecordingChannel : INotificationChannel
    {
        public RecordingChannel(string channelName, bool succeed = true)
        {
            ChannelName = channelName;
            _succeed = succeed;
        }

        private readonly bool _succeed;
        public string ChannelName { get; }
        public List<string> SnmpSends { get; } = new();
        public List<string> WebhookSends { get; } = new();
        public List<long> EventIds { get; } = new();

        public Task<bool> SendToSnmpTargetAsync(NotificationContext context, SnmpTarget target, CancellationToken ct = default)
        {
            SnmpSends.Add(target.Name);
            EventIds.Add(context.EventId);
            return Task.FromResult(_succeed);
        }

        public Task<bool> SendToWebhookTargetAsync(NotificationContext context, WebhookTarget target, CancellationToken ct = default)
        {
            WebhookSends.Add(target.Name);
            EventIds.Add(context.EventId);
            return Task.FromResult(_succeed);
        }
    }

    public Release110FeatureTests()
    {
        var options = new DbContextOptionsBuilder<Mail2SnmpDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new Mail2SnmpDbContext(options);
        _db.Database.EnsureCreated();
        _license.IsEnterprise().Returns(true);
    }

    public void Dispose() => _db.Dispose();

    /// <summary>
    /// Seeds a job with one SNMP and one webhook target, each at the given
    /// <c>MinSeverity</c>, and returns the job id.
    /// </summary>
    private async Task<int> SeedJobWithTargetsAsync(Severity snmpMin, Severity webhookMin, bool snmpActive = true)
    {
        var mailbox = new Mailbox { Name = "MB", Host = "h", Username = "u" };
        var rule = new Rule { Name = "R", Field = RuleFieldType.Subject, MatchType = RuleMatchType.Contains, Criteria = "t" };
        _db.Mailboxes.Add(mailbox);
        _db.Rules.Add(rule);
        await _db.SaveChangesAsync();

        var job = new Job { Name = "J", MailboxId = mailbox.Id, RuleId = rule.Id };
        var snmp = new SnmpTarget { Name = "NOC", Host = "127.0.0.1", Port = 162, MinSeverity = snmpMin, IsActive = snmpActive };
        var webhook = new WebhookTarget { Name = "Chat", Url = "https://example.invalid/hook", MinSeverity = webhookMin };
        _db.Jobs.Add(job);
        _db.SnmpTargets.Add(snmp);
        _db.WebhookTargets.Add(webhook);
        await _db.SaveChangesAsync();

        _db.JobSnmpTargets.Add(new JobSnmpTarget { JobId = job.Id, SnmpTargetId = snmp.Id });
        _db.JobWebhookTargets.Add(new JobWebhookTarget { JobId = job.Id, WebhookTargetId = webhook.Id });
        await _db.SaveChangesAsync();
        return job.Id;
    }

    private JobService NewJobService(params INotificationChannel[] channels) =>
        new(_db, _license, _audit, new RuleEvaluator(NullLogger<RuleEvaluator>.Instance), channels, NullLogger<JobService>.Instance);

    private EventService NewEventService(params INotificationChannel[] channels) =>
        new(_db, _audit, channels, NullLogger<EventService>.Instance);

    // ---------------------------------------------------------------- UC-4

    [Fact]
    public async Task UC4_Replay_SkipsTargetsWhoseMinSeverityExceedsTheEvent()
    {
        // "Page the NOC only for Critical": the SNMP target demands Critical, the chat
        // webhook accepts anything.
        var jobId = await SeedJobWithTargetsAsync(snmpMin: Severity.Critical, webhookMin: Severity.Information);
        var snmpChannel = new RecordingChannel(INotificationChannel.Snmp);
        var webhookChannel = new RecordingChannel(INotificationChannel.Webhook);
        var svc = NewEventService(snmpChannel, webhookChannel);

        var evt = await svc.CreateAsync(new Event { JobId = jobId, Severity = Severity.Warning, Subject = "x" });
        await svc.ReplayAsync(evt.Id);

        Assert.Empty(snmpChannel.SnmpSends);
        Assert.Equal(new[] { "Chat" }, webhookChannel.WebhookSends);
    }

    [Fact]
    public async Task UC4_Replay_DeliversWhenSeverityMeetsTheThreshold()
    {
        var jobId = await SeedJobWithTargetsAsync(snmpMin: Severity.Critical, webhookMin: Severity.Information);
        var snmpChannel = new RecordingChannel(INotificationChannel.Snmp);
        var webhookChannel = new RecordingChannel(INotificationChannel.Webhook);
        var svc = NewEventService(snmpChannel, webhookChannel);

        var evt = await svc.CreateAsync(new Event { JobId = jobId, Severity = Severity.Critical, Subject = "x" });
        await svc.ReplayAsync(evt.Id);

        // Equal severity must pass — the comparison is >=, not >.
        Assert.Equal(new[] { "NOC" }, snmpChannel.SnmpSends);
        Assert.Equal(new[] { "Chat" }, webhookChannel.WebhookSends);
    }

    [Fact]
    public async Task UC4_Replay_SkipsInactiveTargetRegardlessOfSeverity()
    {
        var jobId = await SeedJobWithTargetsAsync(
            snmpMin: Severity.Information, webhookMin: Severity.Information, snmpActive: false);
        var snmpChannel = new RecordingChannel(INotificationChannel.Snmp);
        var svc = NewEventService(snmpChannel, new RecordingChannel(INotificationChannel.Webhook));

        var evt = await svc.CreateAsync(new Event { JobId = jobId, Severity = Severity.Critical, Subject = "x" });
        await svc.ReplayAsync(evt.Id);

        Assert.Empty(snmpChannel.SnmpSends);
    }

    // ---------------------------------------------------------------- UC-7

    [Fact]
    public async Task UC7_TestSend_ReportsPerTargetOutcomeAndUsesASyntheticEventId()
    {
        var jobId = await SeedJobWithTargetsAsync(snmpMin: Severity.Information, webhookMin: Severity.Information);
        var snmpChannel = new RecordingChannel(INotificationChannel.Snmp);
        var webhookChannel = new RecordingChannel(INotificationChannel.Webhook);

        var report = await NewJobService(snmpChannel, webhookChannel).SendTestEventAsync(jobId);

        Assert.Contains("2 delivered, 0 failed", report);
        Assert.Contains("SNMP  NOC", report);
        Assert.Contains("HTTP  Chat: delivered", report);
        // A negative id cannot collide with a real event, which is what keeps the
        // notification-dedup cache from swallowing a second click — and what keeps the
        // channels from dead-lettering a test failure against a non-existent Events row.
        Assert.All(snmpChannel.EventIds, id => Assert.True(id < 0));
        Assert.All(webhookChannel.EventIds, id => Assert.True(id < 0));
    }

    [Fact]
    public async Task UC7_TestSend_ExplainsSeveritySkipsInsteadOfStayingSilent()
    {
        // The synthetic event is Severity.Information, so a Critical-only target is
        // skipped. Silently omitting it would look like a broken test-send.
        var jobId = await SeedJobWithTargetsAsync(snmpMin: Severity.Critical, webhookMin: Severity.Critical);
        var snmpChannel = new RecordingChannel(INotificationChannel.Snmp);
        var webhookChannel = new RecordingChannel(INotificationChannel.Webhook);

        var report = await NewJobService(snmpChannel, webhookChannel).SendTestEventAsync(jobId);

        Assert.Contains("SNMP  NOC: skipped (target requires >= Critical)", report);
        Assert.Contains("HTTP  Chat: skipped (target requires >= Critical)", report);
        Assert.Empty(snmpChannel.SnmpSends);
        Assert.Empty(webhookChannel.WebhookSends);
        // A report full of skips must not also claim there were no targets at all.
        Assert.DoesNotContain("No active targets", report);
    }

    [Fact]
    public async Task UC7_TestSend_ReportsFailureWhenAChannelDoesNotDeliver()
    {
        var jobId = await SeedJobWithTargetsAsync(snmpMin: Severity.Information, webhookMin: Severity.Information);
        var snmpChannel = new RecordingChannel(INotificationChannel.Snmp, succeed: false);
        var webhookChannel = new RecordingChannel(INotificationChannel.Webhook, succeed: false);

        var report = await NewJobService(snmpChannel, webhookChannel).SendTestEventAsync(jobId);

        Assert.Contains("0 delivered, 2 failed", report);
        Assert.Contains("FAILED", report);
    }

    [Fact]
    public async Task UC7_TestSend_SaysSoWhenTheJobHasNoTargets()
    {
        var mailbox = new Mailbox { Name = "MB", Host = "h", Username = "u" };
        var rule = new Rule { Name = "R", Field = RuleFieldType.Subject, MatchType = RuleMatchType.Contains, Criteria = "t" };
        _db.Mailboxes.Add(mailbox);
        _db.Rules.Add(rule);
        await _db.SaveChangesAsync();
        var job = new Job { Name = "Lonely", MailboxId = mailbox.Id, RuleId = rule.Id };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();

        var report = await NewJobService(new RecordingChannel(INotificationChannel.Snmp)).SendTestEventAsync(job.Id);

        Assert.Contains("No active targets are assigned to this job", report);
    }

    // ---------------------------------------------------------------- UC-5

    [Fact]
    public async Task UC5_Disposition_AndEventLink_RoundTrip()
    {
        var mailbox = new Mailbox { Name = "MB", Host = "h", Username = "u" };
        _db.Mailboxes.Add(mailbox);
        await _db.SaveChangesAsync();

        _db.ProcessedMails.Add(new ProcessedMail
        {
            MailboxId = mailbox.Id,
            MessageId = "<a@x>",
            Disposition = MailDisposition.Deduplicated,
            EventId = 4711
        });
        await _db.SaveChangesAsync();

        var stored = await _db.ProcessedMails.AsNoTracking().SingleAsync();
        Assert.Equal(MailDisposition.Deduplicated, stored.Disposition);
        // Deliberately not a foreign key: retention purges events on a different
        // schedule, and the trace must outlive the event it points at.
        Assert.Equal(4711, stored.EventId);
    }

    [Fact]
    public async Task UC5_LegacyRowsDefaultToUnknownRatherThanClaimingAnOutcome()
    {
        var mailbox = new Mailbox { Name = "MB", Host = "h", Username = "u" };
        _db.Mailboxes.Add(mailbox);
        await _db.SaveChangesAsync();

        _db.ProcessedMails.Add(new ProcessedMail { MailboxId = mailbox.Id, MessageId = "<legacy@x>" });
        await _db.SaveChangesAsync();

        var stored = await _db.ProcessedMails.AsNoTracking().SingleAsync();
        // Unknown is also the in-flight claim state the poller re-processes after a
        // crash, so it must never be conflated with "no match".
        Assert.Equal(MailDisposition.Unknown, stored.Disposition);
        Assert.Null(stored.EventId);
    }

    [Fact]
    public async Task UC5_ClaimIsPerJob_SoSeveralJobsOnOneMailboxEachSeeTheMail()
    {
        var mailbox = new Mailbox { Name = "MB", Host = "h", Username = "u" };
        var rule = new Rule { Name = "R", Field = RuleFieldType.Subject, MatchType = RuleMatchType.Contains, Criteria = "t" };
        _db.Mailboxes.Add(mailbox);
        _db.Rules.Add(rule);
        await _db.SaveChangesAsync();
        var jobA = new Job { Name = "A", MailboxId = mailbox.Id, RuleId = rule.Id };
        var jobB = new Job { Name = "B", MailboxId = mailbox.Id, RuleId = rule.Id };
        _db.Jobs.AddRange(jobA, jobB);
        await _db.SaveChangesAsync();

        _db.ProcessedMails.Add(new ProcessedMail
        {
            MailboxId = mailbox.Id, JobId = jobA.Id, MessageId = "<same@x>",
            Disposition = MailDisposition.EventCreated
        });
        _db.ProcessedMails.Add(new ProcessedMail
        {
            MailboxId = mailbox.Id, JobId = jobB.Id, MessageId = "<same@x>",
            Disposition = MailDisposition.NoMatch
        });
        await _db.SaveChangesAsync();

        // The claim key is (MessageId, MailboxId, JobId). When it was (MessageId,
        // MailboxId) the first job to poll won and every other job on that mailbox
        // silently never fired — so the mail log could only ever show one outcome
        // per mail instead of one per job.
        var traces = await _db.ProcessedMails.AsNoTracking()
            .Where(p => p.MessageId == "<same@x>").ToListAsync();
        Assert.Equal(2, traces.Count);
        Assert.Contains(traces, t => t.JobId == jobA.Id && t.Disposition == MailDisposition.EventCreated);
        Assert.Contains(traces, t => t.JobId == jobB.Id && t.Disposition == MailDisposition.NoMatch);
    }

    // ---------------------------------------------------------------- UC-3

    [Fact]
    public async Task UC3_SnmpDeadLetter_IsRecordedAndMappedWithoutLeakingCredentials()
    {
        var mailbox = new Mailbox { Name = "MB", Host = "h", Username = "u" };
        var rule = new Rule { Name = "R", Field = RuleFieldType.Subject, MatchType = RuleMatchType.Contains, Criteria = "t" };
        _db.Mailboxes.Add(mailbox);
        _db.Rules.Add(rule);
        await _db.SaveChangesAsync();
        var job = new Job { Name = "J", MailboxId = mailbox.Id, RuleId = rule.Id };
        var snmp = new SnmpTarget
        {
            Name = "NOC", Host = "127.0.0.1", Port = 162,
            EncryptedCommunityString = "ciphertext-community",
            EncryptedAuthPassword = "ciphertext-auth",
            EncryptedPrivPassword = "ciphertext-priv"
        };
        _db.Jobs.Add(job);
        _db.SnmpTargets.Add(snmp);
        await _db.SaveChangesAsync();
        var evt = new Event { JobId = job.Id, Severity = Severity.Critical, Subject = "s" };
        _db.Events.Add(evt);
        await _db.SaveChangesAsync();

        var svc = new DeadLetterService(_db, NullLogger<DeadLetterService>.Instance);
        var created = await svc.CreateAsync(new DeadLetterEntry
        {
            SnmpTargetId = snmp.Id,
            EventId = evt.Id,
            PayloadJson = "{}",
            LastError = "No route to host",
            AttemptCount = 1,
            Status = DeadLetterStatus.Pending
        });

        // Before UC-3 a failed trap was logged and dropped; only webhooks had a queue.
        Assert.NotNull(created.NextRetryUtc);
        Assert.Null(created.WebhookTargetId);

        var reloaded = (await svc.QueryAsync(new DeadLetterQuery { Kind = DeadLetterTargetKind.Snmp })).Entries.Single();
        var dto = reloaded.ToResponse();
        Assert.Equal("snmp", dto.Kind);
        Assert.Equal("NOC", dto.TargetName);
        // The endpoint used to serialize the entity graph, which dragged the target's
        // ciphertexts into the JSON for any Operator.
        var json = System.Text.Json.JsonSerializer.Serialize(dto);
        Assert.DoesNotContain("ciphertext", json, StringComparison.OrdinalIgnoreCase);
    }
}
