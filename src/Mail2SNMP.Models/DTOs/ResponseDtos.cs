namespace Mail2SNMP.Models.DTOs;

/// <summary>
/// API response DTO for Mailbox - excludes EncryptedPassword.
/// </summary>
public class MailboxResponse
{
    /// <summary>Database primary key of the mailbox. Serialized as <c>id</c>; assigned by the server, so 0 on records that have not yet been persisted.</summary>
    public int Id { get; set; }

    /// <summary>Human-friendly display name for the mailbox (1–200 characters). Used for identification in the UI and logs, not for IMAP connection.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>IMAP server hostname or IP address the poller connects to (e.g. <c>imap.gmail.com</c>).</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>IMAP TCP port (1–65535). Conventionally 993 for implicit TLS or 143 for plaintext/STARTTLS.</summary>
    public int Port { get; set; }

    /// <summary>When <see langword="true"/>, the connection uses implicit SSL/TLS (typically port 993).</summary>
    public bool UseSsl { get; set; }

    /// <summary>IMAP login username (often the full email address).</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Indicates whether an IMAP password is stored for this mailbox.
    /// </summary>
    /// <remarks>
    /// The password itself is encrypted at rest (AES-256-GCM) and is never returned through the API;
    /// this flag is the safe substitute so clients can tell a configured credential from a missing one.
    /// Maps from <see cref="Entities.Mailbox.EncryptedPassword"/> being non-empty.
    /// </remarks>
    public bool HasPassword { get; set; }

    /// <summary>IMAP folder/mailbox name to poll. Defaults to <c>INBOX</c> on the entity.</summary>
    public string Folder { get; set; } = string.Empty;

    /// <summary>When <see langword="true"/>, the mailbox is enabled and will be polled; when <see langword="false"/> it is retained but skipped.</summary>
    public bool IsActive { get; set; }

    /// <summary>UTC timestamp recording when the mailbox was created. Serialized in ISO-8601 round-trip form.</summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>UTC timestamp of the most recent poll attempt, or <see langword="null"/> if the mailbox has never been polled.</summary>
    public DateTime? LastCheckedUtc { get; set; }

    /// <summary>Message from the most recent failed poll, or <see langword="null"/> when the last poll succeeded (or none has run yet). Used to surface connectivity/auth problems in the UI.</summary>
    public string? LastError { get; set; }
}

/// <summary>
/// API response DTO for SnmpTarget - excludes encrypted passwords.
/// </summary>
public class SnmpTargetResponse
{
    /// <summary>Database primary key of the SNMP target. Serialized as <c>id</c>; assigned by the server.</summary>
    public int Id { get; set; }

    /// <summary>Human-friendly display name for the target (1–200 characters), used in the UI and logs.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Hostname or IP address of the SNMP trap receiver (manager/NMS).</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>UDP port the trap is sent to (1–65535). Conventionally 162 for SNMP traps.</summary>
    public int Port { get; set; }

    /// <summary>
    /// SNMP protocol version as a string. One of <c>V1</c>, <c>V2c</c>, or <c>V3</c>, produced by
    /// <c>ToString()</c> on the <see cref="Enums.SnmpVersion"/> enum. Determines which credential
    /// fields below are meaningful (community string for v1/v2c; security name + auth/priv for v3).
    /// </summary>
    public string Version { get; set; } = string.Empty;
    /// <summary>
    /// R2: indicates whether a v1/v2c community string is configured. The
    /// actual value is encrypted at rest and never returned through the API
    /// — same policy as <see cref="HasAuthPassword"/> and <see cref="HasPrivPassword"/>.
    /// </summary>
    public bool HasCommunityString { get; set; }

    /// <summary>SNMPv3 USM security (user) name, or <see langword="null"/> for v1/v2c targets where it is not used.</summary>
    public string? SecurityName { get; set; }

    /// <summary>
    /// SNMPv3 authentication protocol as a string (e.g. <c>None</c>, <c>MD5</c>, <c>SHA</c>), from
    /// <c>ToString()</c> on the <see cref="Enums.AuthProtocol"/> enum. <c>None</c> indicates no
    /// authentication (noAuthNoPriv); only relevant when <see cref="Version"/> is <c>V3</c>.
    /// </summary>
    public string AuthProtocol { get; set; } = string.Empty;

