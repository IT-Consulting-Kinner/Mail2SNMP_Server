using Mail2SNMP.Models.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Mail2SNMP.Infrastructure.Data;

/// <summary>
/// Entity Framework Core database context with Identity support. Configures all entity mappings, indexes, and constraints.
/// </summary>
public class Mail2SnmpDbContext : IdentityDbContext<AppUser>
{
    /// <summary>
    /// Initializes a new instance of the context with the supplied options (provider,
    /// connection string, command timeout, etc.) configured during dependency injection.
    /// </summary>
    /// <param name="options">The EF Core options used to configure this context.</param>
    public Mail2SnmpDbContext(DbContextOptions<Mail2SnmpDbContext> options) : base(options) { }

    /// <summary>Configured IMAP mailboxes that are polled for incoming mail.</summary>
    public DbSet<Mailbox> Mailboxes => Set<Mailbox>();

    /// <summary>Matching rules whose criteria decide which e-mails trigger a job.</summary>
    public DbSet<Rule> Rules => Set<Rule>();

    /// <summary>Jobs that bind a mailbox and rule to one or more notification targets.</summary>
    public DbSet<Job> Jobs => Set<Job>();

    /// <summary>Quartz schedule definitions that drive periodic execution of jobs.</summary>
    public DbSet<Schedule> Schedules => Set<Schedule>();

    /// <summary>SNMP trap destinations (host, version, credentials) that receive notifications.</summary>
    public DbSet<SnmpTarget> SnmpTargets => Set<SnmpTarget>();

    /// <summary>Webhook (HTTP) destinations that receive notifications.</summary>
    public DbSet<WebhookTarget> WebhookTargets => Set<WebhookTarget>();

    /// <summary>Events (alarms) raised by jobs, tracked through their lifecycle state.</summary>
    public DbSet<Event> Events => Set<Event>();

    /// <summary>Deduplication records that suppress repeat events within the dedup window.</summary>
    public DbSet<EventDedup> EventDedups => Set<EventDedup>();

    /// <summary>Append-only audit log of user/system actions for compliance and forensics.</summary>
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    /// <summary>Maintenance windows during which notifications are suppressed for a scope.</summary>
    public DbSet<MaintenanceWindow> MaintenanceWindows => Set<MaintenanceWindow>();

    /// <summary>Failed webhook deliveries parked for retry (the dead-letter queue).</summary>
    public DbSet<DeadLetterEntry> DeadLetterEntries => Set<DeadLetterEntry>();

    /// <summary>Tracking records of already-processed e-mails, used for IMAP message-id dedup.</summary>
    public DbSet<ProcessedMail> ProcessedMails => Set<ProcessedMail>();

    /// <summary>Distributed worker leases used to coordinate a single active worker across instances.</summary>
    public DbSet<WorkerLease> WorkerLeases => Set<WorkerLease>();

    /// <summary>Persisted authentication tickets backing the cookie/session ticket store.</summary>
    public DbSet<AuthTicket> AuthTickets => Set<AuthTicket>();

    /// <summary>Join table assigning SNMP targets to jobs (many-to-many).</summary>
    public DbSet<JobSnmpTarget> JobSnmpTargets => Set<JobSnmpTarget>();

    /// <summary>Join table assigning webhook targets to jobs (many-to-many).</summary>
    public DbSet<JobWebhookTarget> JobWebhookTargets => Set<JobWebhookTarget>();

    /// <summary>Persisted key/value application settings stored in the database.</summary>
    public DbSet<Setting> Settings => Set<Setting>();

    /// <summary>API keys used for header-based REST authentication (stored hashed).</summary>
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    /// <summary>
    /// C-1: <c>true</c> when the context runs on SQLite, which has no server-generated
    /// row-version concept. Consulted by <see cref="OnModelCreating"/> and by the
    /// <c>SaveChanges</c> overrides below.
    /// </summary>
    private bool IsSqlite =>
        Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// C-1: configures the optimistic-concurrency token for an entity in a
    /// provider-appropriate way.
    /// </summary>
    /// <remarks>
    /// On SQL Server the column is a native <c>rowversion</c>: the server maintains it and
    /// EF must not send a value. On SQLite there is no such mechanism —
    /// <c>IsRowVersion()</c> nevertheless marks the property store-generated
    /// (<c>ValueGenerated.OnAddOrUpdate</c>, whose <c>BeforeSaveBehavior</c> is
    /// <c>Ignore</c>), so EF omitted the column from every INSERT while the migration
    /// declares it <c>NOT NULL</c> without a default. The result was
    /// <c>SQLite Error 19: NOT NULL constraint failed</c> on the first write of every
    /// entity carrying a row version — i.e. the product could not persist any
    /// configuration at all on its default provider. On SQLite the token is therefore
    /// client-generated: still a concurrency token (so a concurrent overwrite is
    /// detected), but written by <see cref="StampRowVersions"/> on each save.
    /// </remarks>
    private void ConfigureRowVersion<TEntity>(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> e,
        System.Linq.Expressions.Expression<Func<TEntity, byte[]>> property)
        where TEntity : class
    {
        if (IsSqlite)
            e.Property(property).IsConcurrencyToken().ValueGeneratedNever();
        else
            e.Property(property).IsRowVersion();
    }

