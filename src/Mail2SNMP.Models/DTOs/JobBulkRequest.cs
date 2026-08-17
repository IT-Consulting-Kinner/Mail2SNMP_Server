using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Mail2SNMP.Models.DTOs;

/// <summary>
/// Which bulk operation to apply to a set of jobs.
/// </summary>
/// <remarks>
/// Accepts and emits its name rather than an ordinal. The rest of the API serializes enums
/// numerically and switching that globally would break every existing client, so the
/// converter is applied to this new type only — where <c>{"action": "Delete"}</c> being
/// self-evident matters more than consistency with ordinals nobody has to read.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum JobBulkAction
{
    /// <summary>Set <c>IsActive = true</c> on each job.</summary>
    Activate,

    /// <summary>Set <c>IsActive = false</c> on each job.</summary>
    Deactivate,

    /// <summary>Delete each job. Jobs still referenced by a schedule are reported as failed.</summary>
    Delete
}

/// <summary>
/// Request body for <c>POST /api/v1/jobs/bulk</c>.
/// </summary>
/// <remarks>
/// Bulk activate/deactivate/delete existed only as buttons in the management UI, so the
/// REST surface — the thing automation actually talks to — could only emulate it with a
/// loop of read-modify-write PUTs, each of which can clobber a concurrent edit.
/// </remarks>
public sealed class JobBulkRequest
{
    /// <summary>The jobs to act on. Duplicates are ignored; an empty list is rejected.</summary>
    [Required]
    [MinLength(1, ErrorMessage = "At least one job id is required.")]
    public List<int> Ids { get; set; } = new();

    /// <summary>The operation to apply to every listed job.</summary>
    [Required]
    public JobBulkAction Action { get; set; }
}
