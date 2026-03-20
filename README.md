# Mail2SNMP Server

Mail2SNMP is a Windows service that monitors email mailboxes and converts matching messages into SNMP traps and/or webhook notifications. It bridges the gap between email-based alerting systems and modern monitoring infrastructure.

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