    /// <summary>
    /// C-1: assigns a fresh row-version value to every added or modified entity that
    /// carries a client-generated concurrency token (SQLite only). On SQL Server the
    /// database generates the value and this method does nothing.
    /// </summary>
    /// <remarks>
    /// Implemented on the context rather than as an interceptor so that it applies no
    /// matter how the context was constructed — including the direct
    /// <c>new Mail2SnmpDbContext(options)</c> used by tests and design-time tooling,
    /// which never sees DI-registered interceptors.
    /// </remarks>
    private void StampRowVersions()
    {
        if (!IsSqlite) return;

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;

            var rowVersion = entry.Metadata.FindProperty("RowVersion");
            if (rowVersion is null || rowVersion.ClrType != typeof(byte[])) continue;

            // A new value on every write is what makes the token detect concurrent
            // overwrites: a second writer's UPDATE ... WHERE RowVersion = <old> matches
            // zero rows and EF raises DbUpdateConcurrencyException, exactly as with a
            // server-generated rowversion.
            entry.Property(rowVersion.Name).CurrentValue = Guid.NewGuid().ToByteArray();
        }
    }

    /// <inheritdoc />
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampRowVersions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampRowVersions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Configures entity mappings, column constraints, indexes, and relationships for all domain entities.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Mailbox>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Host).HasMaxLength(500).IsRequired();
            e.Property(x => x.Username).HasMaxLength(500);
            e.Property(x => x.EncryptedPassword).HasMaxLength(2000);
            ConfigureRowVersion(e, x => x.RowVersion);
        });

        builder.Entity<Rule>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Criteria).HasMaxLength(2000).IsRequired();
            ConfigureRowVersion(e, x => x.RowVersion);
        });

        // G6: API keys for header-based REST authentication
        builder.Entity<ApiKey>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.KeyHash).HasMaxLength(128).IsRequired();
            e.Property(x => x.KeyPrefix).HasMaxLength(16).IsRequired();
            e.Property(x => x.Scopes).HasMaxLength(200).IsRequired();
            e.Property(x => x.CreatedBy).HasMaxLength(200);
            e.HasIndex(x => x.KeyHash).IsUnique();
            e.HasIndex(x => x.KeyPrefix);
        });

        builder.Entity<Job>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasOne(x => x.Mailbox).WithMany(m => m.Jobs).HasForeignKey(x => x.MailboxId);
            e.HasOne(x => x.Rule).WithMany(r => r.Jobs).HasForeignKey(x => x.RuleId);
            e.Ignore(x => x.Channels); // Computed [NotMapped] property — derived from join tables
            ConfigureRowVersion(e, x => x.RowVersion);
        });

        // Many-to-many: Job ↔ SnmpTarget (per-job target assignment)
        builder.Entity<JobSnmpTarget>(e =>
        {
            e.HasKey(x => new { x.JobId, x.SnmpTargetId });
            e.HasOne(x => x.Job).WithMany(j => j.JobSnmpTargets)
                .HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.SnmpTarget).WithMany()
                .HasForeignKey(x => x.SnmpTargetId).OnDelete(DeleteBehavior.Restrict);
        });

        // Many-to-many: Job ↔ WebhookTarget (per-job target assignment)
        builder.Entity<JobWebhookTarget>(e =>
        {
            e.HasKey(x => new { x.JobId, x.WebhookTargetId });
            e.HasOne(x => x.Job).WithMany(j => j.JobWebhookTargets)
                .HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.WebhookTarget).WithMany()
                .HasForeignKey(x => x.WebhookTargetId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Schedule>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasOne(x => x.Job).WithMany(j => j.Schedules).HasForeignKey(x => x.JobId);
            ConfigureRowVersion(e, x => x.RowVersion);
        });

        builder.Entity<SnmpTarget>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Host).HasMaxLength(500).IsRequired();
            e.Property(x => x.EncryptedCommunityString).HasMaxLength(2000);
            e.Property(x => x.SecurityName).HasMaxLength(200);
            e.Property(x => x.EncryptedAuthPassword).HasMaxLength(2000);
            e.Property(x => x.EncryptedPrivPassword).HasMaxLength(2000);
            e.Property(x => x.EngineId).HasMaxLength(200);
            e.Property(x => x.EnterpriseTrapOid).HasMaxLength(500);
            ConfigureRowVersion(e, x => x.RowVersion);
        });

        builder.Entity<WebhookTarget>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Url).HasMaxLength(2000).IsRequired();
            e.Property(x => x.EncryptedSecret).HasMaxLength(2000);
            ConfigureRowVersion(e, x => x.RowVersion);
        });

        builder.Entity<Event>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Job).WithMany(j => j.Events).HasForeignKey(x => x.JobId);
            e.Property(x => x.Subject).HasMaxLength(500);
            e.Property(x => x.MailFrom).HasMaxLength(500);
            e.HasIndex(x => new { x.JobId, x.State });
            // PF-5: the events list and the dashboard open-events count filter by State
            // alone (no JobId) and always order by CreatedUtc DESC. The (JobId, State)
            // index above cannot serve a state-only, time-ordered query because its
            // leading column is JobId, forcing a full scan + sort. This index makes the
            // state-filtered, newest-first Take(500) query seekable.
            e.HasIndex(x => new { x.State, x.CreatedUtc });
            ConfigureRowVersion(e, x => x.RowVersion);
        });

        builder.Entity<EventDedup>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.DedupKeyHash).HasMaxLength(64).IsFixedLength().IsRequired();
            e.HasOne(x => x.Job).WithMany().HasForeignKey(x => x.JobId);
            e.HasOne(x => x.Event).WithMany().HasForeignKey(x => x.EventId);
            e.HasIndex(x => new { x.DedupKeyHash, x.JobId }).IsUnique();
            e.HasIndex(x => x.LastSeenUtc);
        });

        builder.Entity<AuditEvent>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Action).HasMaxLength(200).IsRequired();
            e.Property(x => x.ActorId).HasMaxLength(200).IsRequired();
            e.Property(x => x.Details).HasMaxLength(4096);
            e.Property(x => x.IpAddress).HasMaxLength(50);
            e.Property(x => x.UserAgent).HasMaxLength(500);
            e.Property(x => x.CorrelationId).HasMaxLength(100);
            e.HasIndex(x => x.TimestampUtc);
            e.HasIndex(x => x.Action);
        });

        builder.Entity<MaintenanceWindow>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Scope).HasMaxLength(500);
            e.Property(x => x.CreatedBy).HasMaxLength(200);
        });

        builder.Entity<DeadLetterEntry>(e =>
        {
            e.HasKey(x => x.Id);
            // UC-3: an entry references EITHER a webhook target OR an SNMP target
            // (both FKs nullable; exactly-one-set is enforced by the creating services).
            e.HasOne(x => x.WebhookTarget).WithMany().HasForeignKey(x => x.WebhookTargetId);
            e.HasOne(x => x.SnmpTarget).WithMany().HasForeignKey(x => x.SnmpTargetId);
            e.HasOne(x => x.Event).WithMany().HasForeignKey(x => x.EventId);
            e.Property(x => x.LockedByInstanceId).HasMaxLength(100);
            e.HasIndex(x => new { x.Status, x.LockedUntilUtc });
            ConfigureRowVersion(e, x => x.RowVersion);
        });

        builder.Entity<ProcessedMail>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Mailbox).WithMany().HasForeignKey(x => x.MailboxId);
            e.Property(x => x.MessageId).HasMaxLength(1000);
            e.HasIndex(x => x.ProcessedUtc);
            // H-1: the claim is scoped per JOB, not just per mailbox. With the old
            // (MessageId, MailboxId) key the first job to poll won the claim and every
            // other job on the same mailbox silently never fired.
            e.HasIndex(x => new { x.MessageId, x.MailboxId, x.JobId }).IsUnique();
            // Supports the "have all active jobs claimed this mail yet?" lookup that
            // decides when the message may be flagged Seen on the IMAP server.
            e.HasIndex(x => new { x.MailboxId, x.MessageId });
        });

        builder.Entity<WorkerLease>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.InstanceId).HasMaxLength(100).IsRequired();
            e.Property(x => x.MachineName).HasMaxLength(200);
            e.HasIndex(x => x.LastHeartbeatUtc);
            e.HasIndex(x => x.InstanceId).IsUnique();
        });

        builder.Entity<AuthTicket>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(200);
            e.Property(x => x.Value).IsRequired();
            e.HasIndex(x => x.ExpiresUtc);
        });

        builder.Entity<Setting>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(200);
            e.Property(x => x.Value).HasMaxLength(2000);
        });
    }
}
