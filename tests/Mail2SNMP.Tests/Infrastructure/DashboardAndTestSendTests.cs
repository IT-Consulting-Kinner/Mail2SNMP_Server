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
/// Two review findings in one place: the dashboard aggregation was implemented twice
/// (REST endpoint and Blazor home page) and had already drifted, and Test Send was fixed
/// at <see cref="Severity.Information"/>, which made severity routing — the setting most
/// likely to be misconfigured — the one thing Test Send could not verify.
/// </summary>
public class DashboardAndTestSendTests : IDisposable
{
    private readonly Mail2SnmpDbContext _db;
    private readonly IAuditService _audit = Substitute.For<IAuditService>();
    private readonly ILicenseProvider _license = Substitute.For<ILicenseProvider>();
    private readonly IMaintenanceWindowService _maintenance = Substitute.For<IMaintenanceWindowService>();

    /// <summary>Channel that records the severity it was asked to deliver.</summary>
    private sealed class RecordingChannel : INotificationChannel
    {
        public string ChannelName => INotificationChannel.Snmp;
        public List<Severity> Delivered { get; } = new();

        public Task<bool> SendToSnmpTargetAsync(NotificationContext c, SnmpTarget t, CancellationToken ct = default)
        {
            Delivered.Add(c.Severity);
            return Task.FromResult(true);
        }
    }

    public DashboardAndTestSendTests()
    {
        var options = new DbContextOptionsBuilder<Mail2SnmpDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new Mail2SnmpDbContext(options);
        _db.Database.EnsureCreated();
        _license.IsEnterprise().Returns(true);
        _license.Current.Returns(new LicenseInfo { Edition = LicenseEdition.Enterprise });
        _maintenance.IsInMaintenanceAsync(ct: Arg.Any<CancellationToken>()).Returns(false);
    }

    public void Dispose() => _db.Dispose();

    // ---------------------------------------------------------------- dashboard

    [Fact]
    public async Task Dashboard_CountsOnlyActiveRecords_AndTreatsAcknowledgedAsOpen()
    {
        var mailbox = new Mailbox { Name = "MB", Host = "h", Username = "u" };
        var inactive = new Mailbox { Name = "Old", Host = "h", Username = "u", IsActive = false };
        var rule = new Rule { Name = "R", Field = RuleFieldType.Subject, MatchType = RuleMatchType.Contains, Criteria = "t" };
        _db.Mailboxes.AddRange(mailbox, inactive);
        _db.Rules.Add(rule);
        await _db.SaveChangesAsync();
        var job = new Job { Name = "J", MailboxId = mailbox.Id, RuleId = rule.Id };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();

        _db.Events.AddRange(
            new Event { JobId = job.Id, Severity = Severity.Error, State = EventState.New },
            new Event { JobId = job.Id, Severity = Severity.Error, State = EventState.Acknowledged },
            new Event { JobId = job.Id, Severity = Severity.Error, State = EventState.Resolved });
        await _db.SaveChangesAsync();

        var dto = await new DashboardService(_db, _maintenance, _license).GetAsync();

        Assert.Equal(1, dto.ActiveMailboxes);
        Assert.Equal(1, dto.ActiveJobs);
        // Acknowledged is still an operator's problem: seen, not resolved.
        Assert.Equal(2, dto.OpenEvents);
    }

    [Fact]
    public async Task Dashboard_HealthIsComputedFromFailingMailboxes()
    {
        var healthy = new Mailbox { Name = "OK", Host = "h", Username = "u" };
        _db.Mailboxes.Add(healthy);
        await _db.SaveChangesAsync();

        var svc = new DashboardService(_db, _maintenance, _license);
        Assert.True((await svc.GetAsync()).IsHealthy);

        // UC-1: a failing active mailbox means ingestion is down for its jobs. Reporting
        // a hardcoded green here is what let a broken mailbox stay invisible.
        healthy.LastError = "Authentication failed";
        await _db.SaveChangesAsync();

        var degraded = await svc.GetAsync();
        Assert.False(degraded.IsHealthy);
        Assert.Equal(1, degraded.MailboxesInError);
    }

