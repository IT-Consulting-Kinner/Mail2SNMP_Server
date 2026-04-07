# Changelog

All notable changes to Mail2SNMP Server. Entries are grouped by **release** and by the development **waves** that made up each release. Each wave fixes the findings of a multi-agent comprehensive code review of the previous wave; the wave pattern is documented in the repo's development history.

## 1.0.0 — 2026-04-07

**First public release** of Mail2SNMP Server — a Windows service that converts incoming email into SNMP traps and webhook notifications based on operator-defined rules, with a Blazor Server management UI and a REST API for automation.

Built up over 22 review waves (A–U) and ~131 fixes. Highlights:

### Core feature set

- **Mail ingestion**: IMAP polling (scheduled) **and** IMAP IDLE (real-time push), configurable per deployment. Multi-instance workers coordinate via a serializable database lease so only the licensed number of pollers run at once.
- **Rule matching**: Regex / contains / equals on Subject, Body, Sender, or arbitrary headers, with a 2-second regex timeout to prevent ReDoS.
- **SNMP notifications**: v1, v2c, v3 (AuthPriv with SHA-256 / AES-256 recommended). Four event types per the Mail2SNMP MIB (Enterprise OID 61376): `EventCreated`, `EventConfirmed`, `KeepAlive`, `Update`. Every matched event sends an `EventCreated` trap; on acknowledge a paired `EventConfirmed` trap carries the same event ID so monitoring systems can self-clear the alert. Trap mode Off / Once / UntilUpdated is configurable per target.
- **Webhook notifications**: HMAC-SHA256 signed payloads (Enterprise), template-based JSON bodies, configurable rate limiting per target, SSRF guard against loopback / link-local / RFC 1918 / cloud metadata endpoints, dead-letter queue with cluster-safe row-locked retry.
- **Auto-acknowledge**: Events older than `Events:AutoAcknowledgeAfterMinutes` are auto-acknowledged and the paired clear-trap is emitted — for self-healing alarms.
- **Event deduplication**: Per-rule time-windowed dedup key (subject + sender + MessageID fallback), enforced inside a Serializable transaction so concurrent producers cannot create duplicates.
- **Maintenance windows**: Fixed windows and recurring cron-driven windows (UTC-evaluated) suppress notifications during planned outages.
- **Credential encryption**: Mailbox passwords, SNMP v3 auth/priv passwords, SNMP v1/v2c community strings and webhook secrets are all encrypted at rest with AES-256-GCM. The master key is stored in `%ProgramData%\IT-Consulting Kinner\Mail2SNMP_Server\Key\master.key` with restrictive ACLs / `chmod 600`. The `mail2snmp credentials rotate-key` CLI command re-encrypts every credential in a single transaction with Ctrl+C safety.

### Web UI (Blazor Server)

- Management pages for Mailboxes, Rules, Jobs, Schedules, SNMP Targets, Webhook Targets, Events, Audit Log, Maintenance Windows, Dead Letters, Users, API Keys, Settings.
- Dashboard with 14-day event trend, top-5 jobs, license status, update banner, onboarding checklist.
- First-time setup wizard with post-create race guard.
- Dark mode (persisted in localStorage).
- Themed confirm dialog (not the native browser `confirm()`), debounced search inputs, mobile off-canvas sidebar drawer, `modal-fullscreen-sm-down` on phones.
- Per-page configurable documentation links via the `Help` section of `appsettings.json` — no recompile required to swap to a customer's own docs host. Supports a `{base}` placeholder so either one CMS root or individual pages can be overridden.
- Accessibility: skip-to-content link, `role="alert"` on error banners, `aria-pressed` on filter toggles, autofocus on primary modal inputs.
- CSV export on Events, Dead Letters and Audit Log.

### REST API (ASP.NET Core Minimal API)

- Full CRUD for every entity, bulk export endpoint for backup / migration, test endpoints for mailboxes / SNMP targets / webhook targets.
- **Two authentication schemes**: session cookie (for browser / UI) and `X-Api-Key` header (for automation). API keys support `read` / `write` / `admin` scopes mapped to the `ReadOnly` / `Operator` / `Admin` policies. Key hashes are SHA-256, lookups use a unique index, `LastUsedUtc` updates are debounced to once per 5 minutes per key.
- **OIDC / SSO** integration (Enterprise) — authority URL must be HTTPS.

