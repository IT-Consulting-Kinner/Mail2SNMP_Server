# Changelog

All notable changes to Mail2SNMP Server. This project is **pre-release** — there are no published versions yet, only development waves identified by the branch and commit hash. Each wave fixes the findings of a 4-agent comprehensive code review of the previous wave.

## Wave L (commit: pending) — 2026-04-07

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
