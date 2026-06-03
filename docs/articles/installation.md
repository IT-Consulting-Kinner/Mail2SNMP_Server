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

## Post-Installation

### 1. Configure the service

Edit `C:\Program Files\Mail2SNMP\appsettings.json` to set your database connection and other settings. See [Configuration Reference](configuration.md).

### 2. Verify the service

```powershell
Get-Service Mail2SNMP
```

### 3. Check logs

Logs are written to `C:\Program Files\Mail2SNMP\logs\mail2snmp-worker-*.log`.

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
