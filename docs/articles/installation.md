# Installation Guide

## Prerequisites

- Windows Server 2019+ or Windows 10/11
- [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (ASP.NET Core Runtime)

## MSI Installer

Download the latest MSI from [GitHub Releases](https://github.com/IT-Consulting-Kinner/Mail2SNMP_Server/releases).

The installer:

1. Installs files to `C:\Program Files\Mail2SNMP`
2. Creates data directories at `C:\ProgramData\Mail2SNMP\{data, keys}`
3. Creates a `logs` directory
4. Registers and starts the **Mail2SNMP** Windows Service (auto-start, LocalSystem)

## What the installer contains

| Path under `C:\Program Files\Mail2SNMP\` | Component |
|------------------------------------------|-----------|
| `Mail2SNMP.Worker.exe`                   | The polling service, registered as the `Mail2SNMP` Windows service and started automatically. |
| `cli\mail2snmp.exe`                      | Command-line tool — database migrations, first admin, diagnostics, backups. |
| `ui\Mail2SNMP.Web.exe`                   | Management web UI (Blazor Server), including the first-run `/setup` wizard. |
| `mib\Mail2SNMP-MIB.mib`                  | MIB file for your monitoring system. |

## Post-Installation

### 1. Configure the database and settings

Edit `C:\Program Files\Mail2SNMP\appsettings.json` to set your database connection and other settings. See [Configuration Reference](configuration.md).

### 2. Create the database schema

```powershell
cd "C:\Program Files\Mail2SNMP\cli"
.\mail2snmp.exe db migrate
```

Schema creation and upgrades are performed exclusively by this command — the
services never create or migrate the schema themselves, and they refuse to start
against a database whose schema is missing.

### 3. Start the management UI and create the first admin

```powershell
cd "C:\Program Files\Mail2SNMP\ui"
.\Mail2SNMP.Web.exe
```

Then open the printed URL and complete the `/setup` wizard to create the first
administrator account. From there, add mailboxes, rules, targets and jobs as
described in [Examples](examples.md).

> To run the UI as a service as well, register it with
> `New-Service -Name Mail2SNMP-Web -BinaryPathName "C:\Program Files\Mail2SNMP\ui\Mail2SNMP.Web.exe"`,
> or host it behind IIS/nginx.

### 4. Verify the worker service

```powershell
Get-Service Mail2SNMP
```

### 5. Check logs

Logs are written to `C:\Program Files\Mail2SNMP\logs\mail2snmp-worker-*.log`.

## Upgrading

After installing a newer MSI over an existing deployment, apply any pending
database migrations once before starting the services:

```powershell
cd "C:\Program Files\Mail2SNMP\cli"
.\mail2snmp.exe db migrate
```

`.\mail2snmp.exe db status` shows the connection state and applied migrations.
The upgrade is otherwise drop-in — no configuration changes are required.

## Uninstall

Use Windows Settings > Apps or run the MSI installer again and choose Remove.

## Building from Source

```bash
# Build all projects
dotnet build -c Release

# Run tests
dotnet test -c Release

# Publish the Worker service
dotnet publish src/Mail2SNMP.Worker/Mail2SNMP.Worker.csproj -c Release -r win-x64 --self-contained false -o ./publish/worker

# Build the MSI (requires WiX v5)
dotnet build installer/Mail2SNMP.Installer/Mail2SNMP.Installer.wixproj -c Release -p:PublishDir=%cd%/publish/worker
```

## Least-privilege service account (security hardening)

By default the MSI installs the Worker service to run as **LocalSystem**. This
is the simplest configuration but `LocalSystem` is the most privileged local
account — a compromise of the worker process (which parses untrusted email and
makes outbound HTTP calls) would yield full SYSTEM rights.

For hardened deployments, run the service under a dedicated **virtual service
account** instead:

```powershell
# Point the installed service at a virtual service account
sc.exe config Mail2SnmpWorker obj= "NT SERVICE\Mail2SnmpWorker"

# Grant that account the rights it actually needs:
#   - read/write the data + log directories under %ProgramData%\IT-Consulting Kinner\Mail2SNMP_Server
#   - read the master key file
icacls "%ProgramData%\IT-Consulting Kinner\Mail2SNMP_Server" /grant "NT SERVICE\Mail2SnmpWorker:(OI)(CI)M"
```

The application already adds the **running identity** to the master key file's
ACL when it (re)tightens permissions on startup, so once the service account is
changed and the service restarted, it will retain access to the key without a
manual ACL edit. Verify with:

```powershell
icacls "%ProgramData%\IT-Consulting Kinner\Mail2SNMP_Server\Key\master.key"
```

The ACL should list only `SYSTEM`, `Administrators`, and your service account —
no inherited or `Users` entries.