    [Fact]
    public async Task Dashboard_PendingDeadLettersExcludesAbandonedOnes()
    {
        var mailbox = new Mailbox { Name = "MB", Host = "h", Username = "u" };
        var rule = new Rule { Name = "R", Field = RuleFieldType.Subject, MatchType = RuleMatchType.Contains, Criteria = "t" };
        _db.Mailboxes.Add(mailbox);
        _db.Rules.Add(rule);
        await _db.SaveChangesAsync();
        var job = new Job { Name = "J", MailboxId = mailbox.Id, RuleId = rule.Id };
        var target = new WebhookTarget { Name = "WH", Url = "https://example.invalid/h" };
        _db.Jobs.Add(job);
        _db.WebhookTargets.Add(target);
        await _db.SaveChangesAsync();
        var evt = new Event { JobId = job.Id, Severity = Severity.Error };
        _db.Events.Add(evt);
        await _db.SaveChangesAsync();

        _db.DeadLetterEntries.AddRange(
            new DeadLetterEntry { WebhookTargetId = target.Id, EventId = evt.Id, PayloadJson = "{}", Status = DeadLetterStatus.Pending },
            new DeadLetterEntry { WebhookTargetId = target.Id, EventId = evt.Id, PayloadJson = "{}", Status = DeadLetterStatus.Abandoned });
        await _db.SaveChangesAsync();

        var dto = await new DashboardService(_db, _maintenance, _license).GetAsync();

        // The tile means "waiting to be retried", so an abandoned entry does not belong.
        Assert.Equal(1, dto.PendingDeadLetters);
    }

    [Fact]
    public async Task Dashboard_ReportsTheActiveMaintenanceWindowByName()
    {
        var now = DateTime.UtcNow;
        _maintenance.IsInMaintenanceAsync(ct: Arg.Any<CancellationToken>()).Returns(true);
        _maintenance.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<MaintenanceWindow>
        {
            new() { Name = "Nightly patching", IsActive = true, StartUtc = now.AddHours(-1), EndUtc = now.AddHours(1) }
        });

        var dto = await new DashboardService(_db, _maintenance, _license).GetAsync();

        // The UI copy of the aggregation never surfaced this at all — exactly the kind of
        // drift a second implementation invites.
        Assert.True(dto.MaintenanceActive);
        Assert.Equal("Nightly patching", dto.MaintenanceWindowName);
    }

    // ---------------------------------------------------------------- test send

    [Fact]
    public async Task TestSend_UsesTheRequestedSeverity_SoRoutingCanBeVerified()
    {
        var mailbox = new Mailbox { Name = "MB", Host = "h", Username = "u" };
        var rule = new Rule { Name = "R", Field = RuleFieldType.Subject, MatchType = RuleMatchType.Contains, Criteria = "t" };
        _db.Mailboxes.Add(mailbox);
        _db.Rules.Add(rule);
        await _db.SaveChangesAsync();
        var job = new Job { Name = "J", MailboxId = mailbox.Id, RuleId = rule.Id };
        var target = new SnmpTarget { Name = "NOC", Host = "127.0.0.1", Port = 162, MinSeverity = Severity.Critical };
        _db.Jobs.Add(job);
        _db.SnmpTargets.Add(target);
        await _db.SaveChangesAsync();
        _db.JobSnmpTargets.Add(new JobSnmpTarget { JobId = job.Id, SnmpTargetId = target.Id });
        await _db.SaveChangesAsync();

        var channel = new RecordingChannel();
        var svc = new JobService(_db, _license, _audit,
            new RuleEvaluator(NullLogger<RuleEvaluator>.Instance),
            new[] { (INotificationChannel)channel }, NullLogger<JobService>.Instance);

        // Default is unchanged, and a Critical-only target is still correctly skipped.
        var atInformation = await svc.SendTestEventAsync(job.Id);
        Assert.Contains("skipped (target requires >= Critical)", atInformation);
        Assert.Empty(channel.Delivered);

        // Raising the severity is what makes the routing verifiable from the product.
        var atCritical = await svc.SendTestEventAsync(job.Id, Severity.Critical);
        Assert.Contains("1 delivered", atCritical);
        Assert.Equal(new[] { Severity.Critical }, channel.Delivered);
    }
}
