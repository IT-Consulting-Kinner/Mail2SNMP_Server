using Mail2SNMP.Infrastructure.Services;
using Mail2SNMP.Models.Configuration;
using Mail2SNMP.Worker.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
using System.Threading.Channels;

namespace Mail2SNMP.Worker.Jobs;

/// <summary>
/// Quartz job that enqueues IMAP polling work items into the bounded channel.
/// TryWrite result is always checked — on failure, a warning is logged and the
/// <c>mail2snmp_channel_overflow_total</c> counter is incremented (v5.8).
/// Marked with <see cref="DisallowConcurrentExecutionAttribute"/> to prevent overlapping polls for the same schedule.
/// </summary>
[DisallowConcurrentExecution]
public class ImapPollingJob : IJob
{
    private readonly Channel<MailWorkItem> _channel;
    private readonly ILogger<ImapPollingJob> _logger;
    private readonly int _channelCapacity;

    public ImapPollingJob(
        Channel<MailWorkItem> channel,
        ILogger<ImapPollingJob> logger,
        IOptions<ImapSettings> imapSettings)
    {
        _channel = channel;
        _logger = logger;
        _channelCapacity = imapSettings.Value.ChannelBoundedCapacity;
    }

    /// <summary>
    /// Extracts job, mailbox, and schedule identifiers from the Quartz data map and writes a
    /// <see cref="MailWorkItem"/> into the bounded channel. When the channel is at capacity
    /// the item is skipped (misfire) and not requeued. Both the pre-check and TryWrite result
    /// are logged and metricked so that no overflow goes unnoticed.
    /// </summary>
    public Task Execute(IJobExecutionContext context)
    {
        var dataMap = context.MergedJobDataMap;
        var jobId = dataMap.GetInt("JobId");
        var mailboxId = dataMap.GetInt("MailboxId");
        var scheduleId = dataMap.GetInt("ScheduleId");

        // Pre-check capacity for early misfire detection (Reader.Count is O(1) on BoundedChannel)
        if (_channel.Reader.Count >= _channelCapacity)
        {
            _logger.LogWarning(
                "Channel at capacity ({Count}/{Capacity}). Skipping poll for Job {JobId}, Schedule {ScheduleId} (misfire).",
                _channel.Reader.Count, _channelCapacity, jobId, scheduleId);
            Mail2SnmpMetrics.ChannelOverflow.Inc();
            return Task.CompletedTask;
        }

        var workItem = new MailWorkItem(jobId, mailboxId, scheduleId);
        var written = _channel.Writer.TryWrite(workItem);

        if (!written)
        {
            // Race condition: channel filled between pre-check and TryWrite — log and count as overflow
            _logger.LogWarning(
                "Channel overflow: TryWrite failed for Job {JobId}, Schedule {ScheduleId}. " +
                "Channel filled between capacity check and write attempt (misfire).",
                jobId, scheduleId);
            Mail2SnmpMetrics.ChannelOverflow.Inc();
            return Task.CompletedTask;
        }

        _logger.LogDebug(
            "Enqueued work item for Job {JobId}, Mailbox {MailboxId}, Schedule {ScheduleId}",
            jobId, mailboxId, scheduleId);

        return Task.CompletedTask;
    }
}
