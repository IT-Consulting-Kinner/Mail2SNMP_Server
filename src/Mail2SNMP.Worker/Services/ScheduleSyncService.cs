using Mail2SNMP.Core.Interfaces;
using Mail2SNMP.Worker.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Mail2SNMP.Worker.Services;

/// <summary>
/// Periodically reads active schedules from the database and syncs them into the
/// Quartz scheduler. Ensures new schedules are created, updated schedules are
/// rescheduled, and deleted/inactive schedules are removed from the scheduler.
/// </summary>
public class ScheduleSyncService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISchedulerFactory _schedulerFactory;
    private readonly ILogger<ScheduleSyncService> _logger;
    private static readonly TimeSpan SyncInterval = TimeSpan.FromSeconds(30);

    // Track last known schedule states to detect changes
    private readonly Dictionary<int, (int IntervalMinutes, bool IsActive)> _lastKnown = new();

    public ScheduleSyncService(
        IServiceScopeFactory scopeFactory,
        ISchedulerFactory schedulerFactory,
        ILogger<ScheduleSyncService> logger)
    {
        _scopeFactory = scopeFactory;
        _schedulerFactory = schedulerFactory;
        _logger = logger;
    }

    /// <summary>
    /// Waits briefly for Quartz to initialize, then enters a loop that syncs schedules at a fixed interval.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ScheduleSyncService starting (sync interval: {Interval}s)", SyncInterval.TotalSeconds);

        // Wait for Quartz scheduler to be ready
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncSchedulesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during schedule sync");
            }

            await Task.Delay(SyncInterval, stoppingToken);
        }

        _logger.LogInformation("ScheduleSyncService stopped");
    }

    /// <summary>
    /// Reads all schedules from the database, creates or reschedules Quartz triggers for active ones,
    /// removes inactive or deleted schedules from the Quartz scheduler, and tracks known state for change detection.
    /// </summary>
    private async Task SyncSchedulesAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var scheduleService = scope.ServiceProvider.GetRequiredService<IScheduleService>();
        var scheduler = await _schedulerFactory.GetScheduler(ct);

        var allSchedules = await scheduleService.GetAllAsync(ct);
        var currentIds = new HashSet<int>();

        foreach (var schedule in allSchedules)
        {
            currentIds.Add(schedule.Id);
            var jobKey = new JobKey($"imap-poll-{schedule.Id}", "mail-polling");
            var triggerKey = new TriggerKey($"trigger-{schedule.Id}", "mail-polling");

            if (!schedule.IsActive || schedule.Job == null || !schedule.Job.IsActive)
            {
                // Remove inactive schedule from scheduler
                if (await scheduler.CheckExists(jobKey, ct))
                {
                    await scheduler.DeleteJob(jobKey, ct);
                    _logger.LogInformation("Removed inactive schedule {ScheduleId} ({Name}) from scheduler",
                        schedule.Id, schedule.Name);
                }
                _lastKnown[schedule.Id] = (schedule.IntervalMinutes, false);
                continue;
            }

            var needsUpdate = false;
            if (_lastKnown.TryGetValue(schedule.Id, out var known))
            {
                needsUpdate = known.IntervalMinutes != schedule.IntervalMinutes || !known.IsActive;
            }
            else
            {
                // New schedule
                needsUpdate = true;
            }

            if (!needsUpdate && await scheduler.CheckExists(jobKey, ct))
            {
                continue; // Already scheduled, no changes
            }

            // Remove old job if exists (reschedule)
            if (await scheduler.CheckExists(jobKey, ct))
                await scheduler.DeleteJob(jobKey, ct);

            var job = JobBuilder.Create<ImapPollingJob>()
                .WithIdentity(jobKey)
                .UsingJobData("JobId", schedule.JobId)
                .UsingJobData("MailboxId", schedule.Job.MailboxId)
                .UsingJobData("ScheduleId", schedule.Id)
                .Build();

            var trigger = TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .StartNow()
                .WithSimpleSchedule(x => x
                    .WithIntervalInMinutes(schedule.IntervalMinutes)
                    .RepeatForever()
                    .WithMisfireHandlingInstructionNextWithRemainingCount())
                .Build();

            await scheduler.ScheduleJob(job, trigger, ct);
            _lastKnown[schedule.Id] = (schedule.IntervalMinutes, true);

            _logger.LogInformation(
                "Scheduled {ScheduleName} (Id={ScheduleId}) for Job {JobId} every {Interval} minutes",
                schedule.Name, schedule.Id, schedule.JobId, schedule.IntervalMinutes);
        }

        // Remove schedules that were deleted from the DB
        var deletedIds = _lastKnown.Keys.Except(currentIds).ToList();
        foreach (var deletedId in deletedIds)
        {
            var jobKey = new JobKey($"imap-poll-{deletedId}", "mail-polling");
            if (await scheduler.CheckExists(jobKey, ct))
            {
                await scheduler.DeleteJob(jobKey, ct);
                _logger.LogInformation("Removed deleted schedule {ScheduleId} from scheduler", deletedId);
            }
            _lastKnown.Remove(deletedId);
        }
    }
}
