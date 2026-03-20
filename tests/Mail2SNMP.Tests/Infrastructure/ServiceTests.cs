using System.ComponentModel.DataAnnotations;
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

public class ServiceTests : IDisposable
{
    private readonly Mail2SnmpDbContext _db;
    private readonly IAuditService _audit;
    private readonly ILicenseProvider _license;
    private readonly ICredentialEncryptor _credentialEncryptor;
    private readonly RuleEvaluator _ruleEvaluator;

    public ServiceTests()
    {
        var options = new DbContextOptionsBuilder<Mail2SnmpDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new Mail2SnmpDbContext(options);
        _db.Database.EnsureCreated();

        _audit = Substitute.For<IAuditService>();
        _license = Substitute.For<ILicenseProvider>();
        _license.GetLimit("maxmailboxes").Returns(3);
        _license.GetLimit("maxjobs").Returns(5);
        _license.GetLimit("maxworkerinstances").Returns(1);
        _license.Current.Returns(new LicenseInfo { Edition = LicenseEdition.Community });
        _credentialEncryptor = Substitute.For<ICredentialEncryptor>();
        _ruleEvaluator = new RuleEvaluator(NullLogger<RuleEvaluator>.Instance);
    }

    public void Dispose() => _db.Dispose();

    // MailboxService tests
    [Fact]
    public async Task MailboxService_Create_And_GetAll()
    {
        var service = new MailboxService(_db, _license, _audit, _credentialEncryptor, NullLogger<MailboxService>.Instance);
        var mailbox = new Mailbox { Name = "Test", Host = "imap.test.com", Username = "user" };
        await service.CreateAsync(mailbox);

        var all = await service.GetAllAsync();
        Assert.Single(all);
        Assert.Equal("Test", all[0].Name);
    }

