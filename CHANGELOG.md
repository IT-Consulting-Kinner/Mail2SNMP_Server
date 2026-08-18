# Changelog

All notable changes to Mail2SNMP Server. Entries are grouped by **release** and by the development **waves** that made up each release. Each wave fixes the findings of a multi-agent comprehensive code review of the previous wave; the wave pattern is documented in the repo's development history.

## Unreleased

### Added

- **The gateway now alerts on its own ingestion failing.** This was the one failure
  the product structurally could not report: every alert it sends is derived from
  an inbound mail, so a mailbox that stops polling produces no mail, therefore no
  event, therefore no notification — a dead ingestion path was indistinguishable
  from a quiet night. The only signal was a red banner on a dashboard somebody had
  to open. Now there is a push signal on all three surfaces:
  - SNMP trap `mail2SNMPIngestionHealthNotification`
    (`1.3.6.1.4.1.61376.1.2.0.5`), sent to every active target regardless of its
    `MinSeverity` — a "Critical only" target certainly wants to hear this.
  - Webhook POST with `{"type":"ingestion-health","status":"degraded"|"recovered"}`.
  - Prometheus gauge `mail2snmp_mailboxes_in_error`; alert on `> 0`.

  Sent once per outage rather than once per check — a trap per minute for the same
  broken mailbox is noise operators learn to filter — and sent again on recovery so
  the alarm can be cleared without a human deciding it looks fine now. In a
  cluster only the elected primary notifies; the gauge is maintained by every
  instance. The MIB ships the new notification type.

- **REST parity for the features that shipped UI-only**: `GET /api/v1/mail-log`
  (filters, paging, `X-Total-Count`, and the delivery outcome),
  `POST /api/v1/jobs/{id}/test-send?severity=`, and `POST /api/v1/jobs/bulk`.

### Performance

- The Mail Log is indexed on `ReceivedUtc` and `(MailboxId, ReceivedUtc)`, and now
  sorts by the column it actually displays. It previously sorted by `ProcessedUtc`
  while showing `ReceivedUtc` — seconds apart for most mail, but visibly unsorted
  while a backlog drains — and the date filter was on a third column again, so
  every load sorted the largest table in the deployment.
- Data retention deletes set-based instead of materializing up to 5000 tracked
  entities per step, once an hour for the life of the process.

### Fixed

- **Worker metrics were unreachable in a split deployment.** `/metrics` is served only
  by the Api and Web hosts, but nearly every metric — mail processed and matched,
  events created, IMAP connection errors, dead-letter counters, retention deletions,
  and the `mail2snmp_mailboxes_in_error` gauge added above — is maintained by the
  **Worker**, which had no HTTP listener at all. In All-in-One mode this was hidden,
  because the Worker services share the Web process. In the split topology the
  documentation recommends for clustered and HA setups, those values went into a
  process nobody could scrape, so an alert on them could never fire. The Worker now
  serves its own endpoint (`Metrics:Port`, default 9184, bound to `localhost` unless
  `Metrics:Hostname` says otherwise). A failed bind is logged, not fatal.
- Metric series are registered at startup rather than on first use, so a freshly
  started Worker reports `mail2snmp_mailboxes_in_error 0` instead of omitting the
  series — an alert rule against an absent series does not behave like one against a
  zero.

- **Audit trail names who made a change.** Every configuration mutation was recorded
  as `System` / `system`, so the log showed that a job was deleted but never by
  whom — the one question an audit trail exists to answer. Changes are now
  attributed to the signed-in user, to `apikey:<name>` for API-key callers, or to
  `cli:<domain>\<user>` for CLI runs; the worker's own changes remain `system`,
  which is the honest answer there. Login and event-lifecycle actions already
  carried a real actor and are unchanged.
- **Mail Log covers the delivery half.** The trace stopped at event creation, so
  the page answered "did this mail become an event?" but not "did anyone get
  told?" — which is the actual question behind "we emailed at 03:14 and got no
  trap". A Delivery column now reports delivered / not delivered / suppressed /
  failed (with the dead-letter count) / purged.
