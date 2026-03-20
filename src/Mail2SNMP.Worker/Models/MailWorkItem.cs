namespace Mail2SNMP.Worker.Models;

/// <summary>
/// Immutable work item passed through the bounded channel from Quartz jobs to the MailPollingService consumers.
/// </summary>
public record MailWorkItem(int JobId, int MailboxId, int ScheduleId);
