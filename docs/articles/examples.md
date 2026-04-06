# Example Configurations

Copy-paste-ready snippets for common scenarios. All examples assume `appsettings.json` in the install directory.

## Mail providers

### Microsoft 365 / Exchange Online (modern)

```json
{
  "Mailboxes": "configure via UI"
}
```

UI → Mailboxes → Add:
- **Host:** `outlook.office365.com`
- **Port:** `993`
- **UseSsl:** `true`
- **Username:** `user@yourdomain.com`
- **Password:** App password (NOT account password — requires MFA app password)
- **Folder:** `INBOX`

> **Note:** OAuth2 device code flow is on the roadmap. For now, use App Passwords.

### Gmail

UI → Mailboxes → Add:
- **Host:** `imap.gmail.com`
- **Port:** `993`
- **UseSsl:** `true`
- **Username:** `user@gmail.com`
- **Password:** App password (Settings → Security → 2-Step → App passwords)
- **Folder:** `INBOX`

### Self-hosted Exchange / on-prem IMAP

UI → Mailboxes → Add:
- **Host:** `mail.intra.example.com`
- **Port:** `993` (or `143` + STARTTLS)
- **UseSsl:** `true`
- **Username:** `DOMAIN\user` or `user@example.com`
- **Folder:** `Inbox/Alerts` (sub-folder syntax)

## Database providers

### SQL Server (recommended for production)

```json
{
  "Database": {
    "Provider": "SqlServer",
    "Server": "sql01.intra.example.com",
    "Port": 1433,
    "DatabaseName": "Mail2SNMP",
    "Username": "mail2snmp_app",
    "Password": "<from secrets>",
    "ConnectTimeoutSeconds": 15,
    "CommandTimeoutSeconds": 30
  }
}
```

### SQL Server cluster (Always On)

```json
{
  "Database": {
    "Provider": "SqlServer",
    "Server": "sql-listener.intra.example.com",
    "Port": 1433,
    "DatabaseName": "Mail2SNMP",
    "Username": "mail2snmp_app",
    "Password": "<from secrets>",
    "ConnectTimeoutSeconds": 30,
    "CommandTimeoutSeconds": 60,
    "AdditionalConnectionParameters": "MultiSubnetFailover=True;ApplicationIntent=ReadWrite"
  }
}
```

### SQLite (dev/demo only)

```json
{
  "Database": {
    "Provider": "Sqlite",
    "DatabaseName": "mail2snmp.db"
  }
}
```

> ⚠ SQLite is **not supported** in production. The system shows a banner and reports `degraded` on `/health/ready`.

## SNMP target examples

### v2c (most common)

UI → SNMP Targets → Add:
- **Host:** `nms01.example.com`
- **Port:** `162`
- **Version:** `V2c`
- **Community:** `public` (or your community string)

### v3 with AES-256 + SHA-512

UI → SNMP Targets → Add:
- **Host:** `nms01.example.com`
- **Port:** `162`
- **Version:** `V3`
- **SecurityLevel:** `AuthPriv`
- **Username:** `mail2snmp`
- **AuthProtocol:** `SHA-512`
- **AuthPassword:** `<min 8 chars>`
- **PrivProtocol:** `AES-256`
- **PrivPassword:** `<min 8 chars>`
- **EngineId:** auto (or hex string from your NMS)

## Logging examples

### Production: rolling file + Seq

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft.AspNetCore": "Warning",
        "Mail2SNMP.Worker": "Information"
      }
    },
    "WriteTo": [
      {
        "Name": "File",
        "Args": {
          "path": "C:/ProgramData/Mail2SNMP/logs/mail2snmp-.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 30
        }
      },
      {
        "Name": "Seq",
        "Args": { "serverUrl": "http://seq.intra.example.com:5341" }
      }
    ]
  }
}
```

## Observability

### Prometheus scrape

```yaml
scrape_configs:
  - job_name: mail2snmp
    static_configs:
      - targets: ['mail2snmp.intra.example.com']
    metrics_path: /metrics
```

Enable in `appsettings.json`:

```json
{ "Metrics": { "Enabled": true } }
```

### OpenTelemetry → Jaeger / Tempo

```json
{
  "Otel": {
    "Enabled": true,
    "Endpoint": "http://otel-collector.intra.example.com:4317",
    "ServiceName": "mail2snmp-prod"
  }
}
```

## Webhook target examples

### Microsoft Teams

UI → Webhook Targets → Add:
- **URL:** `https://outlook.office.com/webhook/...`
- **Headers:** `{ "Content-Type": "application/json" }`
- **Payload Template:**
  ```json
  {
    "@type": "MessageCard",
    "@context": "https://schema.org/extensions",
    "summary": "Mail2SNMP Event",
    "title": "{{Severity}}: {{JobName}}",
    "text": "{{Subject}} from {{From}}"
  }
  ```

### Slack

- **URL:** `https://hooks.slack.com/services/...`
- **Headers:** `{ "Content-Type": "application/json" }`
- **Payload Template:**
  ```json
  { "text": ":warning: *{{JobName}}* — {{Subject}}" }
  ```

### Generic JSON sink

- **URL:** `https://eventbus.example.com/ingest`
- **Headers:** `{ "Content-Type": "application/json", "X-API-Key": "<secret>" }`
- **Payload Template:**
  ```json
  {
    "id": "{{EventId}}",
    "ts": "{{CreatedUtc}}",
    "severity": "{{Severity}}",
    "source": "{{From}}",
    "subject": "{{Subject}}"
  }
  ```

## Reverse proxy examples

### nginx

```nginx
server {
    listen 443 ssl http2;
    server_name mail2snmp.example.com;

    ssl_certificate     /etc/nginx/ssl/cert.pem;
    ssl_certificate_key /etc/nginx/ssl/key.pem;

    location / {
        proxy_pass http://127.0.0.1:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_read_timeout 300s;
    }
}
```

### IIS / ARR

Enable WebSockets feature, then in `web.config`:

```xml
<rewrite>
  <rules>
    <rule name="ReverseProxy" stopProcessing="true">
      <match url="(.*)" />
      <action type="Rewrite" url="http://localhost:5000/{R:1}" />
    </rule>
  </rules>
</rewrite>
```
