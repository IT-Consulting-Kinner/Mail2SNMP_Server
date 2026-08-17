using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Infrastructure.Services;
using Mail2SNMP.Models.DTOs;
using Mail2SNMP.Models.Entities;
using Mail2SNMP.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mail2SNMP.Tests.Infrastructure;

/// <summary>
/// Review finding: the dead-letter queue was webhook-shaped and unfiltered — the listing
/// hard-capped at the newest 500 rows with no total and no way to reach
/// <see cref="DeadLetterStatus.Abandoned"/> entries, and the only bulk retry took a
/// <c>webhookTargetId</c>, so SNMP entries (UC-3) needed one call per row and got a
/// different reset than webhook entries. These tests pin the filtering, paging, honest
/// total, and kind-neutral bulk semantics.
/// </summary>
public class DeadLetterQueryTests : IDisposable
{
    private readonly Mail2SnmpDbContext _db;
    private readonly DeadLetterService _svc;

    public DeadLetterQueryTests()
    {
        var options = new DbContextOptionsBuilder<Mail2SnmpDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new Mail2SnmpDbContext(options);
        _db.Database.EnsureCreated();
        _svc = new DeadLetterService(_db, NullLogger<DeadLetterService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    /// <summary>
    /// Seeds one event plus a webhook and an SNMP target, then the requested mix of
    /// dead letters. Entries are stamped with descending CreatedUtc so ordering is
    /// deterministic rather than dependent on insert timing.
    /// </summary>
    private async Task SeedAsync(int webhookPending, int webhookAbandoned, int snmpPending)
    {
        var mailbox = new Mailbox { Name = "MB", Host = "h", Username = "u" };
        var rule = new Rule { Name = "R", Field = RuleFieldType.Subject, MatchType = RuleMatchType.Contains, Criteria = "t" };
        _db.Mailboxes.Add(mailbox);
        _db.Rules.Add(rule);
        await _db.SaveChangesAsync();

        var job = new Job { Name = "J", MailboxId = mailbox.Id, RuleId = rule.Id };
        var webhook = new WebhookTarget { Name = "WH", Url = "https://example.invalid/hook" };
        var snmp = new SnmpTarget { Name = "SN", Host = "127.0.0.1", Port = 162 };
        _db.Jobs.Add(job);
        _db.WebhookTargets.Add(webhook);
        _db.SnmpTargets.Add(snmp);
        await _db.SaveChangesAsync();

        var evt = new Event { JobId = job.Id, Severity = Severity.Error, Subject = "s" };
        _db.Events.Add(evt);
        await _db.SaveChangesAsync();

        var stamp = new DateTime(2026, 8, 17, 12, 0, 0, DateTimeKind.Utc);
        void Add(int count, int? webhookId, int? snmpId, DeadLetterStatus status, int attempts)
        {
            for (var i = 0; i < count; i++)
            {
                _db.DeadLetterEntries.Add(new DeadLetterEntry
                {
                    WebhookTargetId = webhookId,
                    SnmpTargetId = snmpId,
                    EventId = evt.Id,
                    PayloadJson = "{}",
                    LastError = "boom",
                    AttemptCount = attempts,
                    Status = status,
                    CreatedUtc = stamp.AddSeconds(-i)
                });
            }
        }

        Add(webhookPending, webhook.Id, null, DeadLetterStatus.Pending, 1);
        Add(webhookAbandoned, webhook.Id, null, DeadLetterStatus.Abandoned, 10);
        Add(snmpPending, null, snmp.Id, DeadLetterStatus.Pending, 1);
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Query_ReportsTotalBeforePaging_SoTruncationIsVisible()
    {
        await SeedAsync(webhookPending: 30, webhookAbandoned: 0, snmpPending: 0);

        var page = await _svc.QueryAsync(new DeadLetterQuery { Take = 10 });

        // The page is short; the count is not. Reporting only the page length is what
        // made the old 500-row cap invisible.
        Assert.Equal(10, page.Entries.Count);
        Assert.Equal(30, page.TotalCount);
    }

    [Fact]
    public async Task Query_Skip_ReturnsDisjointPages()
    {
        await SeedAsync(webhookPending: 25, webhookAbandoned: 0, snmpPending: 0);

        var first = await _svc.QueryAsync(new DeadLetterQuery { Take = 10, Skip = 0 });
        var second = await _svc.QueryAsync(new DeadLetterQuery { Take = 10, Skip = 10 });
        var third = await _svc.QueryAsync(new DeadLetterQuery { Take = 10, Skip = 20 });

        Assert.Equal(5, third.Entries.Count);
        var ids = first.Entries.Concat(second.Entries).Concat(third.Entries).Select(e => e.Id).ToList();
        Assert.Equal(25, ids.Distinct().Count());
    }

    [Fact]
    public async Task Query_AbandonedFilter_ReachesEntriesTheDefaultViewBuries()
    {
        // 600 newer Pending rows would have pushed every Abandoned entry past the old
        // unfiltered 500-row cap, making them unreachable in the UI.
        await SeedAsync(webhookPending: 600, webhookAbandoned: 3, snmpPending: 0);

        var abandoned = await _svc.QueryAsync(new DeadLetterQuery { Status = DeadLetterStatus.Abandoned });

        Assert.Equal(3, abandoned.TotalCount);
        Assert.All(abandoned.Entries, e => Assert.Equal(DeadLetterStatus.Abandoned, e.Status));
    }

    [Fact]
    public async Task Query_KindFilter_SeparatesSnmpFromWebhook()
    {
        await SeedAsync(webhookPending: 4, webhookAbandoned: 0, snmpPending: 3);

        var snmp = await _svc.QueryAsync(new DeadLetterQuery { Kind = DeadLetterTargetKind.Snmp });
        var webhook = await _svc.QueryAsync(new DeadLetterQuery { Kind = DeadLetterTargetKind.Webhook });

        Assert.Equal(3, snmp.TotalCount);
        Assert.All(snmp.Entries, e => Assert.NotNull(e.SnmpTargetId));
        Assert.Equal(4, webhook.TotalCount);
        Assert.All(webhook.Entries, e => Assert.NotNull(e.WebhookTargetId));
    }

    [Fact]
    public async Task RetryAll_WithNoFilter_RequeuesBothKindsInOneCall()
    {
        await SeedAsync(webhookPending: 2, webhookAbandoned: 0, snmpPending: 2);

        var count = await _svc.RetryAllAsync(new DeadLetterQuery());

        // The UI previously needed one bulk call per webhook target plus one individual
        // call per SNMP row to achieve this.
        Assert.Equal(4, count);
        var all = await _db.DeadLetterEntries.AsNoTracking().ToListAsync();
        Assert.All(all, e => Assert.Equal(DeadLetterStatus.Pending, e.Status));
        Assert.All(all, e => Assert.Null(e.LockedByInstanceId));
    }

    [Fact]
    public async Task RetryAll_ResetsAttemptCount_SoAbandonedEntriesAreActuallyClaimable()
    {
        await SeedAsync(webhookPending: 0, webhookAbandoned: 2, snmpPending: 0);

        var count = await _svc.RetryAllAsync(new DeadLetterQuery { Status = DeadLetterStatus.Abandoned });

        Assert.Equal(2, count);
        var all = await _db.DeadLetterEntries.AsNoTracking().ToListAsync();
        // Without the counter reset the worker's claim query (AttemptCount < MaxAttempts)
        // skips these forever while the UI reports "queued for retry" — the exact
        // asymmetry the single-entry RetryAsync path already avoided.
        Assert.All(all, e => Assert.Equal(0, e.AttemptCount));
        Assert.All(all, e => Assert.Equal(DeadLetterStatus.Pending, e.Status));
        Assert.All(all, e => Assert.Null(e.LastError));
    }

    [Fact]
    public async Task RetryAll_WithFilter_LeavesNonMatchingEntriesUntouched()
    {
        await SeedAsync(webhookPending: 2, webhookAbandoned: 0, snmpPending: 2);

        var count = await _svc.RetryAllAsync(new DeadLetterQuery { Kind = DeadLetterTargetKind.Snmp });

        Assert.Equal(2, count);
        var webhookEntries = await _db.DeadLetterEntries.AsNoTracking()
            .Where(e => e.WebhookTargetId != null).ToListAsync();
        // Untouched means the original error text survives — a blanket reset would have
        // cleared it.
        Assert.All(webhookEntries, e => Assert.Equal("boom", e.LastError));
    }

    [Fact]
    public async Task Query_TakeIsClampedToMaxTake()
    {
        await SeedAsync(webhookPending: 3, webhookAbandoned: 0, snmpPending: 0);

        // A client asking for int.MaxValue must not make the server materialize the
        // whole table.
        var page = await _svc.QueryAsync(new DeadLetterQuery { Take = int.MaxValue, Skip = -5 });

        Assert.Equal(3, page.Entries.Count);
        Assert.Equal(3, page.TotalCount);
    }
}