    /// <summary>
    /// Indicates whether an SNMPv3 authentication password is stored. The secret is encrypted at rest
    /// and never returned through the API; this flag is the safe substitute. See <see cref="HasCommunityString"/>.
    /// </summary>
    public bool HasAuthPassword { get; set; }

    /// <summary>
    /// SNMPv3 privacy (encryption) protocol as a string (e.g. <c>None</c>, <c>DES</c>, <c>AES</c>), from
    /// <c>ToString()</c> on the <see cref="Enums.PrivProtocol"/> enum. <c>None</c> indicates no encryption;
    /// only relevant when <see cref="Version"/> is <c>V3</c>.
    /// </summary>
    public string PrivProtocol { get; set; } = string.Empty;

    /// <summary>
    /// Indicates whether an SNMPv3 privacy (encryption) password is stored. The secret is encrypted at rest
    /// and never returned through the API; this flag is the safe substitute. See <see cref="HasCommunityString"/>.
    /// </summary>
    public bool HasPrivPassword { get; set; }

    /// <summary>SNMPv3 engine ID (hex string) used for authoritative engine discovery, or <see langword="null"/> if not configured.</summary>
    public string? EngineId { get; set; }

    /// <summary>
    /// Enterprise-specific trap OID in dotted-decimal form (e.g. <c>1.3.6.1.4.1.61376.1.2.0.1</c>) sent as the
    /// snmpTrapOID varbind, or <see langword="null"/> to use the built-in default. Empty/blank means unset.
    /// </summary>
    public string? EnterpriseTrapOid { get; set; }

    /// <summary>Outbound rate cap in traps per minute (1–10,000). Notifications beyond this rate are throttled. Defaults to 100 on the entity.</summary>
    public int MaxTrapsPerMinute { get; set; }

    /// <summary>When <see langword="true"/>, the worker periodically sends KeepAlive traps to this target to signal liveness.</summary>
    public bool SendKeepAlive { get; set; }

    /// <summary>When <see langword="true"/>, the target is enabled and will receive traps; when <see langword="false"/> it is retained but skipped.</summary>
    public bool IsActive { get; set; }

    /// <summary>UTC timestamp recording when the target was created. Serialized in ISO-8601 round-trip form.</summary>
    public DateTime CreatedUtc { get; set; }
}

/// <summary>
/// API response DTO for WebhookTarget - excludes EncryptedSecret.
/// </summary>
public class WebhookTargetResponse
{
    /// <summary>Database primary key of the webhook target. Serialized as <c>id</c>; assigned by the server.</summary>
    public int Id { get; set; }

    /// <summary>Human-friendly display name for the target (1–200 characters), used in the UI and logs.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Absolute HTTP/HTTPS endpoint that notification payloads are POSTed to (up to 2000 characters).</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Optional extra HTTP request headers, stored as a JSON object string (name/value pairs), or
    /// <see langword="null"/> if none. Applied to every outbound request in addition to the default headers.
    /// </summary>
    public string? Headers { get; set; }

    /// <summary>
    /// Optional body template used to render the POST payload, or <see langword="null"/> to use the default payload.
    /// Supports the same <c>{{Placeholder}}</c> tokens as <see cref="NotificationContext"/>. A per-job
    /// <see cref="JobResponse.WebhookTemplate"/> overrides this when present.
    /// </summary>
    public string? PayloadTemplate { get; set; }

    /// <summary>
    /// Indicates whether an HMAC signing secret is configured. When set, outbound requests are signed
    /// (HMAC-SHA256). The secret is encrypted at rest and never returned through the API; this flag is the safe substitute.
    /// </summary>
    public bool HasSecret { get; set; }

    /// <summary>Outbound rate cap in requests per minute (1–10,000). Notifications beyond this rate are throttled. Defaults to 60 on the entity.</summary>
    public int MaxRequestsPerMinute { get; set; }

    /// <summary>When <see langword="true"/>, the target is enabled and will receive webhook calls; when <see langword="false"/> it is retained but skipped.</summary>
    public bool IsActive { get; set; }

    /// <summary>UTC timestamp recording when the target was created. Serialized in ISO-8601 round-trip form.</summary>
    public DateTime CreatedUtc { get; set; }
}

/// <summary>
/// API request DTO for creating or updating a Job — includes target assignment arrays.
/// </summary>
public class JobRequest
{
    /// <summary>Display name for the job. Required.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Id of the <see cref="Entities.Mailbox"/> this job polls. Must reference an existing mailbox.</summary>
    public int MailboxId { get; set; }

