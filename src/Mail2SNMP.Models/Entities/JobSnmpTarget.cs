namespace Mail2SNMP.Models.Entities;

/// <summary>
/// Join entity linking a Job to a specific SNMP target.
/// Enables per-job target selection instead of broadcasting to all active targets.
/// </summary>
public class JobSnmpTarget
{
    public int JobId { get; set; }
    public Job Job { get; set; } = null!;

    public int SnmpTargetId { get; set; }
    public SnmpTarget SnmpTarget { get; set; } = null!;
}
