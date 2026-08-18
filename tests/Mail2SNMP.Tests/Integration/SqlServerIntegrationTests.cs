using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Models.Entities;
using Mail2SNMP.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace Mail2SNMP.Tests.Integration;

/// <summary>
/// Integration tests that run against a real SQL Server instance — the documented
/// production provider. They verify EF Core migrations, SQL-specific behaviour and data
/// persistence against the actual engine rather than a substitute.
/// </summary>
/// <remarks>
/// Two ways to get a server, tried in order:
/// <list type="number">
/// <item>The <c>MAIL2SNMP_TEST_SQLSERVER</c> environment variable, if set, is used as the
/// connection string. This lets the suite run against any reachable instance — a developer's
/// local SQL Server, a CI service container, a shared test server — on a machine where Docker
/// is unavailable or broken.</item>
/// <item>Otherwise a throwaway container via Testcontainers.</item>
/// </list>
/// If neither is available the tests report as SKIPPED, not passed: the distinction matters,
/// because a suite that silently reports green without ever touching SQL Server is exactly how
/// the 1.1.0 schema defect (C-2) reached a release.
/// </remarks>
[Trait("Category", "Docker")]
public class SqlServerIntegrationTests : IAsyncLifetime
{
    /// <summary>Environment variable holding a connection string to an existing SQL Server.</summary>
    private const string ConnectionStringVariable = "MAIL2SNMP_TEST_SQLSERVER";

    private MsSqlContainer? _container;
    private Mail2SnmpDbContext? _db;
    private bool _serverAvailable;
    private string? _skipReason;

    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            _serverAvailable = true;
        }
        else
        {
            // H-8: ONLY acquiring the server may be swallowed into "skip". The migration
            // below must be allowed to throw — an earlier version wrapped it in the same
            // catch, so a broken schema (the SQLite-typed migration set that made SQL
            // Server unusable in 1.1.0) reported itself as "Docker not available" and the
            // suite went green.
            try
            {
                _container = new MsSqlBuilder()
                    .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
                    .Build();
                await _container.StartAsync();
                connectionString = _container.GetConnectionString();
                _serverAvailable = true;
            }
            catch (Exception ex)
            {
                _serverAvailable = false;
                _skipReason =
                    $"No SQL Server available. Docker could not provide one ({ex.GetType().Name}), " +
                    $"and {ConnectionStringVariable} is not set. Set that variable to a connection " +
                    "string to run these against an existing instance.";
                return;
            }
        }

        // Deliberately outside the catch: a migration failure is a real test failure.
        var options = new DbContextOptionsBuilder<Mail2SnmpDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        _db = new Mail2SnmpDbContext(options);
        await _db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_db is not null) await _db.DisposeAsync();
        if (_container is not null) await _container.DisposeAsync();
    }

    private void SkipIfNoSqlServer()
    {
        // Skip.IfNot reports the test as SKIPPED rather than FAILED. A plain exception here
        // would show up as six failures on every machine without a server.
        Skip.IfNot(_serverAvailable, _skipReason ?? "No SQL Server available.");
    }

    [SkippableFact]
    public async Task Migrations_ApplySuccessfully()
    {
        SkipIfNoSqlServer();
        var applied = await _db!.Database.GetAppliedMigrationsAsync();
        Assert.NotEmpty(applied);
    }

    [SkippableFact]
    public async Task Mailbox_CRUD_SqlServer()
    {
        SkipIfNoSqlServer();

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

    [SkippableFact]
    public async Task Rule_WithEnums_PersistsCorrectly()
    {
        SkipIfNoSqlServer();

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

    [SkippableFact]
    public async Task Job_WithRelationships_PersistsCorrectly()
    {
        SkipIfNoSqlServer();

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

    [SkippableFact]
    public async Task AuditEvent_CanBeStored()
    {
        SkipIfNoSqlServer();

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

    [SkippableFact]
    public async Task ConcurrentAccess_NoDeadlocks()
    {
        SkipIfNoSqlServer();

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

