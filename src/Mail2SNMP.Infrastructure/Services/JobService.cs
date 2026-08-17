using System.Text;
using Mail2SNMP.Core.Exceptions;
using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Core.Services;
using Mail2SNMP.Infrastructure.Data;
using Mail2SNMP.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Mail2SNMP.Infrastructure.Services;

/// <summary>
/// CRUD implementation for polling jobs, including eager-loading of related Mailbox and Rule.
/// </summary>
public class JobService : IJobService
{
    private readonly Mail2SnmpDbContext _db;
    private readonly ILicenseProvider _license;
    private readonly IAuditService _audit;
    private readonly RuleEvaluator _ruleEvaluator;
    private readonly IEnumerable<INotificationChannel> _channels;
    private readonly ILogger<JobService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JobService"/> class.
    /// </summary>
    /// <param name="db">The database context used to read and persist jobs and their target assignments.</param>
    /// <param name="license">The license provider that enforces the maximum job count.</param>
    /// <param name="audit">The audit service used to record job changes.</param>
    /// <param name="ruleEvaluator">The rule evaluator used to test rule matches during a dry run.</param>
    /// <param name="channels">The notification channels used by <see cref="SendTestEventAsync"/> (UC-7).</param>
    /// <param name="logger">The logger for dry-run diagnostics.</param>
    public JobService(Mail2SnmpDbContext db, ILicenseProvider license, IAuditService audit, RuleEvaluator ruleEvaluator, IEnumerable<INotificationChannel> channels, ILogger<JobService> logger)
    {
        _db = db;
        _license = license;
        _audit = audit;
        _ruleEvaluator = ruleEvaluator;
        _channels = channels;
        _logger = logger;
    }

    /// <summary>
    /// Returns all jobs ordered by name, eager-loading the related Mailbox, Rule, and target assignments.
    /// </summary>
    public async Task<IReadOnlyList<Job>> GetAllAsync(CancellationToken ct = default)
        => await _db.Jobs.AsNoTracking()
            .Include(j => j.Mailbox)
            .Include(j => j.Rule)
            .Include(j => j.JobSnmpTargets).ThenInclude(jst => jst.SnmpTarget)
            .Include(j => j.JobWebhookTargets).ThenInclude(jwt => jwt.WebhookTarget)
            .OrderBy(j => j.Name)
            .ToListAsync(ct);

    /// <summary>
    /// Returns a single job by its identifier with Mailbox, Rule, and target assignments included, or <c>null</c> if not found.
    /// </summary>
    public async Task<Job?> GetByIdAsync(int id, CancellationToken ct = default)
        => await _db.Jobs.AsNoTracking()
            .Include(j => j.Mailbox)
            .Include(j => j.Rule)
            .Include(j => j.JobSnmpTargets).ThenInclude(jst => jst.SnmpTarget)
            .Include(j => j.JobWebhookTargets).ThenInclude(jwt => jwt.WebhookTarget)
            .FirstOrDefaultAsync(j => j.Id == id, ct);

    /// <summary>
    /// Creates a new job after verifying the license-enforced job limit has not been reached.
    /// </summary>
    public async Task<Job> CreateAsync(Job job, CancellationToken ct = default)
    {
        var count = await _db.Jobs.CountAsync(ct);
        var max = _license.GetLimit("maxjobs");
        if (count >= max)
            throw new InvalidOperationException($"Community Edition limit: max {max} jobs. Upgrade to Enterprise for unlimited.");

        _db.Jobs.Add(job);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Job.Created", "Job", job.Id.ToString(), ct: ct);
        return job;
    }

    /// <summary>
    /// Updates an existing job configuration.
    /// </summary>
    public async Task<Job> UpdateAsync(Job job, CancellationToken ct = default)
    {
        var existing = _db.ChangeTracker.Entries<Job>()
            .FirstOrDefault(e => e.Entity.Id == job.Id);
        if (existing != null)
            existing.CurrentValues.SetValues(job);
        else
            _db.Jobs.Update(job);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Job.Updated", "Job", job.Id.ToString(), ct: ct);
        return job;
    }

