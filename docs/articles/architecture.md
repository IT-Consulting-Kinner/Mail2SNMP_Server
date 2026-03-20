# Architecture Overview

Mail2SNMP follows a layered architecture with clear separation of concerns.

## Project Structure

```
Mail2SNMP.Models            Domain entities, DTOs, enums
        |
Mail2SNMP.Core              Interfaces, exceptions, business rules
        |
Mail2SNMP.Infrastructure    EF Core, IMAP, SNMP, webhooks, services
        |
   +---------+---------+---------+
   |         |         |         |
 Worker     Api       Web       Cli
```

### Mail2SNMP.Models

Shared data model layer containing entity classes (`Job`, `Mailbox`, `Rule`, `SnmpTarget`, `WebhookTarget`, `Event`, `Schedule`, `MaintenanceWindow`), DTOs for API communication, and enums (`Severity`, `EventState`, `SnmpVersion`, etc.).

### Mail2SNMP.Core

Defines service interfaces (`IJobService`, `IMailboxService`, etc.), custom exceptions (`DependencyException`), and business rule abstractions. No external dependencies.

### Mail2SNMP.Infrastructure

Implements all service interfaces using Entity Framework Core (SQLite / SQL Server), MailKit for IMAP, SharpSnmpLib for SNMP traps, and HttpClient for webhooks. Contains notification channels, the database context, and data migrations.

### Mail2SNMP.Worker

Windows Service hosting background tasks via Quartz.NET:

- **ScheduleSyncService** -- synchronizes job schedules with Quartz
- **MailPollingService** -- polls mailboxes, evaluates rules, sends notifications
- **DeadLetterRetryService** -- retries failed webhook deliveries
- **DataRetentionService** -- cleans up expired events and audit logs
- **HeartbeatService** -- worker lease management (prevents split-brain)

### Mail2SNMP.Api

ASP.NET Core Minimal API providing REST endpoints under `/api/v1/`. Includes Swagger/OpenAPI documentation, health checks, and optional Prometheus metrics.

### Mail2SNMP.Web

Blazor Server application for the management UI. Provides CRUD pages for all entities, a dashboard, event management, and audit log viewing.

### Mail2SNMP.Cli

Command-line tool for administrative tasks (database migration, license management, etc.).

## Database Support

| Provider   | Use Case                        | Quartz Store    |
|------------|---------------------------------|-----------------|
| SQLite     | Single-instance, small setups   | RAMJobStore     |
| SQL Server | Multi-instance with clustering  | AdoJobStore     |

## Security

- Credentials (IMAP passwords, SNMP auth/priv passwords, webhook secrets) are encrypted at rest using AES-256-GCM
- Authentication via ASP.NET Core Identity (local) or OIDC/SSO (Enterprise)
- Role-based authorization: ReadOnly, Operator, Admin
