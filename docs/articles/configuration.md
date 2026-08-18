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
    "Enabled": false,
    "Port": 9184,
    "Hostname": "localhost"
  }
}
```

| Key | Default | Description |
|-----|---------|-------------|
| `Enabled` | `false` | Exposes the Prometheus scrape endpoint. Off in every host by default. |
| `Port` | `9184` | **Worker only.** The Api and Web hosts map `/metrics` onto the HTTP server they already run; the Worker has none, so it serves its own listener on this port. |
| `Hostname` | `localhost` | **Worker only.** Set to `+` to accept scrapes from other machines. A non-loopback prefix needs administrative rights or a URL ACL (`netsh http add urlacl url=http://+:9184/metrics/ user=<account>`); if the bind fails the Worker logs an error and keeps polling mail rather than refusing to start. |

Most metrics — mail processed, events created, IMAP errors, dead-letter counters, retention
deletions, mailboxes in error — are maintained by the **Worker**, so in a split deployment
that is the process to scrape. In All-in-One mode the Worker services run inside the Web
process and share its registry, so the Web host's `/metrics` has everything.

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

> **Startup validation (1.1.0):** the `Imap`, `Events`, `Retention` and
> `Session` sections are validated at boot. An out-of-range value (e.g.
> `Imap:ConsumerTasks = 0`) stops the host with a clear error naming the
> offending option instead of failing deep at runtime.

### Poll batch limit

```json
"Imap": {
  "MaxMessagesPerPoll": 500
}
```

Bounds how many unseen messages a single poll pass processes (oldest first);
the remainder stays unseen and is picked up by the next cycle. This prevents a
backlogged inbox (e.g. after an outage) from monopolizing a consumer task and
an IMAP connection slot for the entire drain. `0` disables the cap. Default
`500` (1.1.0).

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

### Webhook SSRF protection

```json
"Security": {
  "AllowPrivateWebhookTargets": false
}
```

By default Mail2SNMP refuses to deliver webhooks (and update-feed checks) to
loopback, link-local (incl. the cloud metadata endpoint `169.254.169.254`),
RFC 1918, CGNAT and IPv6 ULA addresses. The guard resolves the target host and
pins the connection to the validated IP, so it cannot be bypassed by DNS
rebinding. Leave this **`false`** in any internet-facing or cloud deployment.

Set it to `true` only when you legitimately need to deliver webhooks to an
internal host (e.g. an on-prem Splunk/Teams relay on a private network). The
update-check feed is always required to be a public HTTPS URL and is never
exempted by this flag.

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

## Session (Web & Api)

Governs the authentication-cookie lifetimes on both hosts (1.1.0 — previously
these were hardcoded and differed between the hosts).

```json
"Session": {
  "SlidingExpiryMinutes": 60,
  "AbsoluteExpiryHours": 8
}
```

| Field | Default | Description |
|-------|---------|-------------|
| `SlidingExpiryMinutes` | `60` | Idle timeout: each authenticated request inside the window extends the session. Range 1–1440. |
| `AbsoluteExpiryHours` | `8` | Hard ceiling measured from sign-in — an actively-used session is terminated after this many hours regardless of activity and requires a fresh login. Range 1–168. |

Sessions are additionally revalidated on every request: a user who is
deactivated or deleted (or whose password changed) loses any live session
immediately on both hosts.

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

The dead-letter retry loop re-tries failed **webhook and SNMP** deliveries with
exponential backoff (since 1.1.0 the queue covers both channels; for SNMP the
retry covers *local* send failures — DNS, sockets, credentials — since v1/v2c
traps are fire-and-forget UDP and receiver-side loss is protocol-invisible).
Automatic retry is Enterprise-gated; entries are persisted in both editions.

```json
"DeadLetter": {
  "PollIntervalSeconds": 900,
  "BatchSize": 10,
  "MaxAttempts": 10,
  "LockDurationMinutes": 7,
  "BackoffBaseMinutes": 15,
  "InitialDelaySeconds": 15
}
```

| Field | Default | Description |
|-------|---------|-------------|
| `PollIntervalSeconds` | `900` | How often the worker scans for retryable entries. |
| `BatchSize` | `10` | Max entries claimed per scan. |
| `MaxAttempts` | `10` | After this many failures the entry is marked permanently failed (Abandoned). |
| `LockDurationMinutes` | `7` | How long the worker holds an exclusive lease on the claimed batch. Values below the safe minimum (`BatchSize × 35 s + 60 s`) are raised automatically with a startup warning, so a lease can never expire while its batch is still being processed. |
| `BackoffBaseMinutes` | `15` | Base for exponential delay (`BackoffBaseMinutes * 2^(attempt-1)`). |
| `InitialDelaySeconds` | `15` | Delay before the retry loop starts after worker boot. |

> **Note:** the defaults shown above match the code. Earlier versions of this
> page listed different values.

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
