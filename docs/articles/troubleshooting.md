# Troubleshooting Guide

This guide lists the most common issues operators encounter and how to resolve them.

## Diagnostic endpoints

| Endpoint | Purpose |
|----------|---------|
| `/health/live` | Process liveness — returns 200 if the host is running |
| `/health/ready` | Readiness — checks DB, master key, SQLite-in-prod |
| `/metrics` | Prometheus metrics (if `Metrics:Enabled=true`) |
| `/mib/Mail2SNMP-MIB.mib` | Download the MIB definition |

## Logs

Default location (Windows Service install):

```
C:\ProgramData\Mail2SNMP\logs\mail2snmp-YYYYMMDD.log
```

Set `Serilog:MinimumLevel:Default` to `Debug` in `appsettings.json` for verbose output during troubleshooting.

## Common issues

### 1. "Cannot connect to the database"

**Symptom:** Web/Worker fails on startup with `Database schema check failed`.

**Cause:** Schema not yet created, wrong connection string, or SQL Server unreachable.

**Fix:**
1. Verify `Database:Server`, `Database:Port`, `Database:DatabaseName`, credentials in `appsettings.json`
2. Run the migration CLI: `mail2snmp db migrate`
3. Check firewall: SQL Server default port 1433 must be reachable
4. Check `/health/ready` for the specific failing check

### 2. Mails are not being processed

**Symptom:** New mails arrive in the mailbox but no Events are created.

**Checks:**
1. **Schedule active?** UI → Schedules → confirm IsActive = true
2. **Job has a Rule and Mailbox assigned?** UI → Jobs
3. **IMAP credentials valid?** UI → Mailboxes → Test
4. **Rule actually matches?** Check mailbox folder, search regex/contains pattern
5. **Worker running?** UI → Dashboard → Worker status
6. Look for `MailPolling` errors in the log

### 3. Traps are not being sent

**Symptom:** Events are created but no SNMP traps reach the monitoring system.

**Checks:**
1. **Job has SNMP-Targets assigned?** UI → Jobs → "no targets" warning shown
2. **Target IsActive?** UI → SNMP Targets
3. **Target reachable?** UI → SNMP Targets → Test (sends test trap)
4. **Maintenance window active?** UI → Maintenance
5. **Rate limit hit?** Check `Mail2SnmpMetrics.SnmpRateLimited` counter
6. **Master key correct?** Credentials may not decrypt — see `/health/ready`
7. **Firewall:** UDP 162 (default trap port) must be open

### 4. EventConfirmed trap missing after acknowledge

**Symptom:** Trap pairs don't appear in the monitoring system after acknowledge.

**Cause:** Almost always: the SNMP target was *removed* from the job's assignment between EventCreated and acknowledge. The system intentionally only sends EventConfirmed to currently-assigned targets.

**Fix:** Re-assign the target to the job, then re-acknowledge (or manually re-send via DeadLetter UI if available).

### 5. KeepAlive traps missing

**Symptom:** No periodic keep-alive traps to monitoring.

**Cause:** Multi-instance worker deployment — only the primary instance (lowest InstanceId) sends keep-alives. If logs show `Skipped keep-alive trap (not primary)` this is expected.

### 6. Master key warning at startup

**Symptom:** `MasterKeyHealthCheck: degraded — credentials may not decrypt`.

**Cause:** The master key in `MASTER_KEY` environment variable does not match the key used to encrypt existing credentials.

**Fix:**
- Restore the original master key, OR
- Re-enter all mailbox/SNMP/webhook credentials after rotating the key

### 7. License banner shown on dashboard

**Symptom:** Red/yellow banner: "License expired" / "License expires in N days" / "Community license".

**Fix:**
- Place the new `license.lic` in the install directory (next to `Mail2SNMP.Web.exe`) and restart the service

### 8. SignalR / live updates not working

**Symptom:** Dashboard does not refresh in real-time.

**Cause:** Reverse proxy stripping WebSocket headers, or CSP blocking `ws://` / `wss://`.

**Fix:**
- Verify proxy passes `Upgrade` and `Connection: Upgrade` headers
- The CSP `connect-src` directive already allows `ws:` and `wss:`

### 9. High CPU or memory usage

**Checks:**
1. **Polling too frequent?** UI → Schedules → consider increasing IntervalMinutes
2. **Mailbox backlog?** Many unread mails on first poll — let it drain
3. Review Prometheus metrics: `mail2snmp_channel_overflow`, `process_resident_memory_bytes`
4. If metrics suggest GC pressure, increase Worker max threads in `appsettings.json`

### 10. Rate-limited at /account/login

**Symptom:** Login returns HTTP 429.

**Cause:** Wave A added a 10-req/min/IP fixed-window rate limit to `/account/login`.

**Fix:** Wait 60 seconds and retry. If genuinely needed, raise the limit in `Program.cs` (`PermitLimit`).

## Getting more help

1. Check `docs/articles/configuration.md` for full settings reference
2. Open an issue: <https://github.com/IT-Consulting-Kinner/Mail2SNMP_Server/issues>
3. Include: log excerpt (sanitised), `/health/ready` response, version (visible in dashboard footer)
