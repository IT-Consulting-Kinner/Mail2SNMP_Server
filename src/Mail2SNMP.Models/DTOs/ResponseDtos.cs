namespace Mail2SNMP.Models.DTOs;

/// <summary>
/// API response DTO for Mailbox - excludes EncryptedPassword.
/// </summary>
public class MailboxResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public bool UseSsl { get; set; }
    public string Username { get; set; } = string.Empty;
    public bool HasPassword { get; set; }
    public string Folder { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? LastCheckedUtc { get; set; }
    public string? LastError { get; set; }
}

/// <summary>
/// API response DTO for SnmpTarget - excludes encrypted passwords.
/// </summary>
public class SnmpTargetResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Version { get; set; } = string.Empty;
    public string? CommunityString { get; set; }
    public string? SecurityName { get; set; }
    public string AuthProtocol { get; set; } = string.Empty;
    public bool HasAuthPassword { get; set; }
    public string PrivProtocol { get; set; } = string.Empty;
    public bool HasPrivPassword { get; set; }
    public string? EngineId { get; set; }
    public string? EnterpriseTrapOid { get; set; }
    public int MaxTrapsPerMinute { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedUtc { get; set; }
}

/// <summary>
/// API response DTO for WebhookTarget - excludes EncryptedSecret.
/// </summary>
public class WebhookTargetResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Headers { get; set; }
    public string? PayloadTemplate { get; set; }
    public bool HasSecret { get; set; }
    public int MaxRequestsPerMinute { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedUtc { get; set; }
}

/// <summary>
/// API request DTO for creating or updating a Job — includes target assignment arrays.
/// </summary>
public class JobRequest
{
    public string Name { get; set; } = string.Empty;
    public int MailboxId { get; set; }
    public int RuleId { get; set; }
    public string? TrapTemplate { get; set; }
    public string? WebhookTemplate { get; set; }
    public string? OidMapping { get; set; }
    public int MaxEventsPerHour { get; set; } = 50;
    public int MaxActiveEvents { get; set; } = 200;
    public int DedupWindowMinutes { get; set; } = 30;
    public bool IsActive { get; set; } = true;

    /// <summary>SNMP target IDs to assign to this job.</summary>
    public int[] SnmpTargetIds { get; set; } = Array.Empty<int>();

    /// <summary>Webhook target IDs to assign to this job.</summary>
    public int[] WebhookTargetIds { get; set; } = Array.Empty<int>();
}

/// <summary>
/// API response DTO for Job — includes resolved target assignments.
/// </summary>
public class JobResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int MailboxId { get; set; }
    public string? MailboxName { get; set; }
    public int RuleId { get; set; }
    public string? RuleName { get; set; }
    public string Channels { get; set; } = "none";
    public string? TrapTemplate { get; set; }
    public string? WebhookTemplate { get; set; }
    public string? OidMapping { get; set; }
    public int MaxEventsPerHour { get; set; }
    public int MaxActiveEvents { get; set; }
    public int DedupWindowMinutes { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedUtc { get; set; }

    public JobTargetInfo[] SnmpTargets { get; set; } = Array.Empty<JobTargetInfo>();
    public JobTargetInfo[] WebhookTargets { get; set; } = Array.Empty<JobTargetInfo>();
}

/// <summary>
/// Minimal target info returned within a JobResponse.
/// </summary>
public class JobTargetInfo
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Extension methods for mapping entities to response DTOs.
/// </summary>
public static class ResponseDtoMapper
{
    public static MailboxResponse ToResponse(this Entities.Mailbox mailbox)
    {
        return new MailboxResponse
        {
            Id = mailbox.Id,
            Name = mailbox.Name,
            Host = mailbox.Host,
            Port = mailbox.Port,
            UseSsl = mailbox.UseSsl,
            Username = mailbox.Username,
            HasPassword = !string.IsNullOrEmpty(mailbox.EncryptedPassword),
            Folder = mailbox.Folder,
            IsActive = mailbox.IsActive,
            CreatedUtc = mailbox.CreatedUtc,
            LastCheckedUtc = mailbox.LastCheckedUtc,
            LastError = mailbox.LastError
        };
    }

    public static SnmpTargetResponse ToResponse(this Entities.SnmpTarget target)
    {
        return new SnmpTargetResponse
        {
            Id = target.Id,
            Name = target.Name,
            Host = target.Host,
            Port = target.Port,
            Version = target.Version.ToString(),
            CommunityString = target.CommunityString,
            SecurityName = target.SecurityName,
            AuthProtocol = target.AuthProtocol.ToString(),
            HasAuthPassword = !string.IsNullOrEmpty(target.EncryptedAuthPassword),
            PrivProtocol = target.PrivProtocol.ToString(),
            HasPrivPassword = !string.IsNullOrEmpty(target.EncryptedPrivPassword),
            EngineId = target.EngineId,
            EnterpriseTrapOid = target.EnterpriseTrapOid,
            MaxTrapsPerMinute = target.MaxTrapsPerMinute,
            IsActive = target.IsActive,
            CreatedUtc = target.CreatedUtc
        };
    }

    public static WebhookTargetResponse ToResponse(this Entities.WebhookTarget target)
    {
        return new WebhookTargetResponse
        {
            Id = target.Id,
            Name = target.Name,
            Url = target.Url,
            Headers = target.Headers,
            PayloadTemplate = target.PayloadTemplate,
            HasSecret = !string.IsNullOrEmpty(target.EncryptedSecret),
            MaxRequestsPerMinute = target.MaxRequestsPerMinute,
            IsActive = target.IsActive,
            CreatedUtc = target.CreatedUtc
        };
    }

    public static JobResponse ToResponse(this Entities.Job job)
    {
        return new JobResponse
        {
            Id = job.Id,
            Name = job.Name,
            MailboxId = job.MailboxId,
            MailboxName = job.Mailbox?.Name,
            RuleId = job.RuleId,
            RuleName = job.Rule?.Name,
            Channels = job.Channels,
            TrapTemplate = job.TrapTemplate,
            WebhookTemplate = job.WebhookTemplate,
            OidMapping = job.OidMapping,
            MaxEventsPerHour = job.MaxEventsPerHour,
            MaxActiveEvents = job.MaxActiveEvents,
            DedupWindowMinutes = job.DedupWindowMinutes,
            IsActive = job.IsActive,
            CreatedUtc = job.CreatedUtc,
            SnmpTargets = job.JobSnmpTargets
                .Select(jst => new JobTargetInfo { Id = jst.SnmpTargetId, Name = jst.SnmpTarget?.Name ?? $"#{jst.SnmpTargetId}" })
                .ToArray(),
            WebhookTargets = job.JobWebhookTargets
                .Select(jwt => new JobTargetInfo { Id = jwt.WebhookTargetId, Name = jwt.WebhookTarget?.Name ?? $"#{jwt.WebhookTargetId}" })
                .ToArray()
        };
    }

    public static Entities.Job ToEntity(this JobRequest request)
    {
        return new Entities.Job
        {
            Name = request.Name,
            MailboxId = request.MailboxId,
            RuleId = request.RuleId,
            TrapTemplate = request.TrapTemplate,
            WebhookTemplate = request.WebhookTemplate,
            OidMapping = request.OidMapping,
            MaxEventsPerHour = request.MaxEventsPerHour,
            MaxActiveEvents = request.MaxActiveEvents,
            DedupWindowMinutes = request.DedupWindowMinutes,
            IsActive = request.IsActive
        };
    }
}
