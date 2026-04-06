# Master Key Rotation

The master key encrypts all sensitive credentials stored in the database
(IMAP passwords, SNMPv3 auth/priv passwords, webhook secrets). Rotating it
periodically — or immediately after a suspected compromise — is critical
operational hygiene.

> ⚠ **Read this entire document before starting.** A botched rotation can
> render existing credentials unrecoverable.

## When to rotate

| Trigger | Urgency |
|---------|---------|
| Master key environment variable was leaked / committed to a repo | Immediately |
| An admin with key access has left the organisation | Within 24h |
| Annual policy / compliance requirement | Scheduled |
| First production install | Once, before storing real credentials |

## Prerequisites

- Maintenance window (system will be unavailable for several minutes)
- Full database backup (required — see below)
- Access to both the **old** and the **new** master key value
- Administrative access to the host running Mail2SNMP

## Rotation procedure

### 1. Announce a maintenance window

Use the UI → Maintenance Windows → New, or the CLI:

```
mail2snmp maintenance create --name "Master key rotation" --duration 30m
```

This suppresses event traps and webhook deliveries during the rotation.

### 2. Back up the database

**Mandatory.** If anything goes wrong, this is your only path to recovery.

SQL Server:

```sql
BACKUP DATABASE [Mail2SNMP] TO DISK = 'C:\Backups\Mail2SNMP_pre-rotation.bak'
WITH FORMAT, INIT, COMPRESSION;
```

SQLite:

```
copy mail2snmp.db mail2snmp.db.pre-rotation
```

### 3. Stop the service

```
sc stop Mail2SNMP
```

(or `Stop-Service Mail2SNMP` from PowerShell)

### 4. Decrypt all credentials with the OLD key

The CLI provides a one-shot dump command that reads each encrypted credential,
decrypts it with the current `MASTER_KEY` env var, and writes the plaintext to
a temporary file:

```
$env:MASTER_KEY = "<old key>"
mail2snmp credentials export --out C:\rotation\plaintext.json
```

The output is a JSON document mapping entity IDs to plaintext values.
**Treat this file as a secret** — set ACLs so only your admin account can read it.

### 5. Generate the new master key

```
mail2snmp credentials generate-key
```

Copy the output to a secure secret store (HashiCorp Vault, Azure Key Vault,
Windows Credential Manager, etc.). **Do not** commit it to source control.

### 6. Re-encrypt with the NEW key

```
$env:MASTER_KEY = "<new key>"
mail2snmp credentials import --in C:\rotation\plaintext.json
```

This re-reads the temporary file, encrypts each value with the new key,
and writes the new ciphertext back to the database.

### 7. Securely wipe the temporary file

```
sdelete -p 7 C:\rotation\plaintext.json
```

(or use any DOD-grade wipe tool — never just `del`)

### 8. Update the service configuration

Set the new `MASTER_KEY` value in the location where the service reads it.
Common options:

- Windows Service environment variable (registry under `HKLM\SYSTEM\CurrentControlSet\Services\Mail2SNMP\Environment`)
- Group Policy machine environment
- A secrets file consumed by `appsettings.Production.json`

### 9. Start the service and verify

```
sc start Mail2SNMP
```

Then check `/health/ready`. The `master-key` health check must report `Healthy`.
If it reports `Degraded` the new key does not match the ciphertext — **stop
and restore from backup immediately**.

Verify by running a Test on at least one mailbox and one SNMP target via the UI.

### 10. Close the maintenance window

UI → Maintenance Windows → end the active window early.

## Recovery from a botched rotation

1. Stop the service
2. Restore the pre-rotation backup
3. Restore the original `MASTER_KEY` value
4. Start the service
5. Investigate what went wrong before retrying

## Roadmap

A built-in CLI command `mail2snmp credentials rotate-key --new-key <key>` is
planned to make this a single atomic operation. Until then the manual
procedure above is the supported path.