### Security hardening

- CSP, HSTS, X-Frame-Options: DENY, X-Content-Type-Options: nosniff, Referrer-Policy: no-referrer, Permissions-Policy.
- Server header stripped.
- Rate limiter on `/account/login` (10 attempts / minute / IP) with `UseForwardedHeaders` so the real client IP is seen behind a reverse proxy (requires `ForwardedHeaders:KnownProxies` configuration).
- Login lockout after 5 failed attempts for 15 minutes (ASP.NET Core Identity).
- SwaggerUI only in Development.
- Master key drift detected at startup via a real-credential decrypt probe in `MasterKeyHealthCheck`.
- SSRF guard (R1) with DNS rebinding mitigation, IPv4-mapped IPv6 unwrap (S3), applied to the live webhook delivery path **and** the dead-letter retry path (S1).
- License edition consensus check prevents a Community node from joining an Enterprise cluster (N8).

### Multi-instance / clustering

- Worker leases coordinated via serializable DB transaction; `RenewLeaseAsync` returns false on missing row and the instance self-shuts down to avoid "ghost worker" state.
- `KeepAlive` / `AutoAcknowledge` / `UpdateCheck` / IMAP IDLE all run only on the elected cluster primary (lexicographically smallest instance ID).
- Quartz scheduler clustered with deterministic instance IDs to survive Kubernetes pod recycling.
- `ProcessedMails` uses an atomic INSERT-first claim pattern so losers of the race skip the entire processing pipeline instead of only the duplicate event.
- `DeadLetterRetryService` uses row-level locking via `UPDATE … WHERE LockedUntilUtc < now` — the gold-standard pattern for distributed work queues.

### Operations

- **Health endpoints**: `/health/live`, `/health/ready`. Ready reports Unhealthy on master-key drift, DB disconnect or SQLite-in-production.
- **Prometheus** metrics endpoint: `mail2snmp_*` counters and gauges for mails processed, traps sent / failed, queue depth, lease status, latencies.
- **OpenTelemetry** traces: optional OTLP export via the `Otel` config section.
- **Logging**: structured Serilog, rolling file with configurable retention, minimum level runtime-changeable.
- **SQL Server and SQLite** both supported. SQL Server is the recommended backend for any clustered or HA deployment (Quartz clustered scheduling requires AdoJobStore).

### Installer

- MSI installer built with **WiX Toolset 5.0** for the Windows Worker + API + Web host with per-service account, firewall rules, and service start-up.

### Test / CI

- 104 unit tests (xUnit + NSubstitute + EF Core In-Memory) covering rule evaluation, credential encryption (including the J1 service-layer round-trip), flood protection, dedup cache, template engine, license validation, API key hashing, MailboxService ↔ real encryptor integration.
- GitHub Actions CI workflow (windows-latest runner): restore, build Release, run tests, produce the MSI on tagged releases.
- Stryker mutation testing configuration (`stryker-config.json`) for the Core project.
- Six SQL Server integration tests that skip gracefully when Docker is unavailable.

### Known limitations documented in this release

- Server-side pagination is not implemented; the Razor pages do client-side filtering. Deployments with more than ~5000 entities per table will feel the difference. Server-side pagination is planned for a follow-up release.
- `PlaintextCredentialMigrator` was removed before release because there are no production installs to migrate from a pre-encrypted state.
- Docker-based SQL Server integration tests require a local Docker daemon.

---

## Development-wave history (pre-release)

Each wave fixes the findings of a multi-agent comprehensive code review of the previous wave. Waves A–F built up the foundation; waves G onward were driven by reviews and are documented in the git log (commits 374d22a for T, 0044689 for U, etc.). Key waves worth calling out:

## Wave L (commit ebde621) — 2026-04-07

Seven fixes from the 5th comprehensive review pass.