    /// <summary>
    /// Deletes a job by its identifier. Throws <see cref="DependencyException"/>
    /// if any schedule still references this job.
    /// </summary>
    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var job = await _db.Jobs.FindAsync(new object[] { id }, ct)
            ?? throw new KeyNotFoundException($"Job {id} not found.");

        var referencingSchedule = await _db.Schedules.FirstOrDefaultAsync(s => s.JobId == id, ct);
        if (referencingSchedule != null)
            throw new DependencyException($"Job '{job.Name}' cannot be deleted — it is used by Schedule '{referencingSchedule.Name}'.");

        _db.Jobs.Remove(job);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Job.Deleted", "Job", id.ToString(), ct: ct);
    }

    /// <summary>
    /// UC-7: sends a synthetic test event through the job's REAL delivery path —
    /// templates, OID mapping and all assigned active targets — and reports the
    /// per-target outcome. Uses a unique negative EventId so notification-dedup
    /// never suppresses a repeated test; per-target rate limits still apply
    /// (deliberately, so a test cannot flood a production NMS).
    /// </summary>
    public async Task<string> SendTestEventAsync(int id, Models.Enums.Severity severity = Models.Enums.Severity.Information, CancellationToken ct = default)
    {
        var job = await GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Job {id} not found.");

        var context = new Models.DTOs.NotificationContext
        {
            // Unique negative id: cannot collide with a real event and defeats the
            // notification-dedup cache across repeated test clicks.
            EventId = -DateTime.UtcNow.Ticks,
            JobName = job.Name,
            Mailbox = job.Mailbox?.Name ?? string.Empty,
            From = "test@mail2snmp.local",
            Subject = $"Mail2SNMP test event for job '{job.Name}' ({severity})",
            // Operator-chosen: a fixed Information severity could never reach a
            // Critical-only target, so severity routing was untestable from the product.
            Severity = severity,
            RuleName = job.Rule?.Name ?? string.Empty,
            HitCount = 1,
            TimestampUtc = DateTime.UtcNow,
            TrapTemplate = job.TrapTemplate,
            WebhookTemplate = job.WebhookTemplate,
            OidMapping = job.OidMapping
        };

        var channels = _channels.ToList();
        var snmpChannel = channels.FirstOrDefault(c => c.ChannelName == INotificationChannel.Snmp);
        var webhookChannel = channels.FirstOrDefault(c => c.ChannelName == INotificationChannel.Webhook);

        var report = new StringBuilder();
        var okCount = 0;
        var failCount = 0;
        var skippedCount = 0;

        foreach (var jst in job.JobSnmpTargets.Where(t => t.SnmpTarget.IsActive))
        {
            // UC-4: surface severity routing instead of silently skipping, so the operator
            // sees why a target stays quiet — and can re-run at a higher severity to
            // confirm it then fires.
            if (context.Severity < jst.SnmpTarget.MinSeverity)
            {
                skippedCount++;
                report.AppendLine($"SNMP  {jst.SnmpTarget.Name}: skipped (target requires >= {jst.SnmpTarget.MinSeverity})");
                continue;
            }
            var ok = snmpChannel != null && await snmpChannel.SendToSnmpTargetAsync(context, jst.SnmpTarget, ct);
            report.AppendLine($"SNMP  {jst.SnmpTarget.Name} ({jst.SnmpTarget.Host}:{jst.SnmpTarget.Port}): {(ok ? "sent" : "FAILED")}");
            if (ok) okCount++; else failCount++;
        }

        foreach (var jwt in job.JobWebhookTargets.Where(t => t.WebhookTarget.IsActive))
        {
            if (context.Severity < jwt.WebhookTarget.MinSeverity)
            {
                skippedCount++;
                report.AppendLine($"HTTP  {jwt.WebhookTarget.Name}: skipped (target requires >= {jwt.WebhookTarget.MinSeverity})");
                continue;
            }
            var ok = webhookChannel != null && await webhookChannel.SendToWebhookTargetAsync(context, jwt.WebhookTarget, ct);
            report.AppendLine($"HTTP  {jwt.WebhookTarget.Name}: {(ok ? "delivered" : "FAILED")}");
            if (ok) okCount++; else failCount++;
        }

        // Verified fix: only claim "no targets" when there truly were none — a report
        // full of severity-skipped targets must not end with a contradictory line.
        if (okCount == 0 && failCount == 0 && skippedCount == 0)
            report.AppendLine("No active targets are assigned to this job — nothing was sent.");

        await _audit.LogAsync("Job.TestSend", "Job", id.ToString(), ct: ct);
        _logger.LogInformation("Test event sent through Job {JobId}: {Ok} ok, {Failed} failed", id, okCount, failCount);

        return $"Test event for '{job.Name}': {okCount} delivered, {failCount} failed.\n{report}".TrimEnd();
    }

    /// <summary>
    /// Updates the SNMP and Webhook target assignments for a job, replacing existing entries.
    /// The remove + insert sequence runs inside an explicit transaction so a partial failure
    /// (e.g. constraint violation on the second collection) cannot leave the job with a mix
    /// of old and new assignments.
    /// </summary>
    public async Task UpdateTargetAssignmentsAsync(int jobId, IEnumerable<int> snmpTargetIds, IEnumerable<int> webhookTargetIds, CancellationToken ct = default)
    {
        var job = await _db.Jobs
            .Include(j => j.JobSnmpTargets)
            .Include(j => j.JobWebhookTargets)
            .FirstOrDefaultAsync(j => j.Id == jobId, ct)
            ?? throw new KeyNotFoundException($"Job {jobId} not found.");

        var ownTransaction = _db.Database.CurrentTransaction == null;
        var transaction = ownTransaction
            ? await _db.Database.BeginTransactionAsync(ct)
            : null;
        try
        {
            // Replace SNMP target assignments
            _db.JobSnmpTargets.RemoveRange(job.JobSnmpTargets);
            foreach (var targetId in snmpTargetIds.Distinct())
                _db.JobSnmpTargets.Add(new JobSnmpTarget { JobId = jobId, SnmpTargetId = targetId });

            // Replace Webhook target assignments
            _db.JobWebhookTargets.RemoveRange(job.JobWebhookTargets);
            foreach (var targetId in webhookTargetIds.Distinct())
                _db.JobWebhookTargets.Add(new JobWebhookTarget { JobId = jobId, WebhookTargetId = targetId });

            await _db.SaveChangesAsync(ct);
            if (transaction != null) await transaction.CommitAsync(ct);
        }
        catch
        {
            if (transaction != null) await transaction.RollbackAsync(ct);
            throw;
        }
        finally
        {
            if (transaction != null) await transaction.DisposeAsync();
        }

        await _audit.LogAsync("Job.TargetsUpdated", "Job", jobId.ToString(), ct: ct);
    }

    /// <summary>
    /// Produces a human-readable dry-run report for a job, including configuration details,
    /// rule evaluation against sample data, and a summary of any issues found.
    /// </summary>
    public async Task<string> DryRunAsync(int id, CancellationToken ct = default)
    {
        var job = await GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Job {id} not found.");

        _logger.LogInformation("Dry run for job {Name}: Rule={Rule}, Mailbox={Mailbox}", job.Name, job.Rule?.Name, job.Mailbox?.Name);

        var sb = new StringBuilder();
        sb.AppendLine($"=== Dry Run Report for Job '{job.Name}' ===");
        sb.AppendLine();

        // Job configuration
        sb.AppendLine("[Job Configuration]");
        sb.AppendLine($"  Name:             {job.Name}");
        sb.AppendLine($"  Active:           {job.IsActive}");
        sb.AppendLine($"  Channels:         {job.Channels}");
        sb.AppendLine($"  MaxEventsPerHour: {job.MaxEventsPerHour}");
        sb.AppendLine($"  MaxActiveEvents:  {job.MaxActiveEvents}");
        sb.AppendLine($"  DedupWindow:      {job.DedupWindowMinutes} min");
        sb.AppendLine();

        // Mailbox info
        sb.AppendLine("[Mailbox]");
        if (job.Mailbox is not null)
        {
            sb.AppendLine($"  Name:   {job.Mailbox.Name}");
            sb.AppendLine($"  Host:   {job.Mailbox.Host}:{job.Mailbox.Port}");
            sb.AppendLine($"  Folder: {job.Mailbox.Folder}");
            sb.AppendLine($"  SSL:    {job.Mailbox.UseSsl}");
            sb.AppendLine($"  Active: {job.Mailbox.IsActive}");
            if (!job.Mailbox.IsActive)
                sb.AppendLine("  WARNING: Mailbox is INACTIVE - job will not process emails.");
        }
        else
        {
            sb.AppendLine("  ERROR: No mailbox assigned to this job!");
        }
        sb.AppendLine();

        // Rule info and evaluation
        sb.AppendLine("[Rule]");
        var rule = job.Rule;
        if (rule is not null)
        {
            sb.AppendLine($"  Name:      {rule.Name}");
            sb.AppendLine($"  Field:     {rule.Field}");
            sb.AppendLine($"  MatchType: {rule.MatchType}");
            sb.AppendLine($"  Criteria:  {rule.Criteria}");
            sb.AppendLine($"  Severity:  {rule.Severity}");
            sb.AppendLine($"  Priority:  {rule.Priority}");
            sb.AppendLine($"  Active:    {rule.IsActive}");
            if (!rule.IsActive)
                sb.AppendLine("  WARNING: Rule is INACTIVE - job will not match any emails.");
            sb.AppendLine();

            // Test rule with sample data
            sb.AppendLine("[Rule Evaluation Test]");
            var testSamples = new[]
            {
                new { From = "alert@example.com", Subject = "CRITICAL: Server down", Body = "Server srv01 is unreachable." },
                new { From = "noreply@monitoring.local", Subject = "WARNING: High CPU usage", Body = "CPU at 95% for host db02." },
                new { From = "test@mail2snmp.local", Subject = rule.Criteria, Body = rule.Criteria },
            };

            foreach (var sample in testSamples)
            {
                var matched = _ruleEvaluator.Evaluate(rule, sample.From, sample.Subject, sample.Body);
                sb.AppendLine($"  Sample (From='{sample.From}', Subject='{sample.Subject}')");
                sb.AppendLine($"    => {(matched ? "MATCH" : "NO MATCH")}");
            }
        }
        else
        {
            sb.AppendLine("  ERROR: No rule assigned to this job!");
        }
        sb.AppendLine();

        // Schedule info
        var schedules = await _db.Schedules
            .Where(s => s.JobId == job.Id)
            .AsNoTracking()
            .ToListAsync(ct);

        sb.AppendLine("[Schedules]");
        if (schedules.Count > 0)
        {
            foreach (var schedule in schedules)
            {
                sb.AppendLine($"  - {schedule.Name}: Interval={schedule.IntervalMinutes}min Active={schedule.IsActive}");
            }
        }
        else
        {
            sb.AppendLine("  WARNING: No schedules configured - job will never run automatically.");
        }
        sb.AppendLine();

        // Summary
        sb.AppendLine("[Summary]");
        var issues = new List<string>();
        if (job.Mailbox is null) issues.Add("No mailbox assigned");
        else if (!job.Mailbox.IsActive) issues.Add("Mailbox is inactive");
        if (rule is null) issues.Add("No rule assigned");
        else if (!rule.IsActive) issues.Add("Rule is inactive");
        if (!job.IsActive) issues.Add("Job is inactive");
        if (schedules.Count == 0) issues.Add("No schedules configured");
        else if (schedules.All(s => !s.IsActive)) issues.Add("All schedules are inactive");

        if (issues.Count == 0)
        {
            sb.AppendLine("  Status: READY - Job is fully configured and will process emails.");
        }
        else
        {
            sb.AppendLine($"  Status: ISSUES FOUND ({issues.Count})");
            foreach (var issue in issues)
                sb.AppendLine($"    - {issue}");
        }

        return sb.ToString();
    }
}
