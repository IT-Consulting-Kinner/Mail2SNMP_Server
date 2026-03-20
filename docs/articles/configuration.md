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

## Logging (Serilog)

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "Quartz": "Warning"
      }
    },
    "WriteTo": [
      { "Name": "Console" },
      {
        "Name": "File",
        "Args": {
          "path": "logs/mail2snmp-worker-.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 30,
          "fileSizeLimitBytes": 52428800
        }
      }
    ]
  }
}
```

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