    [Fact]
    public async Task MailboxService_Create_ExceedsLimit_Throws()
    {
        var service = new MailboxService(_db, _license, _audit, _credentialEncryptor, NullLogger<MailboxService>.Instance);
        for (int i = 0; i < 3; i++)
            await service.CreateAsync(new Mailbox { Name = $"MB{i}", Host = "host", Username = "u" });

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.CreateAsync(new Mailbox { Name = "MB4", Host = "host", Username = "u" }));
    }

    [Fact]
    public async Task MailboxService_Delete_ThenCreateNewIsAllowed()
    {
        var service = new MailboxService(_db, _license, _audit, _credentialEncryptor, NullLogger<MailboxService>.Instance);
        var mb = await service.CreateAsync(new Mailbox { Name = "ToDelete", Host = "host", Username = "u" });
        await service.CreateAsync(new Mailbox { Name = "MB2", Host = "host", Username = "u" });
        await service.CreateAsync(new Mailbox { Name = "MB3", Host = "host", Username = "u" });

        await service.DeleteAsync(mb.Id);
        // Should not throw - we deleted one, so we're at 2/3
        var newMb = await service.CreateAsync(new Mailbox { Name = "Replacement", Host = "host", Username = "u" });
        Assert.NotNull(newMb);
    }

    // RuleService tests
    [Fact]
    public async Task RuleService_Create_And_GetById()
    {
        var service = new RuleService(_db, _audit);
        var rule = new Rule { Name = "CriticalAlert", Field = RuleFieldType.Subject, MatchType = RuleMatchType.Contains, Criteria = "CRITICAL" };
        var created = await service.CreateAsync(rule);
        var retrieved = await service.GetByIdAsync(created.Id);
        Assert.NotNull(retrieved);
        Assert.Equal("CriticalAlert", retrieved!.Name);
    }

    // JobService tests
    [Fact]
    public async Task JobService_Create_ExceedsLimit_Throws()
    {
        // Setup mailbox and rule
        var mailbox = new Mailbox { Name = "MB", Host = "host", Username = "u" };
        _db.Mailboxes.Add(mailbox);
        var rule = new Rule { Name = "R1", Field = RuleFieldType.Subject, MatchType = RuleMatchType.Contains, Criteria = "test" };
        _db.Rules.Add(rule);
        await _db.SaveChangesAsync();

        var service = new JobService(_db, _license, _audit, _ruleEvaluator, NullLogger<JobService>.Instance);
        for (int i = 0; i < 5; i++)
            await service.CreateAsync(new Job { Name = $"Job{i}", MailboxId = mailbox.Id, RuleId = rule.Id });

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.CreateAsync(new Job { Name = "Job6", MailboxId = mailbox.Id, RuleId = rule.Id }));
    }

    // EventService tests
    [Fact]
    public async Task EventService_Acknowledge_ChangesState()
    {
        var mailbox = new Mailbox { Name = "MB", Host = "host", Username = "u" };
        _db.Mailboxes.Add(mailbox);
        var rule = new Rule { Name = "R", Field = RuleFieldType.Subject, MatchType = RuleMatchType.Contains, Criteria = "t" };
        _db.Rules.Add(rule);
        await _db.SaveChangesAsync();
        var job = new Job { Name = "J", MailboxId = mailbox.Id, RuleId = rule.Id };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();

        var channels = Substitute.For<IEnumerable<INotificationChannel>>();
        var eventService = new EventService(_db, _audit, Enumerable.Empty<INotificationChannel>(), NullLogger<EventService>.Instance);

        var evt = await eventService.CreateAsync(new Event { JobId = job.Id, Severity = Severity.Warning, Subject = "Test" });
        Assert.Equal(EventState.New, evt.State);

        await eventService.AcknowledgeAsync(evt.Id, "admin");
        var updated = await eventService.GetByIdAsync(evt.Id);
        Assert.Equal(EventState.Acknowledged, updated!.State);
        Assert.Equal("admin", updated.AcknowledgedBy);
    }

    [Fact]
    public async Task EventService_Resolve_ChangesState()
    {
        var mailbox = new Mailbox { Name = "MB", Host = "host", Username = "u" };
        _db.Mailboxes.Add(mailbox);
        var rule = new Rule { Name = "R", Field = RuleFieldType.Subject, MatchType = RuleMatchType.Contains, Criteria = "t" };
        _db.Rules.Add(rule);
        await _db.SaveChangesAsync();
        var job = new Job { Name = "J", MailboxId = mailbox.Id, RuleId = rule.Id };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();

        var eventService = new EventService(_db, _audit, Enumerable.Empty<INotificationChannel>(), NullLogger<EventService>.Instance);
        var evt = await eventService.CreateAsync(new Event { JobId = job.Id, Severity = Severity.Error, Subject = "Error" });

        await eventService.ResolveAsync(evt.Id, "operator1");
        var updated = await eventService.GetByIdAsync(evt.Id);
        Assert.Equal(EventState.Resolved, updated!.State);
        Assert.Equal("operator1", updated.ResolvedBy);
    }

    // MaintenanceWindowService tests
    [Fact]
    public async Task MaintenanceWindowService_IsInMaintenance_Active()
    {
        var service = new MaintenanceWindowService(_db, _audit);
        await service.CreateAsync(new MaintenanceWindow
        {
            Name = "TestMW",
            StartUtc = DateTime.UtcNow.AddMinutes(-10),
            EndUtc = DateTime.UtcNow.AddMinutes(10),
            Scope = "All",
            CreatedBy = "admin"
        });

        Assert.True(await service.IsInMaintenanceAsync());
    }

    [Fact]
    public async Task MaintenanceWindowService_IsInMaintenance_Expired()
    {
        var service = new MaintenanceWindowService(_db, _audit);
        await service.CreateAsync(new MaintenanceWindow
        {
            Name = "PastMW",
            StartUtc = DateTime.UtcNow.AddHours(-2),
            EndUtc = DateTime.UtcNow.AddHours(-1),
            Scope = "All",
            CreatedBy = "admin"
        });

        Assert.False(await service.IsInMaintenanceAsync());
    }

    // WorkerLeaseService tests
    [Fact]
    public async Task WorkerLeaseService_AcquireAndRelease()
    {
        var service = new WorkerLeaseService(_db, _license, NullLogger<WorkerLeaseService>.Instance);
        _license.GetLimit("maxworkerinstances").Returns(1);

        var acquired = await service.TryAcquireLeaseAsync("worker-1");
        Assert.True(acquired);

        var active = await service.GetActiveLeasesAsync();
        Assert.Single(active);

        await service.ReleaseLeaseAsync("worker-1");
        active = await service.GetActiveLeasesAsync();
        Assert.Empty(active);
    }

    [Fact]
    public async Task WorkerLeaseService_SecondWorker_Blocked()
    {
        var service = new WorkerLeaseService(_db, _license, NullLogger<WorkerLeaseService>.Instance);
        _license.GetLimit("maxworkerinstances").Returns(1);

        await service.TryAcquireLeaseAsync("worker-1");
        var second = await service.TryAcquireLeaseAsync("worker-2");
        Assert.False(second);
    }

    // Validation tests
    private static List<ValidationResult> ValidateModel(object model)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(model);
        Validator.TryValidateObject(model, context, results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void Mailbox_Validation_RequiredFields()
    {
        var mailbox = new Mailbox { Name = "", Host = "", Username = "", EncryptedPassword = "", Folder = "" };
        var results = ValidateModel(mailbox);
        Assert.True(results.Count >= 4);
        Assert.Contains(results, r => r.MemberNames.Contains("Name"));
        Assert.Contains(results, r => r.MemberNames.Contains("Host"));
        Assert.Contains(results, r => r.MemberNames.Contains("Username"));
        Assert.Contains(results, r => r.MemberNames.Contains("Folder"));
    }

    [Fact]
    public void Mailbox_Validation_ValidModel()
    {
        var mailbox = new Mailbox { Name = "Test", Host = "imap.test.com", Port = 993, Username = "user", EncryptedPassword = "pass", Folder = "INBOX" };
        var results = ValidateModel(mailbox);
        Assert.Empty(results);
    }

    [Fact]
    public void Mailbox_Validation_InvalidPort()
    {
        var mailbox = new Mailbox { Name = "Test", Host = "host", Username = "u", EncryptedPassword = "p", Folder = "INBOX", Port = 0 };
        var results = ValidateModel(mailbox);
        Assert.Contains(results, r => r.MemberNames.Contains("Port"));
    }

    [Fact]
    public void Rule_Validation_RequiredFields()
    {
        var rule = new Rule { Name = "", Criteria = "" };
        var results = ValidateModel(rule);
        Assert.Contains(results, r => r.MemberNames.Contains("Name"));
        Assert.Contains(results, r => r.MemberNames.Contains("Criteria"));
    }

    [Fact]
    public void Job_Validation_RequiredFields()
    {
        var job = new Job { Name = "", MailboxId = 0, RuleId = 0 };
        var results = ValidateModel(job);
        Assert.Contains(results, r => r.MemberNames.Contains("Name"));
        Assert.Contains(results, r => r.MemberNames.Contains("MailboxId"));
        Assert.Contains(results, r => r.MemberNames.Contains("RuleId"));
    }

    [Fact]
    public void SnmpTarget_Validation_V2c_RequiresCommunityString()
    {
        var target = new SnmpTarget { Name = "T", Host = "h", Port = 162, Version = SnmpVersion.V2c, CommunityString = null };
        var results = ValidateModel(target);
        Assert.Contains(results, r => r.MemberNames.Contains("CommunityString"));
    }

    [Fact]
    public void SnmpTarget_Validation_V2c_WithCommunity_Valid()
    {
        var target = new SnmpTarget { Name = "T", Host = "h", Port = 162, Version = SnmpVersion.V2c, CommunityString = "public" };
        var results = ValidateModel(target);
        Assert.Empty(results);
    }

    [Fact]
    public void SnmpTarget_Validation_V3_RequiresSecurityName()
    {
        var target = new SnmpTarget { Name = "T", Host = "h", Port = 162, Version = SnmpVersion.V3, SecurityName = null };
        var results = ValidateModel(target);
        Assert.Contains(results, r => r.MemberNames.Contains("SecurityName"));
    }

    [Fact]
    public void SnmpTarget_Validation_V3_WithSecurityName_Valid()
    {
        var target = new SnmpTarget { Name = "T", Host = "h", Port = 162, Version = SnmpVersion.V3, SecurityName = "admin" };
        var results = ValidateModel(target);
        Assert.Empty(results);
    }

    [Fact]
    public void WebhookTarget_Validation_RequiredFields()
    {
        var target = new WebhookTarget { Name = "", Url = "" };
        var results = ValidateModel(target);
        Assert.Contains(results, r => r.MemberNames.Contains("Name"));
        Assert.Contains(results, r => r.MemberNames.Contains("Url"));
    }

    [Fact]
    public void WebhookTarget_Validation_InvalidUrl()
    {
        var target = new WebhookTarget { Name = "T", Url = "not-a-url" };
        var results = ValidateModel(target);
        Assert.Contains(results, r => r.MemberNames.Contains("Url"));
    }

    [Fact]
    public void WebhookTarget_Validation_ValidUrl()
    {
        var target = new WebhookTarget { Name = "T", Url = "https://hooks.example.com/webhook" };
        var results = ValidateModel(target);
        Assert.Empty(results);
    }

    [Fact]
    public void Schedule_Validation_RequiredFields()
    {
        var schedule = new Schedule { Name = "", JobId = 0, IntervalMinutes = 0 };
        var results = ValidateModel(schedule);
        Assert.Contains(results, r => r.MemberNames.Contains("Name"));
        Assert.Contains(results, r => r.MemberNames.Contains("JobId"));
        Assert.Contains(results, r => r.MemberNames.Contains("IntervalMinutes"));
    }

    [Fact]
    public void MaintenanceWindow_Validation_RequiredFields()
    {
        var window = new MaintenanceWindow { Name = "", Scope = "", CreatedBy = "" };
        var results = ValidateModel(window);
        Assert.Contains(results, r => r.MemberNames.Contains("Name"));
        Assert.Contains(results, r => r.MemberNames.Contains("Scope"));
        Assert.Contains(results, r => r.MemberNames.Contains("CreatedBy"));
    }
}