- **Test Send is no longer locked to Information severity.** A target set to
  "Critical only" could only ever be reported as skipped, so the setting most
  likely to be misconfigured was the one thing Test Send could not verify. The
  severity is now chosen per test; Information remains the default.

### Changed

- The dashboard aggregation lives in one place (`IDashboardService`) instead of
  being implemented separately in the REST endpoint and the Blazor home page. The
  two had already drifted — the API surfaced the active maintenance window's name
  and the UI did not.

## 1.2.0 — 2026-08-17 (Blocker fixes)

> **1.1.0 is defective and has been withdrawn from the "latest" slot.** It could
> not write to a SQLite database at all — the default configuration — and its
> migrations produced an unusable schema on SQL Server. Upgrade directly to
> 1.2.0. No data migration is needed beyond the usual `mail2snmp db migrate`;
> a 1.1.0 SQLite installation will have no data to lose, because it could not
> create any.

Second full-surface review (UX consistency, security, correctness, use cases,
architecture, performance) with adversarially verified findings, implemented in
batches. This release ships the blocker fixes immediately rather than waiting
for the remaining findings.

**Upgrading:** run `mail2snmp db migrate` once. From SQL Server on 1.1.0, check
the generated schema before migrating — 1.1.0 may have created tables with
SQLite column types (see C-2).

### Fixed — blockers in 1.1.0

- **C-1** Every write to a table with a `RowVersion` column failed on SQLite
  (`NOT NULL constraint failed`). `IsRowVersion()` marks the column
  store-generated, so EF omitted it from the INSERT — which SQL Server fills in
  and SQLite does not. Concurrency tokens are now stamped by the context on
  SQLite and left to the provider on SQL Server. The default (SQLite)
  deployment could not create a mailbox, rule or job at all.
- **C-2** The migrations hard-coded SQLite store types (`TEXT`, `INTEGER`,
  `BLOB`) and `Sqlite:Autoincrement`, so `db migrate` against SQL Server
  produced a schema of SQLite types with no IDENTITY keys. Migrations are now
  provider-neutral and carry both autoincrement annotations.
- **H-1** A mail claim was keyed `(MessageId, MailboxId)`, so with several jobs
  on one mailbox the first job to poll won the claim and every other job
  silently never fired. The claim is now scoped per job.

### Changed — dead-letter queue

- The queue API is no longer webhook-shaped. `IDeadLetterService` filters and
  bulk-retries through a `DeadLetterQuery` that matches either target kind, so
  SNMP entries (UC-3) are no longer an N+1 of single-entry calls with different
  reset semantics. Bulk retry now applies exactly the same reset as a
  single-entry retry, which means **`Abandoned` entries become claimable again**
  — previously the UI reported "queued for retry" for entries the worker would
  skip forever.
- `GET /api/v1/dead-letters` accepts `status`, `kind`, `targetId`, `skip` and
  `take`, and returns the pre-paging total in the `X-Total-Count` header. The
  response body is unchanged (a bare array), and
  `POST /retry-all/{webhookTargetId}` still works, so 1.1.0 clients keep
  working. New: `POST /retry-all` with the same filter parameters.
- The Dead Letters page filters by status and target kind and pages
  server-side. It previously loaded the newest 500 rows with no filter and no
  total, which hid the rest of the queue — including the `Abandoned` entries an
  operator most needs — behind a view that looked complete.
- CLI: `deadletter retry-all` takes an optional filter
  (`<webhook-target-id>`, `webhook` or `snmp`) and defaults to the whole queue;
  `deadletter list` reports the true queue depth alongside the shown page.

### Changed — configuration

