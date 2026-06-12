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
            e.Property(x => x.RowVersion).IsRowVersion();
        });

        builder.Entity<Rule>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Criteria).HasMaxLength(2000).IsRequired();
            e.Property(x => x.RowVersion).IsRowVersion();
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
            e.Property(x => x.RowVersion).IsRowVersion();
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
            e.Property(x => x.RowVersion).IsRowVersion();
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
            e.Property(x => x.RowVersion).IsRowVersion();
        });

        builder.Entity<WebhookTarget>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Url).HasMaxLength(2000).IsRequired();
            e.Property(x => x.EncryptedSecret).HasMaxLength(2000);
            e.Property(x => x.RowVersion).IsRowVersion();
        });

        builder.Entity<Event>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Job).WithMany(j => j.Events).HasForeignKey(x => x.JobId);
            e.Property(x => x.Subject).HasMaxLength(500);
            e.Property(x => x.MailFrom).HasMaxLength(500);
            e.HasIndex(x => new { x.JobId, x.State });
            e.Property(x => x.RowVersion).IsRowVersion();
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
            e.HasOne(x => x.WebhookTarget).WithMany().HasForeignKey(x => x.WebhookTargetId);
            e.HasOne(x => x.Event).WithMany().HasForeignKey(x => x.EventId);
            e.Property(x => x.LockedByInstanceId).HasMaxLength(100);
            e.HasIndex(x => new { x.Status, x.LockedUntilUtc });
            e.Property(x => x.RowVersion).IsRowVersion();
        });

        builder.Entity<ProcessedMail>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Mailbox).WithMany().HasForeignKey(x => x.MailboxId);
            e.Property(x => x.MessageId).HasMaxLength(1000);
            e.HasIndex(x => x.ProcessedUtc);
            e.HasIndex(x => new { x.MessageId, x.MailboxId }).IsUnique();
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
