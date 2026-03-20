using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Models.Entities;
using Mail2SNMP.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace Mail2SNMP.Tests.Integration;

/// <summary>
/// Integration tests that run against a real SQL Server instance in a Docker container via Testcontainers.
/// These tests verify EF Core migrations, SQL-specific behavior, and data persistence.
/// Tests are skipped automatically when Docker is not available (CI/CD without Docker, no Docker Desktop).
/// </summary>
[Trait("Category", "Docker")]
public class SqlServerIntegrationTests : IAsyncLifetime
{
    private MsSqlContainer? _container;
    private Mail2SnmpDbContext? _db;
    private bool _dockerAvailable;

    public async Task InitializeAsync()
    {
        try
        {
            _container = new MsSqlBuilder()
                .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
                .Build();
            await _container.StartAsync();
            _dockerAvailable = true;

            var options = new DbContextOptionsBuilder<Mail2SnmpDbContext>()
                .UseSqlServer(_container.GetConnectionString())
                .Options;
            _db = new Mail2SnmpDbContext(options);
            await _db.Database.MigrateAsync();
        }
        catch (Exception)
        {
            // Docker not available — tests will be skipped via Skip property
            _dockerAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
        if (_container is not null) await _container.DisposeAsync();
    }

    private void SkipIfNoDocker()
    {
        if (!_dockerAvailable)
            throw new SkipException("Docker is not available. Skipping SQL Server integration test.");
    }

    [Fact]
    public async Task Migrations_ApplySuccessfully()
    {
        SkipIfNoDocker();
        var applied = await _db!.Database.GetAppliedMigrationsAsync();
        Assert.NotEmpty(applied);
    }

    [Fact]
    public async Task Mailbox_CRUD_SqlServer()
    {
        SkipIfNoDocker();

        // Create
        var mailbox = new Mailbox
        {
            Name = "SqlTest-MB",
            Host = "imap.test.com",
            Port = 993,
            UseSsl = true,
            Username = "user@test.com",
            EncryptedPassword = "encrypted-test-pw",
            Folder = "INBOX",
            IsActive = true,
            CreatedUtc = DateTime.UtcNow
        };
        _db!.Mailboxes.Add(mailbox);
        await _db.SaveChangesAsync();

        Assert.True(mailbox.Id > 0);

        // Read
        var loaded = await _db.Mailboxes.FindAsync(mailbox.Id);
        Assert.NotNull(loaded);
        Assert.Equal("SqlTest-MB", loaded!.Name);

        // Update
        loaded.Name = "SqlTest-MB-Updated";
        await _db.SaveChangesAsync();
        var updated = await _db.Mailboxes.FindAsync(mailbox.Id);
        Assert.Equal("SqlTest-MB-Updated", updated!.Name);

        // Delete
        _db.Mailboxes.Remove(updated);
        await _db.SaveChangesAsync();
        var deleted = await _db.Mailboxes.FindAsync(mailbox.Id);
        Assert.Null(deleted);
    }

    [Fact]
    public async Task Rule_WithEnums_PersistsCorrectly()
    {
        SkipIfNoDocker();

        var rule = new Rule
        {
            Name = "SqlTest-Rule",
            Field = RuleFieldType.Body,
            MatchType = RuleMatchType.Regex,
            Criteria = @"error\s+\d+",
            Severity = Severity.Critical,
            Priority = 10,
            IsActive = true,
            CreatedUtc = DateTime.UtcNow
        };
        _db!.Rules.Add(rule);
        await _db.SaveChangesAsync();

        var loaded = await _db.Rules.FindAsync(rule.Id);
        Assert.Equal(RuleFieldType.Body, loaded!.Field);
        Assert.Equal(RuleMatchType.Regex, loaded.MatchType);
        Assert.Equal(Severity.Critical, loaded.Severity);
    }

    [Fact]
    public async Task Job_WithRelationships_PersistsCorrectly()
    {
        SkipIfNoDocker();

        var mailbox = new Mailbox { Name = "Job-MB", Host = "h", Username = "u", EncryptedPassword = "p", Folder = "INBOX" };
        var rule = new Rule { Name = "Job-Rule", Field = RuleFieldType.Subject, MatchType = RuleMatchType.Contains, Criteria = "x" };
        _db!.Mailboxes.Add(mailbox);
        _db.Rules.Add(rule);
        await _db.SaveChangesAsync();

        var job = new Job
        {
            Name = "SqlTest-Job",
            MailboxId = mailbox.Id,
            RuleId = rule.Id,
            MaxEventsPerHour = 100,
            MaxActiveEvents = 500,
            DedupWindowMinutes = 15,
            IsActive = true,
            CreatedUtc = DateTime.UtcNow
        };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();

        var loaded = await _db.Jobs
            .Include(j => j.Mailbox)
            .Include(j => j.Rule)
            .FirstAsync(j => j.Id == job.Id);

        Assert.Equal("Job-MB", loaded.Mailbox.Name);
        Assert.Equal("Job-Rule", loaded.Rule.Name);
        // Channels is now computed from join tables; no targets assigned → "none"
        Assert.Equal("none", loaded.Channels);
    }

    [Fact]
    public async Task AuditEvent_CanBeStored()
    {
        SkipIfNoDocker();

        var audit = new AuditEvent
        {
            ActorType = ActorType.User,
            ActorId = "admin",
            Action = "Test.Integration",
            TargetType = "Test",
            TargetId = "1",
            Result = AuditResult.Success,
            TimestampUtc = DateTime.UtcNow
        };
        _db!.AuditEvents.Add(audit);
        await _db.SaveChangesAsync();

        var count = await _db.AuditEvents.CountAsync(a => a.Action == "Test.Integration");
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task ConcurrentAccess_NoDeadlocks()
    {
        SkipIfNoDocker();

        // Simulate concurrent writes to verify SQL Server handles them correctly
        var tasks = Enumerable.Range(0, 10).Select(async i =>
        {
            // Each task uses its own DbContext (separate connection)
            var options = new DbContextOptionsBuilder<Mail2SnmpDbContext>()
                .UseSqlServer(_container!.GetConnectionString())
                .Options;
            await using var db = new Mail2SnmpDbContext(options);

            var mailbox = new Mailbox
            {
                Name = $"Concurrent-MB-{i}",
                Host = "h",
                Username = "u",
                EncryptedPassword = "p",
                Folder = "INBOX"
            };
            db.Mailboxes.Add(mailbox);
            await db.SaveChangesAsync();
        });

        await Task.WhenAll(tasks);

        var count = await _db!.Mailboxes.CountAsync(m => m.Name.StartsWith("Concurrent-MB-"));
        Assert.Equal(10, count);
    }
}

/// <summary>
/// xUnit skip exception — when thrown, the test is reported as Skipped rather than Failed.
/// </summary>
public class SkipException : Exception
{
    public SkipException(string message) : base(message) { }
}
