# Configuration Reference

All configuration is stored in `appsettings.json`. The Worker, API, and Web projects each have their own configuration file.

## Database

```json
{
  "Database": {
    "Provider": "Sqlite",
    "ConnectionString": "Data Source=mail2snmp.db"
  }
}
```

| Key | Values | Description |
|-----|--------|-------------|
| `Provider` | `Sqlite`, `SqlServer` | Database engine |
| `ConnectionString` | -- | Standard ADO.NET connection string |

For SQL Server with multi-instance support:

```json
{
  "Database": {
    "Provider": "SqlServer",
    "ConnectionString": "Server=myserver;Database=Mail2SNMP;Trusted_Connection=True;TrustServerCertificate=True"
  }
}
```

## Logging

The structured `Logging` section is bound to the `LoggingSettings` class and applied at startup by `SerilogConfigurator`. The minimum level can be changed at runtime by editing `appsettings.json` and restarting the host (no rebuild required).

```json
{
  "Logging": {
    "MinimumLevel": "Information",
    "ConsoleEnabled": true,
    "FileEnabled": true,
    "FilePath": "logs/mail2snmp-.log",
    "RetainedFileCountLimit": 30,
    "FileSizeLimitBytes": 52428800
  }
}
```

| Field | Default | Description |
|-------|---------|-------------|
| `MinimumLevel` | `Information` | One of `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Fatal`. Set to `Debug` to see per-mail processing detail (`MailPollingService` rule matches, event creation, suppression — these are intentionally Debug to avoid log spam). |
| `ConsoleEnabled` | `true` | Stream logs to stdout. Useful in container deployments. |
| `FileEnabled` | `true` | Write to a rolling log file. |
| `FilePath` | `logs/mail2snmp-.log` | Pattern; `-` becomes the date in rolling mode. |
| `RetainedFileCountLimit` | `30` | Days of history to keep. |
| `FileSizeLimitBytes` | `52428800` (50 MiB) | Per-file cap before rolling. |

## Metrics

```json
{
  "Metrics": {
    "Enabled": false
  }
}
```

When enabled, Prometheus metrics are exposed at `/metrics`.

## OIDC / SSO (Enterprise)

```json
{
  "Oidc": {
    "Authority": "https://login.example.com",
    "ClientId": "mail2snmp",
    "ClientSecret": "your-secret",
    "RoleClaimType": "roles",
    "AdminClaimValue": "Mail2SNMP.Admin",
    "OperatorClaimValue": "Mail2SNMP.Operator"
  }
}
```

## Entity Configuration

### Mailbox (IMAP)

| Field | Description |
|-------|-------------|
| `Host` | IMAP server hostname |
| `Port` | IMAP port (default: 993) |
| `UseSsl` | Enable TLS (default: true) |
| `Username` | IMAP username |
| `Password` | Encrypted at rest (AES-256-GCM) |
| `Folder` | Mailbox folder (default: INBOX) |

### SNMP Target

| Field | Description |
|-------|-------------|
| `Host` | Target hostname or IP |
| `Port` | SNMP trap port (default: 162) |
| `Version` | `V1`, `V2c`, or `V3` |
| `CommunityString` | For v1/v2c |
| `SecurityName` | For v3 |
| `AuthProtocol` | `None`, `MD5`, `SHA`, `SHA256`, `SHA512` |
| `PrivProtocol` | `None`, `DES`, `AES128`, `AES256` |
| `EnterpriseTrapOid` | Default: `1.3.6.1.4.1.99999.1.1` |

### Webhook Target

| Field | Description |
|-------|-------------|
| `Url` | Webhook endpoint URL |
| `Headers` | JSON object with custom HTTP headers |
| `PayloadTemplate` | Custom JSON payload template |
| `Secret` | HMAC-SHA256 signing key (Enterprise) |
| `MaxRequestsPerMinute` | Rate limit (default: 60) |

### Job

| Field | Description |
|-------|-------------|
| `MailboxId` | Which mailbox to poll |
| `RuleId` | Which rule to evaluate |
| `MaxEventsPerHour` | Rate limit (default: 50) |
| `DedupWindowMinutes` | Deduplication window (default: 30) |
| SNMP/Webhook Targets | Assigned via multi-select |

## Operational Settings

These settings live in `appsettings.json` of each host (Worker / Web / Api).

### IMAP IDLE (real-time mode)

```json
"Imap": {
  "UseIdle": true,
  "IdleRefreshMinutes": 25,
  "IdleConnectTimeoutSeconds": 10
}
```

When `UseIdle = true` the worker holds a long-lived IDLE connection per active mailbox and processes new mail as soon as the server pushes a `CountChanged` notification, instead of waiting for the next scheduled poll. RFC 2177 requires the connection to be cycled at least every 29 minutes; `IdleRefreshMinutes` controls this. Falls back to polling automatically if IDLE is not advertised by the IMAP server.

### Auto-acknowledge

```json
"Events": {
  "AutoAcknowledgeAfterMinutes": 10
}
```