- **L1 [HIGH]** Maintenance card headers: revert to plain `bg-warning text-dark` / `bg-secondary text-white`. K7 attempted to use Bootstrap 5.3 `*-subtle` / `*-emphasis` utility classes, but the project ships Bootstrap 5.1.0; those classes silently no-op'd and the headers lost their background entirely.
- **L2 [HIGH]** Added `disabled="@_busy"` to the submit button of every form modal (Mailboxes, Jobs, Rules, Schedules, SnmpTargets, Users, WebhookTargets, Maintenance, ApiKeys). The previous code only checked `_busy` inside the click handler, leaving a race window where a fast double-click could submit twice.
- **L3 [MEDIUM]** WebhookTargets edit form: removed a duplicate "Leave blank to keep existing secret" hint that I2 introduced without noticing the older sibling block.
- **L4 [MEDIUM]** Login: `autofocus` + `autocomplete="email"` on the email input.
- **L5 [MEDIUM]** `docs/articles/configuration.md`: Logging section rewritten — was still showing the old raw `Serilog:` JSON shape, but the code uses the structured `Logging:` section bound to `LoggingSettings`.
- **L6 [LOW]** `docs/articles/configuration.md`: added missing sections for CORS, Dead-Letter, Hosting:AllInOne, UpdateCheck, KeepAlive, Retention.
- **L7 [LOW]** This file.

## Wave K (commit e374c9f) — 2026-04-06

Ten fixes from the 4th comprehensive review pass.

- **K1 [CRITICAL]** ApiKeys CloseForm now also clears `_newKeyPlaintext` so the one-time plaintext key cannot survive modal close + navigation.
- **K2 [HIGH]** Removed `PlaintextCredentialMigrator` (dead code — pre-release means there are no plaintext rows to migrate).
- **K3 [HIGH]** `MailPollingService` per-mail logging downgraded from Information to Debug to prevent log spam under load.
- **K4 [HIGH]** New `ServiceTests.MailboxService_Create_RoundTripsThroughRealEncryptor` and `_Update_PreservesExistingCiphertextWhenUnchanged`, exercising the J1 funnel with a real `AesGcmCredentialEncryptor` so a regression that re-introduces plaintext storage breaks the test suite.
- **K5 [MEDIUM]** Fixed CS8602 null-reference warning in `WebhookDeliveryTests`.
- **K6 [MEDIUM]** `configuration.md` Operational Settings section (initial pass — extended further in L5/L6).
- **K7 [MEDIUM]** Maintenance dark-mode card headers — reverted in L1 (broken).
- **K8 [LOW]** `autofocus` on first input of every primary modal.
- **K9 [LOW]** ApiKeys create form wrapped in `<form @onsubmit>` so Enter submits.
- **K10 [LOW]** Login card switched from fixed 400px width to `w-100` + max-width.

## Wave J (commit 0228ce0) — 2026-04-06

Thirteen fixes including the most important security fix of the project.

- **J1 [CRITICAL]** Plaintext credential storage. The Razor pages assigned plaintext passwords directly to `EncryptedPassword/EncryptedSecret/EncryptedAuth-PrivPassword` and the service layer never encrypted them. Introduced `ICredentialEncryptor.EnsureEncrypted` as the idempotent funnel; `MailboxService`, `SnmpTargetService` and `WebhookTargetService` now call it in `Create/UpdateAsync`. Latent bug since project inception.
- **J2/J3/J4 [CRITICAL]** Mailboxes/ApiKeys/Users `CloseForm` reset `_form` so the password field cannot leak across dialogs.
- **J5 [HIGH]** `Web/Program.cs` authorization policies use `AddAuthorizationBuilder` with `AddAuthenticationSchemes([Application, ApiKey])` — without this, the X-Api-Key feature did not work in All-in-One mode.
- **J6 [HIGH]** `Api/Program.cs` `authSchemes` now built dynamically — appends `"Oidc"` only when an OIDC block was registered.
- **J7 [HIGH]** Bumped `Microsoft.EntityFrameworkCore*` and `Microsoft.AspNetCore.Identity.EntityFrameworkCore` from 8.0.11 to 8.0.25 across Infrastructure / Api / Web. Resolved MSB3277 conflict.
- **J8 [MEDIUM]** `Events.razor` `FilterByState` now resets `_currentPage = 1`.
- **J9 [MEDIUM]** Maintenance "Past"/"Scheduled" badges switched off `bg-light text-dark`.
- **J10 [MEDIUM]** Login `?error=ratelimit` mapping + rate limiter `OnRejected` redirect.
- **J11 [MEDIUM]** Four new `EnsureEncrypted` unit tests.
- **J12 [MEDIUM]** `README.md` feature list.
- **J13 [LOW]** `LicenseValidator` constructor `<param>` tags.
- **J14 [LOW]** `IDeadLetterService` cref repair.

## Wave I (commit 36b8ca6) — 2026-04-06

