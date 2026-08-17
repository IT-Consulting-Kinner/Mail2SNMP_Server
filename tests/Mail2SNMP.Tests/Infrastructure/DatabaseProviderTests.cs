using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Models.Entities;
using Mail2SNMP.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Mail2SNMP.Tests.Infrastructure;

/// <summary>
/// Provider-level regression tests for the two defects that made release 1.1.0
/// unusable on BOTH supported database providers, and that the whole existing
/// suite was structurally incapable of catching.
/// </summary>
/// <remarks>
/// Why the old suite missed them: every other test uses
/// <c>UseInMemoryDatabase</c>, which enforces neither store-generated value
/// semantics nor NOT NULL, and the only SQL Server coverage
/// (<see cref="SqlServerIntegrationTests"/>) funnels *every* exception into
/// "Docker unavailable" and reports itself as skipped. A schema defect therefore
/// masqueraded as an absent container.
///
/// These tests need no Docker and no server: the SQLite cases run against a real
/// temp-file database, and the SQL Server case only asks the provider's SQL
/// generator what DDL it *would* emit.
/// </remarks>
public class DatabaseProviderTests
{
    private static (Mail2SnmpDbContext Db, string Path) NewSqliteDb()
    {
        var path = Path.Combine(Path.GetTempPath(), $"m2s-provider-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<Mail2SnmpDbContext>()
            .UseSqlite($"Data Source={path}")
            .Options;
        var db = new Mail2SnmpDbContext(options);
        db.Database.Migrate();
        return (db, path);
    }

    /// <summary>
    /// C-1: on SQLite every entity carrying a <c>RowVersion</c> concurrency token must
    /// be insertable. Before the fix EF marked the property store-generated and omitted
    /// it from the INSERT while the column is <c>NOT NULL</c> without a default, so this
    /// failed with <c>SQLite Error 19: NOT NULL constraint failed</c> — i.e. no mailbox,
    /// rule, job, target, schedule, event or dead letter could ever be created on the
    /// default provider.
    /// </summary>
    [Fact]
    public async Task Sqlite_CanInsert_EveryEntityCarryingARowVersion()
    {
        var (db, path) = NewSqliteDb();
        try
        {
            var mailbox = new Mailbox { Name = "MB", Host = "h", Username = "u", EncryptedPassword = "p", Folder = "INBOX" };
            var rule = new Rule { Name = "R", Field = RuleFieldType.Subject, MatchType = RuleMatchType.Contains, Criteria = "x" };
            db.Mailboxes.Add(mailbox);
            db.Rules.Add(rule);
            await db.SaveChangesAsync();

            var job = new Job { Name = "J", MailboxId = mailbox.Id, RuleId = rule.Id };
            var snmp = new SnmpTarget { Name = "S", Host = "127.0.0.1", Port = 162, Version = SnmpVersion.V2c, EncryptedCommunityString = "c" };
            var webhook = new WebhookTarget { Name = "W", Url = "https://example.com/hook" };
            db.Jobs.Add(job);
            db.SnmpTargets.Add(snmp);
            db.WebhookTargets.Add(webhook);
            await db.SaveChangesAsync();

            var schedule = new Schedule { Name = "Sch", JobId = job.Id, IntervalMinutes = 5 };
            var evt = new Event { JobId = job.Id, Severity = Severity.Warning, Subject = "s" };
            db.Schedules.Add(schedule);
            db.Events.Add(evt);
            await db.SaveChangesAsync();

            db.DeadLetterEntries.Add(new DeadLetterEntry
            {
                WebhookTargetId = webhook.Id,
                EventId = evt.Id,
                PayloadJson = "{}",
                Status = DeadLetterStatus.Pending
            });
            await db.SaveChangesAsync();

            // All eight RowVersion-bearing entity types round-tripped.
            Assert.True(mailbox.Id > 0 && rule.Id > 0 && job.Id > 0 && snmp.Id > 0
                        && webhook.Id > 0 && schedule.Id > 0 && evt.Id > 0);
            Assert.Equal(1, await db.DeadLetterEntries.CountAsync());
        }
        finally
        {
            await db.DisposeAsync();
            SqliteCleanup(path);
        }
    }

    /// <summary>
    /// C-1: the client-generated token must still behave as a concurrency token —
    /// a stale writer has to be rejected rather than silently overwriting.
    /// </summary>
    [Fact]
    public async Task Sqlite_RowVersion_StillDetectsConcurrentOverwrite()
    {
        var (db, path) = NewSqliteDb();
        try
        {
            var mailbox = new Mailbox { Name = "MB", Host = "h", Username = "u", EncryptedPassword = "p", Folder = "INBOX" };
            db.Mailboxes.Add(mailbox);
            await db.SaveChangesAsync();

            // Two contexts load the same row, then both write.
            var optionsB = new DbContextOptionsBuilder<Mail2SnmpDbContext>().UseSqlite($"Data Source={path}").Options;
            await using var dbB = new Mail2SnmpDbContext(optionsB);
            var copyB = await dbB.Mailboxes.FirstAsync(m => m.Id == mailbox.Id);

            mailbox.Host = "first-writer";
            await db.SaveChangesAsync();

            copyB.Host = "second-writer";
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => dbB.SaveChangesAsync());
        }
        finally
        {
            await db.DisposeAsync();
            SqliteCleanup(path);
        }
    }

    /// <summary>
    /// C-2: the migration set is applied to SQL Server too (the documented production
    /// provider), so it must not carry SQLite store types. Before the fix every column
    /// was hardcoded <c>TEXT</c>/<c>INTEGER</c>/<c>BLOB</c>: <c>BLOB</c> does not exist
    /// on SQL Server, <c>TEXT</c> is illegal as a key column, and no key was an identity
    /// column — the documented deployment could not create its schema at all.
    /// </summary>
    /// <remarks>
    /// Runs without a server: <see cref="IMigrator.GenerateScript"/> asks the provider's
    /// SQL generator what DDL the migrations produce.
    /// </remarks>
    [Fact]
    public void SqlServer_MigrationScript_UsesSqlServerTypesAndIdentityKeys()
    {
        var options = new DbContextOptionsBuilder<Mail2SnmpDbContext>()
            .UseSqlServer("Server=localhost;Database=schema-check;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        using var db = new Mail2SnmpDbContext(options);

        var script = db.GetService<IMigrator>().GenerateScript();

        foreach (var sqliteType in new[] { "TEXT", "BLOB", "INTEGER" })
        {
            Assert.DoesNotContain($" {sqliteType} ", script);
            Assert.DoesNotContain($" {sqliteType},", script);
            Assert.DoesNotContain($" {sqliteType})", script);
        }

        // Positive assertions: the provider actually produced SQL Server DDL.
        Assert.Contains("nvarchar", script);
        Assert.Contains("IDENTITY", script);
        Assert.Contains("rowversion", script);
        Assert.Contains("CREATE TABLE [Mailboxes]", script);
    }

    private static void SqliteCleanup(string path)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(path)) File.Delete(path); } catch { /* temp file */ }
    }
}