    /// <summary>Id of the <see cref="Entities.Rule"/> applied to matched emails. Must reference an existing rule.</summary>
    public int RuleId { get; set; }

    /// <summary>
    /// Optional per-job override of the SNMP trap body template, or <see langword="null"/> to use the target's
    /// own template. Supports the <c>{{Placeholder}}</c> tokens listed on <see cref="NotificationContext"/>.
    /// </summary>
    public string? TrapTemplate { get; set; }

    /// <summary>
    /// Optional per-job override of the webhook payload template, or <see langword="null"/> to use the target's
    /// own <see cref="WebhookTargetResponse.PayloadTemplate"/>. Supports the same placeholder tokens.
    /// </summary>
    public string? WebhookTemplate { get; set; }

    /// <summary>
    /// Optional JSON mapping of SNMP OIDs to template placeholder values for building trap varbinds, or
    /// <see langword="null"/> to use defaults. See <see cref="NotificationContext.OidMapping"/>.
    /// </summary>
    public string? OidMapping { get; set; }

    /// <summary>Maximum number of events this job may emit per hour (1–10,000). Excess matches are throttled. Defaults to 50.</summary>
    public int MaxEventsPerHour { get; set; } = 50;

    /// <summary>Maximum number of concurrently open (unresolved) events allowed for this job (1–100,000). Defaults to 200.</summary>
    public int MaxActiveEvents { get; set; } = 200;

    /// <summary>
    /// Deduplication window in minutes (0–1440). A repeat match with the same subject within this window
    /// increments an existing event's hit count instead of creating a new one. 0 disables job-level dedup. Defaults to 30.
    /// </summary>
    public int DedupWindowMinutes { get; set; } = 30;

    /// <summary>When <see langword="true"/> (default), the job is enabled for polling; when <see langword="false"/> it is saved but inactive.</summary>
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
    /// <summary>Database primary key of the job. Serialized as <c>id</c>; assigned by the server.</summary>
    public int Id { get; set; }

    /// <summary>Display name of the job.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Id of the <see cref="Entities.Mailbox"/> this job polls.</summary>
    public int MailboxId { get; set; }

    /// <summary>Display name of the linked mailbox, resolved for convenience, or <see langword="null"/> if the mailbox could not be loaded.</summary>
    public string? MailboxName { get; set; }

    /// <summary>Id of the <see cref="Entities.Rule"/> applied to matched emails.</summary>
    public int RuleId { get; set; }

    /// <summary>Display name of the linked rule, resolved for convenience, or <see langword="null"/> if the rule could not be loaded.</summary>
    public string? RuleName { get; set; }

    /// <summary>
    /// Comma-separated list of active notification channels derived from the assigned targets:
    /// contains <c>snmp</c> and/or <c>webhook</c>, or the literal <c>none</c> when no targets are assigned.
    /// This is a computed convenience field, not independently settable.
    /// </summary>
    public string Channels { get; set; } = "none";

    /// <summary>Per-job SNMP trap template override, or <see langword="null"/> if the target's template is used. See <see cref="JobRequest.TrapTemplate"/>.</summary>
    public string? TrapTemplate { get; set; }

    /// <summary>Per-job webhook payload template override, or <see langword="null"/> if the target's template is used. See <see cref="JobRequest.WebhookTemplate"/>.</summary>
    public string? WebhookTemplate { get; set; }

    /// <summary>Per-job OID-to-value JSON mapping for trap varbinds, or <see langword="null"/> if defaults are used. See <see cref="JobRequest.OidMapping"/>.</summary>
    public string? OidMapping { get; set; }

    /// <summary>Maximum events the job may emit per hour (1–10,000). See <see cref="JobRequest.MaxEventsPerHour"/>.</summary>
    public int MaxEventsPerHour { get; set; }

    /// <summary>Maximum concurrently open events allowed for the job (1–100,000). See <see cref="JobRequest.MaxActiveEvents"/>.</summary>
    public int MaxActiveEvents { get; set; }

    /// <summary>Deduplication window in minutes (0–1440); 0 disables job-level dedup. See <see cref="JobRequest.DedupWindowMinutes"/>.</summary>
    public int DedupWindowMinutes { get; set; }