- **AR-6** The `DeadLetter` and `Security` sections are now bound to validated
  options classes like every other section, so an out-of-range value fails the
  boot with a message naming the field instead of producing a service that
  starts and misbehaves. Both sections are declared in `appsettings.json`.
  `DeadLetter:LockDurationMinutes` default corrected to 7 (5 was below the safe
  lease floor and logged a warning on every start).

### Added — tests

- Coverage for the four 1.1.0 features, which shipped with none: UC-4 severity
  routing, UC-5 per-mail disposition and per-job claim, UC-3 SNMP
  dead-lettering, UC-7 Test Send.
- Provider-parity tests that would have caught C-1 and C-2 without a server
  (real SQLite inserts; SQL Server DDL generated via `IMigrator.GenerateScript`).

## 1.1.0 — 2026-08-17 (Quality & Features)

Full-surface review (UX consistency, security, correctness, use cases,
architecture, performance) with adversarially verified findings; all 26
verified findings addressed across 14 fix batches, each fix re-checked by an
independent adversarial verification pass (whose own findings are included).
**All 1.0.x deployments can upgrade; run `mail2snmp db migrate` once — this
release ships four schema migrations (severity routing, mail-log disposition,
SNMP dead-letter, events index).**

### Fixed — silent failure paths

- **FN-1** Dead-letter claim no longer strands entries: the claim is bounded to
  the batch size and expired locks are reclaimable by any instance. Previously
  every eligible row was locked but only one batch processed — surplus rows
  were orphaned permanently on worker restart (silent loss of failed webhooks
  on a routine deploy).
- **FN-2** `Job.DedupWindowMinutes` is now actually honored (base value with
  per-rule override; `0` genuinely disables dedup). It was a dead config field.
- **UC-1** A failing mailbox is no longer invisible: the dashboard computes
  real health (`MailboxesInError`, red banner "Mail ingestion degraded"),
  instead of hardcoding `IsHealthy = true` while KeepAlive kept reporting green.
- **AR-1** All Prometheus metrics are now emitted (previously 18 of 19 were
  defined but never incremented — alerts on `notifications_failed_total` could
  never fire).
- **FN-3** `Notified` is honest: an event is only marked Notified when at least
  one channel actually dispatched (channels return a delivery outcome).
- **FN-4** `MaxActiveEvents` is enforced even when the active set is saturated
  with Acknowledged events.

### Security

- **SEC-1** IMAP without SSL now requires STARTTLS (`SecureSocketOptions.StartTls`)
  instead of silently proceeding over cleartext when the capability is missing.
- **SEC-3** The deactivated-user session revalidation now applies to both hosts
  (shared in `AuthSetup`), and an absolute session lifetime is enforced.
- **SEC-2** The Blazor Server CSP tradeoff (`'unsafe-inline'`) is documented as
  an accepted, known limitation (see architecture docs).

### Added

- **UC-4** Severity-based routing: `MinSeverity` per SNMP/webhook target —
  "page the NOC only for Critical" within a single job. (Migration
  `AddTargetMinSeverity`.)
- **UC-5** Per-mail disposition trace + **Mail Log** page: every inbound mail
  records its outcome (no-match / event created / deduplicated / maintenance-
  suppressed) with a link to the resulting event. (Migration
  `AddProcessedMailDisposition`.)
- **UC-3** SNMP dead-letter retry: failed trap sends (DNS, sockets, credentials)
  are queued and retried like webhooks. (Migration `AddSnmpDeadLetter`.)
- **UC-7** "Test Send" on the Jobs page pushes a synthetic event through the
  job's real templates and targets with a per-target outcome report.
- **UX-5** Bulk activate/deactivate/delete on the Jobs page (parity with the
  other config pages).

### Changed

- **AR-3** One shared registration for the REST API surface in both hosts
  (adds bulk-export to the standalone API); `/api/*` auth failures return
  401/403 in All-in-One mode too (previously 302 to the login page).
- **AR-4** Configuration is validated at startup (`ValidateOnStart` +
  `[Range]` annotations) — bad appsettings values fail the boot with a clear
  message instead of deep runtime errors.