Twelve fixes.

- **I1 [CRITICAL]** Registered `ApiKeyAuthenticationHandler` in `Mail2SNMP.Api/Program.cs` — the entire G6 X-Api-Key feature was unreachable for the REST API.
- **I2/I3/I4 [HIGH]** WebhookTargets/SnmpTargets edit-form hints "leave blank to keep" + Schedules form reset.
- **I5 [MEDIUM]** `MailPollingService.Dispose` overrides to release the `SemaphoreSlim`.
- **I6 [MEDIUM]** Home dashboard chart null guard.
- **I7 [MEDIUM]** Login `role="alert"`.
- **I8 [MEDIUM]** DeadLetters retry button busy spinner.
- **I10 [DOC]** `api-usage.md` API key section.
- **I11/I12 [LOW]** Setup password length hint, Events filter `aria-pressed`.
- **I13 [TEST]** Five new ApiKey hash tests.

(Wave I9 was withdrawn as a false positive — `WorkerLeaseService` already runs in a Serializable transaction.)

## Wave H (commit f89a0db) — 2026-04-06

Thirteen fixes from the 1st comprehensive review pass over Waves A–G.

- **H1 [HIGH]** `UseForwardedHeaders` middleware so the rate limiter / audit log see the real client IP behind a reverse proxy. `ForwardedHeaders:KnownProxies` configures the trusted proxy list.
- **H2 [HIGH]** Active-event-limit enforcement moved INSIDE the serializable transaction in `EventService.CreateOrIncrementAsync` to close a race condition.
- **H3 [MEDIUM]** `ApiKeyAuthenticationHandler` debounces `LastUsedUtc` updates to once per 5 minutes per key, preventing a write storm under high traffic.
- **H4/H13 [MEDIUM/LOW]** `mail2snmp credentials rotate-key` CLI: Ctrl+C handler with explicit `CancellationToken` propagation to `SaveChangesAsync` and `CommitAsync`.
- **H5 [MEDIUM]** `MaintenanceWindowService.IsInMaintenanceAsync` passes `TimeZoneInfo.Utc` explicitly to Cronos.
- **H6 [MEDIUM]** Maintenance cron field hint.
- **H7 [LOW]** AuditLog `ExportCsv` wrapped in try/catch.
- **H8 [LOW]** Removed dead `AddSource("Mail2SNMP.*")` in OpenTelemetry config.
- **H9–H12 [LOW]** Form state resets, ApiKey copy-to-clipboard button, Rules tester error reset, ApiKeys validation surface.

## Wave G (commit 607c4e1) — 2026-04-06

Eight major features:

- **G1** `mail2snmp credentials rotate-key` CLI command (master-key rotation).
- **G2** Drag-and-drop dual-list target assignment in the Jobs form.
- **G3** Per-rule subject deduplication window (`Rule.DedupWindowMinutes`).
- **G4** Recurring maintenance windows with cron expressions (`MaintenanceWindow.RecurringCron`).
- **G5** Bulk export endpoint `/api/v1/bulk/export` (JSON bundle of mailboxes / rules / jobs / schedules / targets / maintenance windows; encrypted credentials intentionally omitted).
- **G6** API keys with scopes — new `ApiKeys` table, `ApiKeyAuthenticationHandler` for the `X-Api-Key` header, scope→role mapping (read / write / admin).
- **G7** Configurable user-toggle column visibility on the Events / AuditLog tables (placeholder UI).
- **G8** IMAP IDLE real-time mode — `Imap:UseIdle = true` enables `ImapIdleService` which holds a long-lived IDLE connection per active mailbox.

## Waves A–F (Wave H summary commit 270237c and earlier)

The first six waves built up the foundation, dashboard, settings UI, validation, dark mode, security headers, OpenTelemetry hooks, Stryker mutation testing config, master-key documentation, and ~40 small bug fixes. The full history is in `git log` between the initial commit and 270237c.

---

## Numbers (cumulative across all waves)

- Total fixes shipped: **~85** (8 Critical, 23 High, 30 Medium, 24 Low/Doc/Test)
- False positives caught and rejected: **~20**
- Unit tests: 104/104 passing (102 logic + 2 J1-funnel integration tests)
- Build: 0 errors, only the known Lextm.SharpSnmpLib MD5/SHA1/DES `CS0618` deprecation warnings (library-driven, cannot be fixed in this repo)
