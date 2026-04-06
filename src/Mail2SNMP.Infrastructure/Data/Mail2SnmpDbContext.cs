using Mail2SNMP.Models.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Mail2SNMP.Infrastructure.Data;

/// <summary>
/// Entity Framework Core database context with Identity support. Configures all entity mappings, indexes, and constraints.
/// </summary>
public class Mail2SnmpDbContext : IdentityDbContext<AppUser>
{
    public Mail2SnmpDbContext(DbContextOptions<Mail2SnmpDbContext> options) : base(options) { }

    public DbSet<Mailbox> Mailboxes => Set<Mailbox>();
    public DbSet<Rule> Rules => Set<Rule>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<Schedule> Schedules => Set<Schedule>();
    public DbSet<SnmpTarget> SnmpTargets => Set<SnmpTarget>();
    public DbSet<WebhookTarget> WebhookTargets => Set<WebhookTarget>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<EventDedup> EventDedups => Set<EventDedup>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<MaintenanceWindow> MaintenanceWindows => Set<MaintenanceWindow>();
    public DbSet<DeadLetterEntry> DeadLetterEntries => Set<DeadLetterEntry>();
    public DbSet<ProcessedMail> ProcessedMails => Set<ProcessedMail>();
    public DbSet<WorkerLease> WorkerLeases => Set<WorkerLease>();
    public DbSet<AuthTicket> AuthTickets => Set<AuthTicket>();
    public DbSet<JobSnmpTarget> JobSnmpTargets => Set<JobSnmpTarget>();
    public DbSet<JobWebhookTarget> JobWebhookTargets => Set<JobWebhookTarget>();
    public DbSet<Setting> Settings => Set<Setting>();

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
            e.Property(x => x.CommunityString).HasMaxLength(500);
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
