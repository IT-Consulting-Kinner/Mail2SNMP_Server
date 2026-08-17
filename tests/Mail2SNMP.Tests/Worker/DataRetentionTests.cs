using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Models.Configuration;
using Mail2SNMP.Models.Entities;
using Mail2SNMP.Models.Enums;
using Mail2SNMP.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Mail2SNMP.Tests.Worker;

/// <summary>
/// Tests for the data-retention cycle, which had no coverage at all.
/// </summary>
/// <remarks>
/// <para>
/// Retention now deletes set-based (<c>ExecuteDelete</c>/<c>ExecuteUpdate</c>) instead of
/// materializing up to 5000 tracked entities per step just to remove them. That is a real
/// behavioural boundary: those operators bypass the change tracker, run outside
/// <c>SaveChanges</c>, and — critically — are <b>not implemented by the in-memory
/// provider</b>. A test written the usual way for this repo would throw rather than pass,
/// which is precisely how a provider-specific defect slipped into a release before.
/// </para>
/// <para>
/// So these run against a real SQLite file, and they also pin the two things the cycle is
/// silently responsible for: draining more than one batch per pass, and keeping the
/// active-events gauge honest when events auto-expire.
/// </para>
/// </remarks>
public class DataRetentionTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ServiceProvider _provider;
    private readonly Mail2SnmpDbContext _db;

    public DataRetentionTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"m2s-retention-{Guid.NewGuid():N}.db");

        var services = new ServiceCollection();
        services.AddDbContext<Mail2SnmpDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"));
        _provider = services.BuildServiceProvider();

        _db = _provider.GetRequiredService<Mail2SnmpDbContext>();
        _db.Database.Migrate();
    }

    public void Dispose()
    {
        _provider.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch (IOException) { /* best effort */ }
    }

    private DataRetentionService NewService(EventSettings? events = null, RetentionSettings? retention = null)
        => new(_provider.GetRequiredService<IServiceScopeFactory>(),
               NullLogger<DataRetentionService>.Instance,
               Options.Create(events ?? new EventSettings()),
               Options.Create(retention ?? new RetentionSettings()));

    private async Task<Job> SeedJobAsync()
    {
        var mailbox = new Mailbox { Name = "MB", Host = "h", Username = "u", EncryptedPassword = "p", Folder = "INBOX" };
        var rule = new Rule { Name = "R", Field = RuleFieldType.Subject, MatchType = RuleMatchType.Contains, Criteria = "x" };
        _db.Mailboxes.Add(mailbox);
        _db.Rules.Add(rule);
        await _db.SaveChangesAsync();
        var job = new Job { Name = "J", MailboxId = mailbox.Id, RuleId = rule.Id };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        return job;
    }

    [Fact]
    public async Task SetBasedDeletesActuallyRunOnSqlite()
    {
        // The point of this test: ExecuteDelete with Take() has to be translatable by the
        // provider the product actually ships with by default. If it is not, every
        // retention step throws once an hour, forever, and nothing else would notice.
        var job = await SeedJobAsync();
        var old = DateTime.UtcNow.AddDays(-400);

        for (var i = 0; i < 5; i++)
        {
            _db.ProcessedMails.Add(new ProcessedMail
            {
                MailboxId = job.MailboxId, JobId = job.Id, MessageId = $"<old{i}@x>",
                ProcessedUtc = old, Disposition = MailDisposition.NoMatch
            });
            _db.AuditEvents.Add(new AuditEvent { TimestampUtc = old, Action = "X.Created", ActorId = "system" });
        }
        _db.ProcessedMails.Add(new ProcessedMail
        {
            MailboxId = job.MailboxId, JobId = job.Id, MessageId = "<fresh@x>",
            ProcessedUtc = DateTime.UtcNow, Disposition = MailDisposition.NoMatch
        });
        await _db.SaveChangesAsync();

        await NewService().RunRetentionCleanupAsync(CancellationToken.None);

        var remaining = await _db.ProcessedMails.AsNoTracking().ToListAsync();
        Assert.Single(remaining);
        Assert.Equal("<fresh@x>", remaining[0].MessageId);
        Assert.Empty(await _db.AuditEvents.AsNoTracking().Where(a => a.TimestampUtc == old).ToListAsync());
    }

    [Fact]
    public async Task ExpiredEventsAreDeletedWithTheirDedupRows()
    {
        var job = await SeedJobAsync();
        var old = DateTime.UtcNow.AddDays(-400);

        var evt = new Event
        {
            JobId = job.Id, Severity = Severity.Error, Subject = "s",
            State = EventState.Resolved, CreatedUtc = old, LastStateChangeUtc = old
        };
        _db.Events.Add(evt);
        await _db.SaveChangesAsync();
        // LastSeenUtc is deliberately RECENT, so the dedup-age step cannot be what removes
        // this row. Note that the assertion is satisfied by either mechanism — the explicit
        // delete or the schema's ON DELETE CASCADE — so it pins the outcome, not the means.
        // That is the right level here: what must never happen is a dedup row outliving its
        // event, regardless of which layer prevents it.
        _db.EventDedups.Add(new EventDedup
        {
            EventId = evt.Id, JobId = job.Id, DedupKeyHash = "hash", LastSeenUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        await NewService().RunRetentionCleanupAsync(CancellationToken.None);

        Assert.Empty(await _db.Events.AsNoTracking().ToListAsync());
        // A dedup row outliving its event suppresses future alerts forever while pointing
        // at an event that no longer exists.
        Assert.Empty(await _db.EventDedups.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task OldNewEventsAreExpiredRatherThanLeftActive()
    {
        var job = await SeedJobAsync();
        var old = DateTime.UtcNow.AddDays(-400);

        _db.Events.Add(new Event
        {
            JobId = job.Id, Severity = Severity.Error, Subject = "s",
            State = EventState.New, CreatedUtc = old, LastStateChangeUtc = old
        });
        await _db.SaveChangesAsync();

        var before = Mail2SNMP.Infrastructure.Services.Mail2SnmpMetrics.ActiveEvents.Value;
        await NewService().RunRetentionCleanupAsync(CancellationToken.None);

        // Expiry stamps LastStateChangeUtc with "now", so the event deliberately survives
        // this cycle: deletion is measured from the state change, which gives an operator
        // the full retention window to see what expired. It is removed by a later cycle.
        var evt = await _db.Events.AsNoTracking().SingleAsync();
        Assert.Equal(EventState.Expired, evt.State);

        // AR-1: New is part of the active set, so auto-expiry must decrement the gauge or
        // it drifts upward by the expired count on every retention cycle.
        Assert.True(Mail2SNMP.Infrastructure.Services.Mail2SnmpMetrics.ActiveEvents.Value < before);
    }

    [Fact]
    public async Task ACycleDrainsBeyondASingleBatch()
    {
        var job = await SeedJobAsync();
        var old = DateTime.UtcNow.AddDays(-400);

        // The dead-letter step is capped at 1000 rows per statement. Before the drain
        // loop existed, a deployment aging out rows faster than the cap grew the table
        // without bound while the cycle reported success every hour.
        var target = new WebhookTarget { Name = "WH", Url = "https://example.invalid/h" };
        _db.WebhookTargets.Add(target);
        await _db.SaveChangesAsync();
        var evt = new Event { JobId = job.Id, Severity = Severity.Error, Subject = "s" };
        _db.Events.Add(evt);
        await _db.SaveChangesAsync();

        for (var i = 0; i < 1200; i++)
        {
            _db.DeadLetterEntries.Add(new DeadLetterEntry
            {
                WebhookTargetId = target.Id, EventId = evt.Id, PayloadJson = "{}",
                CreatedUtc = old, Status = DeadLetterStatus.Abandoned
            });
        }
        await _db.SaveChangesAsync();

        await NewService().RunRetentionCleanupAsync(CancellationToken.None);

        Assert.Equal(0, await _db.DeadLetterEntries.CountAsync());
    }

    [Fact]
    public async Task RecentDataIsLeftAlone()
    {
        var job = await SeedJobAsync();

        _db.ProcessedMails.Add(new ProcessedMail
        {
            MailboxId = job.MailboxId, JobId = job.Id, MessageId = "<today@x>",
            ProcessedUtc = DateTime.UtcNow, Disposition = MailDisposition.EventCreated
        });
        _db.Events.Add(new Event
        {
            JobId = job.Id, Severity = Severity.Error, Subject = "s",
            State = EventState.New, CreatedUtc = DateTime.UtcNow, LastStateChangeUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        await NewService().RunRetentionCleanupAsync(CancellationToken.None);

        Assert.Equal(1, await _db.ProcessedMails.CountAsync());
        var evt = await _db.Events.AsNoTracking().SingleAsync();
        Assert.Equal(EventState.New, evt.State);
    }

    [Fact]
    public async Task TheAuditCapTrimsTheOldestEntriesFirst()
    {
        var baseTime = DateTime.UtcNow.AddHours(-5);
        for (var i = 0; i < 10; i++)
        {
            _db.AuditEvents.Add(new AuditEvent
            {
                TimestampUtc = baseTime.AddMinutes(i), Action = $"X.{i}", ActorId = "system"
            });
        }
        await _db.SaveChangesAsync();

        await NewService(retention: new RetentionSettings { MaxAuditEntries = 4 })
            .RunRetentionCleanupAsync(CancellationToken.None);

        var remaining = await _db.AuditEvents.AsNoTracking().OrderBy(a => a.TimestampUtc).ToListAsync();
        Assert.Equal(4, remaining.Count);
        // Keeping the most recent is the whole point of a cap — trimming the newest would
        // discard exactly the entries an operator is looking for.
        Assert.Equal("X.6", remaining[0].Action);
        Assert.Equal("X.9", remaining[3].Action);
    }
}
