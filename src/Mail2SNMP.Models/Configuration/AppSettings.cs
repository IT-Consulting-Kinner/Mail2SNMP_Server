namespace Mail2SNMP.Models.Configuration;

/// <summary>
/// Database provider and connection string configuration.
/// </summary>
public class DatabaseSettings
{
    public string Provider { get; set; } = "Sqlite";
    public string ConnectionString { get; set; } = "Data Source=mail2snmp.db";
}

/// <summary>
/// IMAP connection pool and consumer task configuration.
/// </summary>
public class ImapSettings
{
    public int MaxConcurrentConnections { get; set; } = 10;
    public int ChannelBoundedCapacity { get; set; } = 20;
    public int ConsumerTasks { get; set; } = 5;
}

/// <summary>
/// Event lifecycle and deduplication configuration.
/// </summary>
public class EventSettings
{
    public int DefaultDedupWindowMinutes { get; set; } = 30;
    public int DefaultMaxEventsPerHour { get; set; } = 50;
    public int DefaultMaxActiveEvents { get; set; } = 200;
    public int AutoExpireDays { get; set; } = 30;
    public int ResolvedRetentionDays { get; set; } = 90;
}

/// <summary>
/// Data retention periods for automatic cleanup.
/// </summary>
public class RetentionSettings
{
    public int ProcessedMailDays { get; set; } = 30;
    public int AuditEventDays { get; set; } = 365;
    public int DeadLetterDays { get; set; } = 7;
    public int MaxAuditEntries { get; set; } = 5_000_000;
}

/// <summary>
/// Session expiry configuration.
/// </summary>
public class SessionSettings
{
    public int SlidingExpiryMinutes { get; set; } = 60;
    public int AbsoluteExpiryHours { get; set; } = 8;
}

/// <summary>
/// OpenID Connect configuration for Enterprise SSO.
/// </summary>
public class OidcSettings
{
    public string? Authority { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>
    /// The primary OIDC claim type that carries role information (e.g. "roles" for Azure AD).
    /// </summary>
    public string RoleClaimType { get; set; } = "roles";

    /// <summary>
    /// Additional claim types that may carry role information (e.g. "role" for Keycloak).
    /// Combined with <see cref="RoleClaimType"/> when mapping external claims to local roles.
    /// </summary>
    public string[] AdditionalRoleClaimTypes { get; set; } = new[] { "role" };

    public string AdminClaimValue { get; set; } = "Mail2SNMP.Admin";
    public string OperatorClaimValue { get; set; } = "Mail2SNMP.Operator";

    /// <summary>
    /// Claim types to preserve in the authentication cookie after stripping unnecessary claims.
    /// Only used for cookie size mitigation. Does NOT affect role mapping.
    /// </summary>
    public string[] RetainedClaimTypes { get; set; } = new[] { "name", "email" };
}

/// <summary>
/// Prometheus metrics endpoint configuration.
/// </summary>
public class MetricsSettings
{
    public bool Enabled { get; set; }
}

/// <summary>
/// Hosting mode configuration. When AllInOne is true, the Web host also runs
/// the background Worker services (Quartz scheduler, mail polling, dead-letter retry,
/// data retention) and maps the REST API endpoints — eliminating the need
/// for separate Worker and API processes.
/// </summary>
public class HostingSettings
{
    public bool AllInOne { get; set; }
}

/// <summary>
/// SNMP KeepAlive notification configuration.
/// Sends an empty keep-alive trap to all SNMP targets that have SendKeepAlive enabled.
/// </summary>
public class KeepAliveSettings
{
    /// <summary>
    /// When true, the worker periodically sends KeepAlive traps.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Interval between KeepAlive traps in minutes (default: 5).
    /// </summary>
    public int IntervalMinutes { get; set; } = 5;
}

/// <summary>
/// Update check configuration. The worker periodically polls a JSON feed
/// to detect newer versions and can emit an SNMP Update notification.
/// </summary>
public class UpdateCheckSettings
{
    /// <summary>
    /// When true, the update check runs (immediately at start, then every IntervalHours).
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Interval in hours between update checks (default: 24).
    /// </summary>
    public int IntervalHours { get; set; } = 24;

    /// <summary>
    /// URL of the JSON feed containing version, publish_date, and download_link fields.
    /// </summary>
    public string Url { get; set; } = "https://mail2snmp.adsumus.biz/downloads/Mail2SNMP.json";

    /// <summary>
    /// Trap notification mode:
    /// - Off: never send a trap (UI hint only)
    /// - Once: send a trap only on first detection of a given version
    /// - UntilUpdated: send a trap on every check until the installed version catches up
    /// </summary>
    public string TrapMode { get; set; } = "UntilUpdated";
}