    /// <summary>When <see langword="true"/>, the job is enabled for polling.</summary>
    public bool IsActive { get; set; }

    /// <summary>UTC timestamp recording when the job was created. Serialized in ISO-8601 round-trip form.</summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>Resolved SNMP targets assigned to this job (id + name). Empty array when none are assigned.</summary>
    public JobTargetInfo[] SnmpTargets { get; set; } = Array.Empty<JobTargetInfo>();

    /// <summary>Resolved webhook targets assigned to this job (id + name). Empty array when none are assigned.</summary>
    public JobTargetInfo[] WebhookTargets { get; set; } = Array.Empty<JobTargetInfo>();
}

/// <summary>
/// Minimal target info returned within a JobResponse.
/// </summary>
public class JobTargetInfo
{
    /// <summary>Id of the referenced SNMP or webhook target.</summary>
    public int Id { get; set; }

    /// <summary>Display name of the referenced target. Falls back to <c>#{Id}</c> when the target row could not be resolved.</summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Extension methods for mapping entities to response DTOs.
/// </summary>
public static class ResponseDtoMapper
{
    /// <summary>
    /// Projects a <see cref="Entities.Mailbox"/> entity onto a <see cref="MailboxResponse"/>, omitting the
    /// encrypted password and exposing only whether one is set via <see cref="MailboxResponse.HasPassword"/>.
    /// </summary>
    /// <param name="mailbox">The source mailbox entity.</param>
    /// <returns>A response DTO safe to serialize to API clients.</returns>
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

    /// <summary>
    /// Projects a <see cref="Entities.SnmpTarget"/> entity onto a <see cref="SnmpTargetResponse"/>, omitting the
    /// encrypted community string and v3 auth/priv passwords and exposing only their <c>Has*</c> presence flags.
    /// Enum-typed fields are emitted as their string names.
    /// </summary>
    /// <param name="target">The source SNMP target entity.</param>
    /// <returns>A response DTO safe to serialize to API clients.</returns>
    public static SnmpTargetResponse ToResponse(this Entities.SnmpTarget target)
    {
        return new SnmpTargetResponse
        {
            Id = target.Id,
            Name = target.Name,
            Host = target.Host,
            Port = target.Port,
            Version = target.Version.ToString(),
            HasCommunityString = !string.IsNullOrEmpty(target.EncryptedCommunityString),
            SecurityName = target.SecurityName,
            AuthProtocol = target.AuthProtocol.ToString(),
            HasAuthPassword = !string.IsNullOrEmpty(target.EncryptedAuthPassword),
            PrivProtocol = target.PrivProtocol.ToString(),
            HasPrivPassword = !string.IsNullOrEmpty(target.EncryptedPrivPassword),
            EngineId = target.EngineId,
            EnterpriseTrapOid = target.EnterpriseTrapOid,
            MaxTrapsPerMinute = target.MaxTrapsPerMinute,
            SendKeepAlive = target.SendKeepAlive,
            IsActive = target.IsActive,
            CreatedUtc = target.CreatedUtc
        };
    }

    /// <summary>
    /// Projects a <see cref="Entities.WebhookTarget"/> entity onto a <see cref="WebhookTargetResponse"/>, omitting the
    /// encrypted signing secret and exposing only whether one is set via <see cref="WebhookTargetResponse.HasSecret"/>.
    /// </summary>
    /// <param name="target">The source webhook target entity.</param>
    /// <returns>A response DTO safe to serialize to API clients.</returns>
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

    /// <summary>
    /// Projects a <see cref="Entities.Job"/> entity onto a <see cref="JobResponse"/>, resolving the linked mailbox
    /// and rule names and flattening the SNMP/webhook join-table assignments into <see cref="JobTargetInfo"/> arrays.
    /// </summary>
    /// <param name="job">The source job entity. Navigation properties (Mailbox, Rule, target joins) should be loaded for full population.</param>
    /// <returns>A response DTO with resolved target assignments.</returns>
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

    /// <summary>
    /// Materializes a new <see cref="Entities.Job"/> from an incoming <see cref="JobRequest"/>, copying scalar fields only.
    /// </summary>
    /// <param name="request">The deserialized create/update request.</param>
    /// <returns>A new job entity. Target assignments (<see cref="JobRequest.SnmpTargetIds"/> / <see cref="JobRequest.WebhookTargetIds"/>) are not applied here and must be handled by the caller.</returns>
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
