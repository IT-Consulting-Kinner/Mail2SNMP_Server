namespace Mail2SNMP.Models.Entities;

/// <summary>
/// Join entity linking a Job to a specific Webhook target.
/// Enables per-job target selection instead of broadcasting to all active targets.
/// </summary>
public class JobWebhookTarget
{
    public int JobId { get; set; }
    public Job Job { get; set; } = null!;

    public int WebhookTargetId { get; set; }
    public WebhookTarget WebhookTarget { get; set; } = null!;
}
