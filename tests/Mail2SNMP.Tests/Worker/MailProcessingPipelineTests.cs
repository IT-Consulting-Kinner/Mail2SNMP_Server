using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Core.Services;
using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Infrastructure.Services;
using Mail2SNMP.Models.DTOs;
using Mail2SNMP.Models.Entities;
using Mail2SNMP.Models.Enums;
using Mail2SNMP.Worker.Models;
using Mail2SNMP.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Mail2SNMP.Tests.Worker;

/// <summary>
/// Characterization tests for <see cref="MailProcessingPipeline"/> — everything that
/// happens to one inbound mail.
/// </summary>
/// <remarks>
/// <para>
/// This logic used to be inlined in <c>MailPollingService.FetchAndProcessEmailsAsync</c>,
/// a 330-line method behind a live <c>ImapClient</c>. Being unreachable from a test is why
/// three defects survived several reviews there: the mail claim was keyed per mailbox
/// rather than per job, the hourly event budget was charged per inspected mail rather than
/// per raised event, and a claim row was treated as proof of completion even when the run
/// that wrote it had died part-way through.
/// </para>
/// <para>
/// These run against a real SQLite file, not the in-memory provider: the claim's whole
/// purpose is the UNIQUE constraint on (MessageId, MailboxId, JobId), and the in-memory
/// provider does not enforce constraints — a test written against it would pass no matter
/// how the claim key were keyed.
/// </para>
/// </remarks>
public class MailProcessingPipelineTests : IDisposable
{
    private readonly Mail2SnmpDbContext _db;
    private readonly string _dbPath;
    private readonly IAuditService _audit = Substitute.For<IAuditService>();

    /// <summary>Notification channel that records its calls and reports a configurable outcome.</summary>
    private sealed class RecordingChannel : INotificationChannel
    {
        public RecordingChannel(string channelName, bool succeed = true)
        {
            ChannelName = channelName;
            _succeed = succeed;
        }

        private readonly bool _succeed;
        public string ChannelName { get; }
        public List<string> Sent { get; } = new();

        public Task<bool> SendToSnmpTargetAsync(NotificationContext c, SnmpTarget t, CancellationToken ct = default)
        {
            Sent.Add(t.Name);
            return Task.FromResult(_succeed);
        }

        public Task<bool> SendToWebhookTargetAsync(NotificationContext c, WebhookTarget t, CancellationToken ct = default)
        {
            Sent.Add(t.Name);
            return Task.FromResult(_succeed);
        }
    }

