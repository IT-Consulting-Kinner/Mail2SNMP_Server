# Mail2SNMP Server

Mail2SNMP is a Windows service that monitors email mailboxes and converts matching messages into SNMP traps and/or webhook notifications. It bridges the gap between email-based alerting systems and modern monitoring infrastructure.

## Features

- **Mail-to-SNMP / Webhook**: regex/contains/equals rules on Subject, Body, Sender match incoming mail and emit SNMP v1/v2c/v3 traps and/or HTTP webhook notifications.
- **Trap pairing**: every matched event sends an `EventCreated` trap (OID `1.3.6.1.4.1.61376.1.2.0.1`); on acknowledge a paired `EventConfirmed` trap (OID `…0.2`) carries the same event ID so monitoring systems can clear the alert.
- **IMAP IDLE**: optional real-time mail processing instead of polling (`Imap:UseIdle = true`).
- **Auto-acknowledge**: events older than `Events:AutoAcknowledgeAfterMinutes` are auto-acknowledged and a paired clear-trap is sent.
- **Recurring maintenance windows**: cron-driven (`RecurringCron`) maintenance windows suppress notifications during planned outages.
- **AES-256-GCM credential encryption**: mailbox passwords, SNMP v3 auth/priv passwords and webhook secrets are stored encrypted; master key managed by the OS file system. CLI command `mail2snmp credentials rotate-key` re-encrypts everything atomically.
- **API key authentication**: `X-Api-Key` header with `read` / `write` / `admin` scopes for automation; manage via Web UI → API Keys.
- **Health & metrics**: `/health/live`, `/health/ready` and Prometheus metrics on `/metrics`.
- **OpenTelemetry tracing** (optional, configure `Otel:Enabled`).
- **Bulk export**: download `mailboxes / rules / jobs / schedules / targets / maintenance windows` as a single JSON bundle (encrypted credentials are intentionally omitted).
- **Worker leasing**: multi-instance deployments are coordinated by a serializable database lease so only the licensed number of workers polls at once.

## Architecture

```
Mail2SNMP.Models          Domain entities and DTOs
Mail2SNMP.Core            Interfaces, exceptions, business rules
Mail2SNMP.Infrastructure  EF Core, IMAP, SNMP, webhooks, services
Mail2SNMP.Worker          Windows Service — mail polling and notifications (Quartz)
Mail2SNMP.Api             REST API (ASP.NET Core Minimal API + Swagger)
Mail2SNMP.Web             Management UI (Blazor Server)
Mail2SNMP.Cli             Command-line administration tool
```

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [WiX Toolset v5](https://wixtoolset.org/) (only for building the MSI installer)

## Build

```bash
dotnet restore
dotnet build
```

## Test

```bash
dotnet test
```

## Run (Development)

```bash
# REST API (http://localhost:5094)
dotnet run --project src/Mail2SNMP.Api

# Web UI (http://localhost:5250)
dotnet run --project src/Mail2SNMP.Web

# Worker Service
dotnet run --project src/Mail2SNMP.Worker
```

## Build MSI Installer

```bash
# 1. Publish the Worker service
dotnet publish src/Mail2SNMP.Worker/Mail2SNMP.Worker.csproj -c Release -r win-x64 --self-contained false -o ./publish/worker

# 2. Build the MSI
dotnet build installer/Mail2SNMP.Installer/Mail2SNMP.Installer.wixproj -c Release -p:PublishDir=%cd%/publish/worker
```

The MSI installs the Worker as a Windows Service (`Mail2SNMP`) under `Program Files\Mail2SNMP`.

## Documentation

Generate the DocFX documentation locally:

```bash
dotnet tool install --global docfx
docfx build docfx.json
```

The output is generated in `docs/_site/`.

## License

Proprietary - Adsumus / IT-Consulting Kinner
