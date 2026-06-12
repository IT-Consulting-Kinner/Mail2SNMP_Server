namespace Mail2SNMP.Models.Entities;

/// <summary>
/// Distributed worker lease for single-instance enforcement or cluster coordination.
/// </summary>
public class WorkerLease
{
    /// <summary>Surrogate primary key. Identity column assigned by the database.</summary>
    public int Id { get; set; }

    /// <summary>
    /// Unique identifier of the worker instance currently holding the lease. Defaults to a fresh GUID
    /// per process; used to detect which instance owns single-instance enforcement.
    /// </summary>
    public string InstanceId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>UTC time the lease was first acquired. Defaults to <see cref="DateTime.UtcNow"/> at construction.</summary>
    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// UTC time of the most recent heartbeat from the lease holder. Other instances treat the lease as
    /// expired (and may take over) once this is older than the configured heartbeat timeout.
    /// </summary>
    public DateTime LastHeartbeatUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Licensed edition reported by the lease holder (e.g. <c>"Community"</c>). Defaults to <c>"Community"</c>.</summary>
    public string LicenseEdition { get; set; } = "Community";

    /// <summary>Host name of the machine running the lease holder. Defaults to <see cref="Environment.MachineName"/>; aids diagnostics.</summary>
    public string MachineName { get; set; } = Environment.MachineName;
}
