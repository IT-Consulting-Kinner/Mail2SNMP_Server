using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Infrastructure.Services;
using Mail2SNMP.Models.Entities;
using Mail2SNMP.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Mail2SNMP.Tests.Infrastructure;

/// <summary>
/// Peer-review: the EventService state machine and deduplication are core
/// correctness guarantees that the existing happy-path tests
/// (Acknowledge/Resolve) did not pin. These cover the illegal-transition guard
/// and the dedup HitCount increment.
/// </summary>
public class EventServiceStateMachineTests : IDisposable
{
    private readonly Mail2SnmpDbContext _db;
    private readonly IAuditService _audit = Substitute.For<IAuditService>();

    public EventServiceStateMachineTests()
    {
        var options = new DbContextOptionsBuilder<Mail2SnmpDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new Mail2SnmpDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose() => _db.Dispose();

    private async Task<Job> SeedJobAsync()
    {
        var mailbox = new Mailbox { Name = "MB", Host = "h", Username = "u" };
        var rule = new Rule { Name = "R", Field = RuleFieldType.Subject, MatchType = RuleMatchType.Contains, Criteria = "t" };
        _db.Mailboxes.Add(mailbox);
        _db.Rules.Add(rule);
        await _db.SaveChangesAsync();
        var job = new Job { Name = "J", MailboxId = mailbox.Id, RuleId = rule.Id };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        return job;
    }

    private EventService NewService() =>
        new(_db, _audit, Enumerable.Empty<INotificationChannel>(), NullLogger<EventService>.Instance);

    [Fact]
    public async Task Acknowledge_AfterResolve_ThrowsInvalidTransition()
    {
        var job = await SeedJobAsync();
        var svc = NewService();
        var evt = await svc.CreateAsync(new Event { JobId = job.Id, Severity = Severity.Error, Subject = "x" });

        await svc.ResolveAsync(evt.Id, "op"); // New → Resolved (terminal)

        // Resolved has no outgoing transitions — acknowledging must be refused.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.AcknowledgeAsync(evt.Id, "op"));
        Assert.Contains("Resolved", ex.Message);
    }

    [Fact]
    public async Task MarkAsNotified_AfterAcknowledge_ThrowsInvalidTransition()
    {
        var job = await SeedJobAsync();
        var svc = NewService();
        var evt = await svc.CreateAsync(new Event { JobId = job.Id, Severity = Severity.Warning, Subject = "x" });

        await svc.AcknowledgeAsync(evt.Id, "op"); // New → Acknowledged

        // Acknowledged cannot go back to Notified.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.MarkAsNotifiedAsync(evt.Id));
    }

    [Fact]
    public async Task Create_DuplicateMessageId_IncrementsHitCount_NotInsert()
    {
        var job = await SeedJobAsync();
        var svc = NewService();

        var first = await svc.CreateAsync(new Event
        {
            JobId = job.Id, Severity = Severity.Critical, Subject = "Disk full", MessageId = "msg-123"
        });
        var second = await svc.CreateAsync(new Event
        {
            JobId = job.Id, Severity = Severity.Critical, Subject = "Disk full", MessageId = "msg-123"
        });

        // Same event returned, HitCount bumped, and only one row persisted.
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(2, second.HitCount);
        Assert.Equal(1, await _db.Events.CountAsync(e => e.JobId == job.Id));
    }
}