    public MailProcessingPipelineTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"m2s-pipeline-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<Mail2SnmpDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        _db = new Mail2SnmpDbContext(options);
        _db.Database.Migrate();
    }

    public void Dispose()
    {
        _db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch (IOException) { /* best effort */ }
    }

    private Mailbox? _mailbox;
    private Rule? _rule;

    /// <summary>Seeds a mailbox, a "contains ALERT" subject rule, and one job wired to an SNMP target.</summary>
    private async Task<Job> SeedAsync(
        Severity ruleSeverity = Severity.Error,
        int maxEventsPerHour = 100,
        int dedupWindowMinutes = 0,
        string jobName = "J")
    {
        // Shared across the jobs a test seeds — several jobs on one mailbox is the
        // configuration most of these tests are about.
        if (_mailbox is null)
        {
            _mailbox = new Mailbox { Name = "MB", Host = "h", Username = "u", EncryptedPassword = "p", Folder = "INBOX" };
            _rule = new Rule
            {
                Name = "R", Field = RuleFieldType.Subject, MatchType = RuleMatchType.Contains,
                Criteria = "ALERT", Severity = ruleSeverity, IsActive = true
            };
            _db.Mailboxes.Add(_mailbox);
            _db.Rules.Add(_rule);
            await _db.SaveChangesAsync();
        }

        var job = new Job
        {
            Name = jobName, MailboxId = _mailbox!.Id, RuleId = _rule!.Id, IsActive = true,
            MaxEventsPerHour = maxEventsPerHour, DedupWindowMinutes = dedupWindowMinutes
        };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();

        var target = new SnmpTarget
        {
            Name = $"NOC-{jobName}", Host = "127.0.0.1", Port = 162,
            IsActive = true, MinSeverity = Severity.Information
        };
        _db.SnmpTargets.Add(target);
        await _db.SaveChangesAsync();
        _db.JobSnmpTargets.Add(new JobSnmpTarget { JobId = job.Id, SnmpTargetId = target.Id });
        await _db.SaveChangesAsync();

        // The pipeline reads the target assignments off the job instance it is handed.
        return await _db.Jobs
            .Include(j => j.Mailbox)
            .Include(j => j.JobSnmpTargets).ThenInclude(t => t.SnmpTarget)
            .Include(j => j.JobWebhookTargets).ThenInclude(t => t.WebhookTarget)
            .FirstAsync(j => j.Id == job.Id);
    }

    private MailProcessingPipeline NewPipeline(params INotificationChannel[] channels)
        => new(_db,
               new RuleEvaluator(NullLogger<RuleEvaluator>.Instance),
               new EventService(_db, _audit, channels, NullLogger<EventService>.Instance),
               new FloodProtectionService(NullLogger<FloodProtectionService>.Instance),
               channels,
               NullLogger<MailProcessingPipeline>.Instance);

    private static InboundMail Mail(string subject, string claimKey = "<m1@x>") => new(
        ClaimKey: claimKey,
        MessageId: claimKey,
        From: "sender@example.invalid",
        Subject: subject,
        Body: "body",
        ReceivedUtc: new DateTime(2026, 8, 17, 3, 14, 0, DateTimeKind.Utc),
        Headers: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    // ------------------------------------------------------------ dispositions

    [Fact]
    public async Task NoMatch_IsRecordedRatherThanForgotten()
    {
        var job = await SeedAsync();
        var channel = new RecordingChannel(INotificationChannel.Snmp);

        var outcome = await NewPipeline(channel).ProcessAsync(
            job, _mailbox!, _rule!, Mail("nothing interesting"), inMaintenance: false,
            markSeenAsync: _ => Task.CompletedTask, CancellationToken.None);

        Assert.Equal(MailDisposition.NoMatch, outcome.Disposition);
        Assert.Null(outcome.EventId);
        Assert.Empty(channel.Sent);
        // The claim row is the Mail Log entry: without it "why did I get no alarm for
        // mail X?" is only answerable from the server log.
        var claim = await _db.ProcessedMails.AsNoTracking().SingleAsync();
        Assert.Equal(MailDisposition.NoMatch, claim.Disposition);
        Assert.Equal(job.Id, claim.JobId);
    }

    [Fact]
    public async Task Match_CreatesEvent_Notifies_AndMarksNotified()
    {
        var job = await SeedAsync();
        var channel = new RecordingChannel(INotificationChannel.Snmp);

        var outcome = await NewPipeline(channel).ProcessAsync(
            job, _mailbox!, _rule!, Mail("ALERT disk full"), inMaintenance: false,
            markSeenAsync: _ => Task.CompletedTask, CancellationToken.None);

        Assert.Equal(MailDisposition.EventCreated, outcome.Disposition);
        Assert.NotNull(outcome.EventId);
        Assert.Equal(new[] { $"NOC-{job.Name}" }, channel.Sent);

        var evt = await _db.Events.AsNoTracking().SingleAsync();
        Assert.Equal(EventState.Notified, evt.State);
        var claim = await _db.ProcessedMails.AsNoTracking().SingleAsync();
        Assert.Equal(evt.Id, claim.EventId);
    }

    [Fact]
    public async Task Match_WhenEveryChannelFails_LeavesTheEventNew()
    {
        var job = await SeedAsync();
        var channel = new RecordingChannel(INotificationChannel.Snmp, succeed: false);

        await NewPipeline(channel).ProcessAsync(
            job, _mailbox!, _rule!, Mail("ALERT disk full"), inMaintenance: false,
            markSeenAsync: _ => Task.CompletedTask, CancellationToken.None);

        // Marking an event Notified when nothing left the process is the failure mode
        // that hides a dead notification path from every dashboard.
        var evt = await _db.Events.AsNoTracking().SingleAsync();
        Assert.Equal(EventState.New, evt.State);
    }

    [Fact]
    public async Task Match_DuringMaintenance_SuppressesTheEventAndSendsNothing()
    {
        var job = await SeedAsync();
        var channel = new RecordingChannel(INotificationChannel.Snmp);

        var outcome = await NewPipeline(channel).ProcessAsync(
            job, _mailbox!, _rule!, Mail("ALERT disk full"), inMaintenance: true,
            markSeenAsync: _ => Task.CompletedTask, CancellationToken.None);

        Assert.Equal(MailDisposition.MaintenanceSuppressed, outcome.Disposition);
        Assert.Empty(channel.Sent);
        var evt = await _db.Events.AsNoTracking().SingleAsync();
        Assert.Equal(EventState.Suppressed, evt.State);
    }

    [Fact]
    public async Task SecondMatchInsideTheDedupWindow_IsRecordedAsDeduplicated()
    {
        var job = await SeedAsync(dedupWindowMinutes: 60);
        var pipeline = NewPipeline(new RecordingChannel(INotificationChannel.Snmp));

        await pipeline.ProcessAsync(job, _mailbox!, _rule!, Mail("ALERT disk full", "<a@x>"),
            inMaintenance: false, _ => Task.CompletedTask, CancellationToken.None);
        var second = await pipeline.ProcessAsync(job, _mailbox!, _rule!, Mail("ALERT disk full", "<b@x>"),
            inMaintenance: false, _ => Task.CompletedTask, CancellationToken.None);

        // Same subject/sender/job inside the window collapses into the existing event.
        Assert.Equal(MailDisposition.Deduplicated, second.Disposition);
        Assert.Single(await _db.Events.AsNoTracking().ToListAsync());
    }

    // ------------------------------------------------------------ rate limiting

    [Fact]
    public async Task BudgetIsChargedPerRaisedEvent_NotPerInspectedMail()
    {
        var job = await SeedAsync(maxEventsPerHour: 1);
        var pipeline = NewPipeline(new RecordingChannel(INotificationChannel.Snmp));

        // Three non-matching mails must not touch the budget. Charging per inspected
        // mail is what let a noisy mailbox exhaust its own allowance before a single
        // alert was ever raised.
        for (var i = 0; i < 3; i++)
        {
            await pipeline.ProcessAsync(job, _mailbox!, _rule!, Mail("chatter", $"<n{i}@x>"),
                inMaintenance: false, _ => Task.CompletedTask, CancellationToken.None);
        }
        Assert.False(pipeline.IsEventBudgetExhausted(job));

        var first = await pipeline.ProcessAsync(job, _mailbox!, _rule!, Mail("ALERT one", "<a@x>"),
            inMaintenance: false, _ => Task.CompletedTask, CancellationToken.None);
        Assert.Equal(MailDisposition.EventCreated, first.Disposition);

        var second = await pipeline.ProcessAsync(job, _mailbox!, _rule!, Mail("ALERT two", "<b@x>"),
            inMaintenance: false, _ => Task.CompletedTask, CancellationToken.None);
        Assert.Equal(MailDisposition.RateLimited, second.Disposition);
        Assert.Null(second.EventId);
    }

    [Fact]
    public async Task IsEventBudgetExhausted_DoesNotConsumeBudget()
    {
        var job = await SeedAsync(maxEventsPerHour: 1);
        var pipeline = NewPipeline(new RecordingChannel(INotificationChannel.Snmp));

        // The poller calls this once per poll pass. When it consumed budget, a
        // one-minute schedule spent 60 of the hourly allowance on polling alone.
        for (var i = 0; i < 10; i++)
            Assert.False(pipeline.IsEventBudgetExhausted(job));

        var outcome = await pipeline.ProcessAsync(job, _mailbox!, _rule!, Mail("ALERT"),
            inMaintenance: false, _ => Task.CompletedTask, CancellationToken.None);
        Assert.Equal(MailDisposition.EventCreated, outcome.Disposition);
    }

    // ------------------------------------------------------------ claim semantics

    [Fact]
    public async Task ClaimIsPerJob_SoEveryJobOnTheMailboxSeesTheMail()
    {
        var jobA = await SeedAsync(jobName: "A");
        var jobB = await SeedAsync(jobName: "B");
        var pipeline = NewPipeline(new RecordingChannel(INotificationChannel.Snmp));
        var mail = Mail("ALERT shared", "<shared@x>");

        var a = await pipeline.ProcessAsync(jobA, _mailbox!, _rule!, mail,
            inMaintenance: false, _ => Task.CompletedTask, CancellationToken.None);
        var b = await pipeline.ProcessAsync(jobB, _mailbox!, _rule!, mail,
            inMaintenance: false, _ => Task.CompletedTask, CancellationToken.None);

        // Keyed per mailbox, job B's claim would have hit the UNIQUE constraint and the
        // mail would have been silently dropped for B — the most natural configuration
        // of the product (one alert mailbox, several rules) losing all but one rule.
        Assert.Equal(MailDisposition.EventCreated, a.Disposition);
        Assert.Equal(MailDisposition.EventCreated, b.Disposition);
        Assert.Equal(2, await _db.ProcessedMails.CountAsync());
    }

    [Fact]
    public async Task ARepeatOfACompletedMail_IsSkipped()
    {
        var job = await SeedAsync();
        var pipeline = NewPipeline(new RecordingChannel(INotificationChannel.Snmp));
        var mail = Mail("ALERT once", "<once@x>");

        await pipeline.ProcessAsync(job, _mailbox!, _rule!, mail,
            inMaintenance: false, _ => Task.CompletedTask, CancellationToken.None);
        var again = await pipeline.ProcessAsync(job, _mailbox!, _rule!, mail,
            inMaintenance: false, _ => Task.CompletedTask, CancellationToken.None);

        Assert.Null(again.Disposition);
        Assert.Single(await _db.Events.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task AnUnstampedClaimFromACrashedRun_IsResumedRatherThanTreatedAsDone()
    {
        var job = await SeedAsync();
        var mail = Mail("ALERT resumed", "<resume@x>");

        // Exactly what a crash between claim and outcome leaves behind: a committed row
        // with no disposition. Treating it as "someone else handled it" dropped the
        // alert permanently and silently.
        _db.ProcessedMails.Add(new ProcessedMail
        {
            MessageId = mail.ClaimKey, MailboxId = _mailbox!.Id, JobId = job.Id,
            Disposition = MailDisposition.Unknown
        });
        await _db.SaveChangesAsync();

        var outcome = await NewPipeline(new RecordingChannel(INotificationChannel.Snmp)).ProcessAsync(
            job, _mailbox!, _rule!, mail, inMaintenance: false, _ => Task.CompletedTask, CancellationToken.None);

        Assert.Equal(MailDisposition.EventCreated, outcome.Disposition);
        Assert.Single(await _db.Events.AsNoTracking().ToListAsync());
        Assert.Equal(1, await _db.ProcessedMails.CountAsync());
    }

    // ------------------------------------------------------------ seen gating

    [Fact]
    public async Task SeenFlagIsDeferredUntilEveryActiveJobHasProcessedTheMail()
    {
        var jobA = await SeedAsync(jobName: "A");
        var jobB = await SeedAsync(jobName: "B");
        var pipeline = NewPipeline(new RecordingChannel(INotificationChannel.Snmp));
        var mail = Mail("ALERT shared", "<shared@x>");
        var markSeenCalls = 0;
        Task MarkSeen(CancellationToken _) { markSeenCalls++; return Task.CompletedTask; }

        var a = await pipeline.ProcessAsync(jobA, _mailbox!, _rule!, mail, false, MarkSeen, CancellationToken.None);
        // Flagging Seen now would hide the mail from job B, whose poll query is NotSeen.
        Assert.False(a.MarkedSeen);
        Assert.Equal(0, markSeenCalls);

        var b = await pipeline.ProcessAsync(jobB, _mailbox!, _rule!, mail, false, MarkSeen, CancellationToken.None);
        Assert.True(b.MarkedSeen);
        Assert.Equal(1, markSeenCalls);
    }

    [Fact]
    public async Task DeactivatingTheOtherJob_ReleasesTheDeferredSeenFlag()
    {
        var jobA = await SeedAsync(jobName: "A");
        var jobB = await SeedAsync(jobName: "B");
        var pipeline = NewPipeline(new RecordingChannel(INotificationChannel.Snmp));
        var mail = Mail("ALERT shared", "<shared@x>");

        var first = await pipeline.ProcessAsync(jobA, _mailbox!, _rule!, mail, false,
            _ => Task.CompletedTask, CancellationToken.None);
        Assert.False(first.MarkedSeen);

        // The expected count is read live, so a mail can never be stranded unseen by a
        // job that no longer polls.
        (await _db.Jobs.FirstAsync(j => j.Id == jobB.Id)).IsActive = false;
        await _db.SaveChangesAsync();

        var second = await pipeline.ProcessAsync(jobA, _mailbox!, _rule!, Mail("ALERT other", "<other@x>"),
            false, _ => Task.CompletedTask, CancellationToken.None);
        Assert.True(second.MarkedSeen);
    }

    [Fact]
    public async Task SingleJob_FlagsSeenImmediately()
    {
        var job = await SeedAsync();
        var seen = false;

        var outcome = await NewPipeline(new RecordingChannel(INotificationChannel.Snmp)).ProcessAsync(
            job, _mailbox!, _rule!, Mail("ALERT solo"), inMaintenance: false,
            markSeenAsync: _ => { seen = true; return Task.CompletedTask; }, CancellationToken.None);

        Assert.True(outcome.MarkedSeen);
        Assert.True(seen);
    }

    // ------------------------------------------------------------ severity routing

    [Fact]
    public async Task TargetsBelowTheEventSeverity_AreSkipped()
    {
        var job = await SeedAsync(ruleSeverity: Severity.Warning);
        // "Page the NOC only for Critical."
        var target = await _db.SnmpTargets.FirstAsync();
        target.MinSeverity = Severity.Critical;
        await _db.SaveChangesAsync();
        job = await _db.Jobs
            .Include(j => j.Mailbox)
            .Include(j => j.JobSnmpTargets).ThenInclude(t => t.SnmpTarget)
            .Include(j => j.JobWebhookTargets).ThenInclude(t => t.WebhookTarget)
            .FirstAsync(j => j.Id == job.Id);

        var channel = new RecordingChannel(INotificationChannel.Snmp);
        await NewPipeline(channel).ProcessAsync(job, _mailbox!, _rule!, Mail("ALERT minor"),
            inMaintenance: false, _ => Task.CompletedTask, CancellationToken.None);

        Assert.Empty(channel.Sent);
        // Nothing was delivered, so the event must not claim otherwise.
        var evt = await _db.Events.AsNoTracking().SingleAsync();
        Assert.Equal(EventState.New, evt.State);
    }
}
