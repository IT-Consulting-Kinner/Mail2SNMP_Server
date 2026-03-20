namespace Mail2SNMP.Models.Entities;

/// <summary>
/// Distributed worker lease for single-instance enforcement or cluster coordination.
/// </summary>
public class WorkerLease
{
    public int Id { get; set; }
    public string InstanceId { get; set; } = Guid.NewGuid().ToString();
    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastHeartbeatUtc { get; set; } = DateTime.UtcNow;
    public string LicenseEdition { get; set; } = "Community";
    public string MachineName { get; set; } = Environment.MachineName;
}
