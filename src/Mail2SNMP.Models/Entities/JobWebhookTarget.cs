namespace Mail2SNMP.Models.Entities;

/// <summary>
/// Join entity linking a Job to a specific Webhook target.
/// Enables per-job target selection instead of broadcasting to all active targets.
/// </summary>
public class JobWebhookTarget
{
    /// <summary>FK to the linked <see cref="Job"/>. Half of the composite primary key for this many-to-many row.</summary>
    public int JobId { get; set; }

    /// <summary>Navigation to the linked <see cref="Job"/> identified by <see cref="JobId"/>.</summary>
    public Job Job { get; set; } = null!;

    /// <summary>FK to the linked <see cref="WebhookTarget"/>. The other half of the composite primary key.</summary>
    public int WebhookTargetId { get; set; }

    /// <summary>Navigation to the linked <see cref="WebhookTarget"/> identified by <see cref="WebhookTargetId"/>.</summary>
    public WebhookTarget WebhookTarget { get; set; } = null!;
}
