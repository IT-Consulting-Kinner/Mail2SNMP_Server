namespace Mail2SNMP.Models.Entities;

/// <summary>
/// Join entity linking a Job to a specific SNMP target.
/// Enables per-job target selection instead of broadcasting to all active targets.
/// </summary>
public class JobSnmpTarget
{
    /// <summary>FK to the linked <see cref="Job"/>. Half of the composite primary key for this many-to-many row.</summary>
    public int JobId { get; set; }

    /// <summary>Navigation to the linked <see cref="Job"/> identified by <see cref="JobId"/>.</summary>
    public Job Job { get; set; } = null!;

    /// <summary>FK to the linked <see cref="SnmpTarget"/>. The other half of the composite primary key.</summary>
    public int SnmpTargetId { get; set; }

    /// <summary>Navigation to the linked <see cref="SnmpTarget"/> identified by <see cref="SnmpTargetId"/>.</summary>
    public SnmpTarget SnmpTarget { get; set; } = null!;
}