When set to a positive value, `AutoAcknowledgeService` scans every minute for events in state `New` whose age exceeds the threshold and acknowledges them automatically (actor `System.AutoAck`). This triggers the paired `EventConfirmed` SNMP trap, so monitoring systems can self-clear alerts. Set to `0` (default) to disable.

### Forwarded headers (reverse proxy deployments)

```json
"ForwardedHeaders": {
  "KnownProxies": [ "10.0.0.1", "10.0.0.2" ]
}
```

When Mail2SNMP runs behind nginx / HAProxy / IIS ARR, list every reverse-proxy IP here so the rate limiter and audit log see the real client IP from `X-Forwarded-For`. Without this, every login attempt looks like it came from the proxy and the per-IP rate limit becomes a global limit.

### OpenTelemetry

```json
"Otel": {
  "Enabled": true,
  "Endpoint": "http://localhost:4317",
  "ServiceName": "mail2snmp"
}
```

Exports ASP.NET Core and HTTP-client traces via OTLP. Requires an OTLP-compatible collector (Jaeger, Tempo, Grafana Agent, OpenTelemetry Collector).

### API Keys

API keys are managed at runtime via the Web UI (Settings → API Keys). No `appsettings.json` configuration is needed. See [api-usage.md](api-usage.md#2-api-key-x-api-key-header) for header format and scope-to-role mapping.

## CORS (API only)

The Mail2SNMP.Api project allows browser clients from the configured origins to call its REST endpoints.

```json
"Cors": {
  "Origins": [ "https://mail2snmp-ui.example.com", "https://localhost:5173" ]
}
```

| Field | Default | Description |
|-------|---------|-------------|
| `Origins` | `[ "https://localhost:5173" ]` | Whitelist of origins permitted by the default CORS policy. Credentials, all headers, and all methods are allowed for these origins. |

## Dead-Letter Queue (Worker)

The dead-letter retry loop is responsible for re-trying failed webhook deliveries with exponential backoff.

```json
"DeadLetter": {
  "PollIntervalSeconds": 60,
  "BatchSize": 50,
  "MaxAttempts": 5,
  "LockDurationMinutes": 5,
  "BackoffBaseMinutes": 15,
  "InitialDelaySeconds": 30
}
```

| Field | Default | Description |
|-------|---------|-------------|
| `PollIntervalSeconds` | `60` | How often the worker scans for retryable entries. |
| `BatchSize` | `50` | Max entries claimed per scan. |
| `MaxAttempts` | `5` | After this many failures the entry is marked permanently failed. |
| `LockDurationMinutes` | `5` | How long the worker holds an exclusive lease on each entry while retrying. |
| `BackoffBaseMinutes` | `15` | Base for exponential delay (`BackoffBaseMinutes * 2^(attempt-1)`). |
| `InitialDelaySeconds` | `30` | Delay before the first retry of a freshly-created dead-letter entry. |

## Hosting (Web)

```json
"Hosting": {
  "AllInOne": false
}
```

| Field | Default | Description |
|-------|---------|-------------|
| `AllInOne` | `false` | When `true`, the Web project additionally hosts the API endpoints and Worker background services in the same process. Convenient for single-machine deployments; for clustered or HA setups run the three projects separately. |

## Update check (Worker)

```json
"UpdateCheck": {
  "Enabled": true,
  "IntervalHours": 24,
  "FeedUrl": "https://updates.it-consulting-kinner.com/mail2snmp/feed.json",
  "TrapMode": "UntilUpdated"
}
```

| Field | Default | Description |
|-------|---------|-------------|
| `Enabled` | `true` | Toggle the entire update-check pipeline. |
| `IntervalHours` | `24` | How often to fetch the feed. |
| `FeedUrl` | (vendor URL) | Update-feed JSON endpoint. |
| `TrapMode` | `UntilUpdated` | One of `Off`, `Once`, `UntilUpdated`. `Once` sends a single SNMP `Update` trap when a new version is detected; `UntilUpdated` keeps re-sending on every check until the local version matches. Invalid values fall back to `UntilUpdated` with a warning. |

## KeepAlive

```json
"KeepAlive": {
  "Enabled": true,
  "IntervalMinutes": 5
}
```

| Field | Default | Description |
|-------|---------|-------------|
| `Enabled` | `true` | When `true`, the elected primary worker emits a periodic `KeepAlive` SNMP trap to all targets that have `SendKeepAlive = true`. |
| `IntervalMinutes` | `5` | How often to send. |

In multi-instance deployments only the lexicographically smallest active worker lease emits the trap, so monitoring systems see exactly one heartbeat per cluster.

## Retention

```json
"Retention": {
  "EventRetentionDays": 90,
  "AuditRetentionDays": 365,
  "DeadLetterRetentionDays": 30
}
```

| Field | Default | Description |
|-------|---------|-------------|
| `EventRetentionDays` | `90` | Events older than this are purged by the maintenance job. |
| `AuditRetentionDays` | `365` | Audit-log retention. |
| `DeadLetterRetentionDays` | `30` | Permanently-failed dead-letter rows are purged after this many days. |
