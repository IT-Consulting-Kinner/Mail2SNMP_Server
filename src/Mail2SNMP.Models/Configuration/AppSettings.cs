namespace Mail2SNMP.Models.Configuration;

/// <summary>
/// Database provider and connection configuration.
/// Supports two modes:
///   1) Structured fields (Server, Port, DatabaseName, Username, Password, ...) — recommended.
///   2) Raw <see cref="ConnectionString"/> — used as-is when set (takes precedence).
/// </summary>
public class DatabaseSettings
{
    /// <summary>"Sqlite" or "SqlServer".</summary>
    public string Provider { get; set; } = "Sqlite";

    /// <summary>
    /// Optional raw ADO.NET connection string. When set, this value is used as-is
    /// and all structured fields below are ignored. Useful for advanced scenarios.
    /// </summary>
    public string? ConnectionString { get; set; }

    // ─── Structured fields (used when ConnectionString is not set) ────────────

    /// <summary>SQL Server hostname or listener name. SqlServer only.</summary>
    public string? Server { get; set; }

    /// <summary>SQL Server TCP port. Default 1433. SqlServer only.</summary>
    public int Port { get; set; } = 1433;

    /// <summary>Database name. For SQLite, this is the file path (e.g. "mail2snmp.db").</summary>
    public string DatabaseName { get; set; } = "mail2snmp.db";

    /// <summary>SQL login username. Ignored when <see cref="IntegratedSecurity"/> is true.</summary>
    public string? Username { get; set; }

    /// <summary>SQL login password. Ignored when <see cref="IntegratedSecurity"/> is true.</summary>
    public string? Password { get; set; }

    /// <summary>When true, uses Windows Authentication (Trusted_Connection). SqlServer only.</summary>
    public bool IntegratedSecurity { get; set; } = false;

    /// <summary>When true, trusts the SQL Server certificate without validation.</summary>
    public bool TrustServerCertificate { get; set; } = true;

    /// <summary>Enable encrypted connection. Default true.</summary>
    public bool Encrypt { get; set; } = true;

    /// <summary>Enable failover for AlwaysOn / multi-subnet listeners.</summary>
    public bool MultiSubnetFailover { get; set; } = false;

    /// <summary>
    /// Connection establishment timeout in seconds (maps to "Connect Timeout"). Default 30.
    /// </summary>
    public int ConnectTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Per-command execution timeout in seconds applied to the EF Core DbContext. Default 30.
    /// </summary>
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Additional raw connection string options appended verbatim
    /// (e.g. "ApplicationIntent=ReadOnly").
    /// </summary>
    public string? AdditionalOptions { get; set; }

    /// <summary>
    /// Returns the effective connection string. If <see cref="ConnectionString"/> is set,
    /// it is returned verbatim. Otherwise the structured fields are assembled.
    /// </summary>
    public string GetEffectiveConnectionString()
    {
        if (!string.IsNullOrWhiteSpace(ConnectionString))
            return ConnectionString;

        if (Provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            var server = string.IsNullOrWhiteSpace(Server) ? "localhost" : Server;
            var serverWithPort = Port == 1433 ? server : $"{server},{Port}";

            var parts = new List<string>
            {
                $"Server={serverWithPort}",
                $"Database={DatabaseName}",
                $"TrustServerCertificate={TrustServerCertificate}",
                $"Encrypt={Encrypt}"
            };
            if (IntegratedSecurity)
            {
                parts.Add("Integrated Security=True");
            }
            else
            {
                parts.Add($"User Id={Username}");
                parts.Add($"Password={Password}");
            }
            if (MultiSubnetFailover)
                parts.Add("MultiSubnetFailover=True");
            parts.Add($"Connect Timeout={ConnectTimeoutSeconds}");
            if (!string.IsNullOrWhiteSpace(AdditionalOptions))
                parts.Add(AdditionalOptions.TrimEnd(';'));

            return string.Join(";", parts) + ";";
        }

        // SQLite — DatabaseName is the file path
        return $"Data Source={DatabaseName}";
    }
}

/// <summary>
/// IMAP connection pool and consumer task configuration.
/// </summary>
public class ImapSettings
{
    public int MaxConcurrentConnections { get; set; } = 10;
    public int ChannelBoundedCapacity { get; set; } = 20;
    public int ConsumerTasks { get; set; } = 5;

    /// <summary>
    /// IMAP connect/authenticate timeout in seconds. Used by both
    /// MailPollingService and the Test-Connection action. Default 10.
    /// </summary>
    public int ConnectTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// IMAP per-operation timeout (search/fetch) in seconds. Default 60.
    /// </summary>
    public int OperationTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Initial backoff (seconds) before restarting a crashed mail-polling consumer
    /// in <c>MailPollingService.SuperviseConsumerAsync</c>. Default 2.
    /// </summary>
    public int ConsumerRestartBackoffSeconds { get; set; } = 2;

    /// <summary>
    /// Maximum backoff (seconds) between consumer restart attempts. The backoff
    /// doubles after each crash up to this cap. Default 30.
    /// </summary>
    public int ConsumerRestartMaxBackoffSeconds { get; set; } = 30;
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

    /// <summary>
    /// Wave D (4): if &gt; 0, the AutoAcknowledgeService scans every minute and
    /// auto-acknowledges (with the System actor) any New event older than this many minutes.
    /// This causes the EventConfirmed pair-trap to be sent automatically — useful for
    /// self-clearing alarms (disk back below threshold, service back up, etc.).
    /// 0 disables the feature globally.
    /// </summary>
    public int AutoAcknowledgeAfterMinutes { get; set; } = 0;
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
/// Simplified, admin-friendly logging configuration. Replaces the verbose Serilog
/// JSON section. The host configures Serilog programmatically from these fields.
/// </summary>
public class LoggingSettings
{
    /// <summary>Minimum log level: Verbose, Debug, Information, Warning, Error, Fatal.</summary>
    public string MinimumLevel { get; set; } = "Information";

    /// <summary>When true, logs are written to the console.</summary>
    public bool ConsoleEnabled { get; set; } = true;

    /// <summary>When true, logs are written to a rolling file.</summary>
    public bool FileEnabled { get; set; } = true;

    /// <summary>
    /// Log file path. Supports %ProgramData% and other Windows environment variables.
    /// The "-.log" suffix triggers daily rolling (e.g. "mail2snmp-worker-20260101.log").
    /// </summary>
    public string FilePath { get; set; } = "%ProgramData%/IT-Consulting Kinner/Mail2SNMP_Server/Logs/mail2snmp-.log";

    /// <summary>How many days of log files to keep before deletion. Default 30.</summary>
    public int RetainedFileCountLimit { get; set; } = 30;

    /// <summary>Maximum size per log file before rolling, in megabytes. Default 50.</summary>
    public int FileSizeLimitMB { get; set; } = 50;

    /// <summary>Rolling interval: Hour, Day, Month, Year, Infinite. Default Day.</summary>
    public string RollingInterval { get; set; } = "Day";
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
