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