- **AR-5** Session lifetimes come from the `Session` config section (previously
  a dead settings class and diverging hardcoded values per host).
- **AR-2/AR-6** Notification-channel contract cleaned up (dead broadcast method
  removed, string dispatch replaced by constants); options injected via
  `IOptions<T>` consistently.
- **PF-1..PF-6** Performance: dashboard uses `COUNT` aggregates (fixes two
  wrong tile counts), poll passes are bounded (`Imap:MaxMessagesPerPoll`),
  retention drains fully each cycle, SQLite gets a busy-timeout, a
  `(State, CreatedUtc)` index serves the events list, and SNMP DNS lookups are
  async with a timeout.
- **UX-1..UX-4/UX-6** UI consistency: dead theme toggle removed, keyboard focus
  restored on navigation, nav menu mirrors role gates, unified error surfaces,
  explicit page authorization on Home.

### Verification pass (fixes to the fixes)

An independent adversarial verification of all batches surfaced and fixed:

- Sessions are invalidated again after a password change on both hosts —
  installing the custom cookie validation had silently discarded Identity's
  `SecurityStampValidator` (pre-existing on the Web host, newly introduced on
  the API host); it is now chained explicitly.
- The absolute session lifetime actually fires: sliding renewal rewrites
  `IssuedUtc`, so the check now uses an immutable sign-in timestamp.
- A failing webhook **Test Send** no longer crashes with a foreign-key error
  (synthetic test events are never dead-lettered).
- Long dedup windows (> 2x the global default) are no longer truncated by the
  hourly retention sweep.
- Deleting a target with dead-letter entries works again (the nullable FK had
  dropped the former cascade); SNMP dead letters get their own metric
  (`mail2snmp_snmp_deadletter_total`) instead of inflating the webhook counter;
  acknowledge traps respect severity routing; the dead-letter lock lease is
  floored to outlast a worst-case batch (default raised 5 → 7 min); plus
  several smaller UI/logging/metrics corrections.

### Documented scope decisions

- Escalation/on-call/paging remain the downstream NMS's responsibility;
  multi-tenancy is one-instance-per-tenant (see architecture docs).

## 1.0.2 — 2026-06-11 (Quality)

Maintenance release. No functional or security behaviour changes — this ships
the **Wave W** peer-review fixes, the new high-value test coverage, two
structural refactors, and a build-system cleanup. **All 1.0.1 deployments can
upgrade safely; no migration or config change is required.**

### Wave W — peer-review fixes

- **P-1** `AuditSaveChangesInterceptor` no longer double-audits entities the
  services already audit explicitly (Mailbox, SnmpTarget, WebhookTarget, Rule,
  Job, the Job↔target join tables, Schedule, MaintenanceWindow, Event) — the
  interceptor's exclusion list was widened so each change is recorded once.
- **m4** Removed a dead `AddHttpClient<WebhookNotificationChannel>()` typed-client
  registration; the channel resolves its client via `CreateClient("WebhookSend")`.
- **N5** `KeepAliveService` now uses the shared `PrimaryElection.IsPrimaryAsync`
  helper instead of a fourth open-coded copy of the cluster primary-election
  logic.
- **m5** `EventService.ReplayAsync` guards the `Job` navigation with a clear
  `InvalidOperationException` instead of risking a raw `NullReferenceException`.

### Wave W — test quality

- The six SQL Server integration tests now use `[SkippableFact]` + `Skip.IfNot`,
  so they are reported as **skipped** (not **failed**) when Docker is
  unavailable. The previous home-grown `SkipException` had no xUnit integration,
  so the suite reported 6 failures on every Docker-less run.
- New high-value tests for previously-untested security/correctness surfaces:
  the outbound **SSRF guard** decision table; **license-token forgery**
  (alg=none downgrade and foreign-key RS256 both fall back to Community); the
  **event state machine** (illegal transitions throw; duplicate MessageId
  increments HitCount); **worker-lease cluster** consensus and expired-lease
  reaping; the **real `WebhookNotificationChannel`** end-to-end against WireMock
  (success, 5xx dead-letter, SSRF-block dead-letter); and the `CsvCell`
  formula-injection and `RoleGuard` role-enforcement helpers.
- Test count: **104 → 155 passing**, 6 properly skipped.

### Wave W — refactors

- **P-2** Extracted the duplicated authentication/authorization bootstrap shared
  by the API and Web hosts into `AuthSetup` (Identity, X-Api-Key scheme, OIDC
  handler with its role-claim mapping, role policies). The two copies had
  already drifted once; a single definition prevents that class of bug.
- **P-3** Split the 1393-line CLI `Program.cs` god-file into partial-class files
  grouped by command area (Db / System / User / Entity / Test). Pure relocation,
  no behaviour change.

### Build

- Pinned EF Core to a single repo-wide `$(EfCoreVersion)`, eliminating the
  floating `8.0.*` → 8.0.27 skew that produced MSB3277 version-conflict warnings.

Build clean (0 MSB3277); 155/155 unit tests pass, 6 skipped.

## 1.0.1 — 2026-04-07 (Security)

Security patch addressing 10 findings from a fresh independent audit (Wave V).
**All deployments of 1.0.0 should upgrade.** Two of the findings are HIGH-severity
access-control gaps in the management UI.

- **V1 [HIGH]** Broken access control: seven write-capable Blazor pages
  (Mailboxes, Rules, Jobs, Schedules, SNMP Targets, Webhook Targets, Dead
  Letters) lacked an `[Authorize]` attribute, so a **ReadOnly** user could
  create/edit/delete configuration and trigger dead-letter retries through the
  UI — operations the REST API restricts to Admin/Operator. Fixed with
  page-level authorization, role-gated buttons (`AuthorizeView`), and
  server-side role guards in every mutating handler, mirroring the API's
  per-operation policy model.
- **V2 [HIGH]** Deactivated users (`IsActive = false`) could still authenticate
  and existing sessions kept working, because `IsActive` was never enforced.
  Login now rejects inactive accounts and a cookie `OnValidatePrincipal`
  terminates live sessions of disabled/deleted users.
- **V3 [MEDIUM]** SSRF guard was bypassable via DNS rebinding (TOCTOU). Outbound
  webhook/update HTTP clients now resolve once and pin the socket to the
  validated IP via a guarded `SocketsHttpHandler` connect callback.
- **V4 [MEDIUM]** Master-key file ACL now also grants the running service
  identity, and the docs recommend running the Worker under a least-privilege
  virtual service account instead of LocalSystem.
- **V5 [MEDIUM]** CSV formula injection: Events/AuditLog/DeadLetters exports now
  neutralize leading `= + - @ TAB CR` before RFC4180 quoting.
- **V6 [MEDIUM]** Dead-letter webhook retries are now HMAC-signed identically to
  first-attempt deliveries (Enterprise), so receivers enforcing signature
  verification no longer reject retries.
- **V7 [LOW]** Login user-enumeration timing oracle equalized with a dummy hash.
- **V8 [LOW]** Untrusted email subject/from/body are bounded (500 / 256 / 1 MB)
  before rule evaluation and storage (SQLite ignores column length caps).
- **V9 [LOW]** Master-key file permissions are re-tightened on load, not only on
  creation.
- **V10 [LOW]** CLI warns when a password is passed via `--password`; `.gitignore`
  now excludes `master.key`, `*.key`, `*.pem`.

Verified clean (no change needed): JWT license validation (RS256 pinned, no
algorithm-confusion or kid/jku abuse), SQL parameterization, template-engine
JSON escaping (no SSTI), ReDoS timeout, absence of `MarkupString`/XSS, no secret
logging, no DI captive dependencies, IMAP TLS validated by default.

Build clean; 104/104 unit tests pass.

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
