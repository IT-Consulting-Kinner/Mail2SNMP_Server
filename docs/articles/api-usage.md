# REST API Usage

The Mail2SNMP REST API is available at `http://localhost:5094` (default). All endpoints are under `/api/v1/` and require authentication.

Swagger UI is available at `/swagger` during development.

## Authentication

There are two supported mechanisms; either is sufficient for any endpoint.

### 1. Session cookie

Browser/UI clients sign in via the Web UI and reuse the resulting `Mail2SNMP.Auth` cookie when calling the API on the same host.

### 2. API Key (`X-Api-Key` header)

Recommended for automation scripts, CI pipelines, and external integrations.

**Create a key:** Web UI → *Settings → API Keys → New key*. The plaintext is shown **exactly once** — copy it immediately. Only the SHA-256 hash is stored.

**Use a key:**

```bash
curl -H "X-Api-Key: m2s_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx" \
     https://mail2snmp.example.com/api/v1/mailboxes
```

**Scopes → roles** mapping:

| Scope on key | Effective role(s)              | Can call                     |
|--------------|--------------------------------|------------------------------|
| `read`       | ReadOnly                       | GET endpoints                |
| `write`      | ReadOnly + Operator            | + test, acknowledge, retry   |
| `admin`      | ReadOnly + Operator + Admin    | All endpoints                |

Multiple scopes can be combined comma-separated, e.g. `read,write`.

**Lifecycle:**

- Keys can be set to expire on a specific date or remain valid indefinitely.
- Disabling or deleting a key takes effect immediately on the next request.
- `LastUsedUtc` is updated at most once per 5 minutes per key (debounced) so high-volume callers do not create write storms.

**Security notes:**

- API-key endpoints are subject to the same `Operator`/`Admin` policies as cookie-authenticated requests.
- Keys are only as secure as where they are stored — treat them like passwords.
- For deployments behind a reverse proxy, configure `ForwardedHeaders:KnownProxies` so the rate limiter sees the real client IP.

## Roles

| Role | Permissions |
|------|-------------|
| ReadOnly | View all resources, dashboard |
| Operator | ReadOnly + test connections, acknowledge/resolve events, retry dead letters, dry-run jobs, toggle schedules |
| Admin | Full access including create/modify/delete, suppress events, retry-all dead letters, user & API-key management |

The same role model is enforced in the **Web UI** (since 1.0.1), not only on the
REST API: pages are gated per role and every mutating action re-checks the
caller's role server-side, so a ReadOnly user cannot modify configuration
through the browser either.

## Endpoints

### Mailboxes (`/api/v1/mailboxes`)

| Method | Path | Role | Description |
|--------|------|------|-------------|
| GET | `/` | ReadOnly | List all mailboxes |
| POST | `/` | Admin | Create mailbox |
| PUT | `/{id}` | Admin | Update mailbox |
| DELETE | `/{id}` | Admin | Delete mailbox |
| POST | `/{id}/test` | Operator | Test IMAP connection |

### Rules (`/api/v1/rules`)

| Method | Path | Role | Description |
|--------|------|------|-------------|
| GET | `/` | ReadOnly | List all rules |
| GET | `/{id}` | ReadOnly | Get rule by ID |
| POST | `/` | Admin | Create rule |
| PUT | `/{id}` | Admin | Update rule |
| DELETE | `/{id}` | Admin | Delete rule |

### Jobs (`/api/v1/jobs`)

| Method | Path | Role | Description |
|--------|------|------|-------------|
| GET | `/` | ReadOnly | List all jobs |
| GET | `/{id}` | ReadOnly | Get job by ID |
| POST | `/` | Admin | Create job with target assignments |
| PUT | `/{id}` | Admin | Update job |
| DELETE | `/{id}` | Admin | Delete job |
| POST | `/{id}/dryrun` | Operator | Execute dry-run (evaluates the rule, sends nothing) |
| POST | `/{id}/test-send` | Operator | Send a synthetic event through the job's **real** targets |
| POST | `/bulk` | Admin | Activate, deactivate or delete several jobs in one call |

**Test Send** (`POST /{id}/test-send`) pushes a synthetic event through the job's real
templates and targets — unlike a dry run, the assigned SNMP receivers and webhooks *will*
receive it. The optional `severity` parameter (`Information` by default) is what makes
severity routing verifiable: a target configured for `Critical` only can otherwise never
be anything but "skipped" in the report.

```bash
curl -X POST -H "X-API-Key: $KEY" \
  "https://mail2snmp.example.com/api/v1/jobs/7/test-send?severity=Critical"
```

**Bulk** (`POST /bulk`) takes `{"ids":[1,2,3],"action":"Activate"|"Deactivate"|"Delete"}`.
Each id is handled independently: the response lists `succeeded` and `failed` separately,
so a job that cannot be deleted because a schedule still references it does not abort the
rest of the batch.

### SNMP Targets (`/api/v1/snmp-targets`)

| Method | Path | Role | Description |
|--------|------|------|-------------|
| GET | `/` | ReadOnly | List all targets |
| GET | `/{id}` | ReadOnly | Get target by ID |
| POST | `/` | Admin | Create target |
| PUT | `/{id}` | Admin | Update target |
| DELETE | `/{id}` | Admin | Delete target |
| POST | `/{id}/test` | Operator | Send test trap |

### Webhook Targets (`/api/v1/webhook-targets`)

| Method | Path | Role | Description |
|--------|------|------|-------------|
| GET | `/` | ReadOnly | List all targets |
| GET | `/{id}` | ReadOnly | Get target by ID |
| POST | `/` | Admin | Create target |
| PUT | `/{id}` | Admin | Update target |
| DELETE | `/{id}` | Admin | Delete target |
| POST | `/{id}/test` | Operator | Send test webhook |

### Events (`/api/v1/events`)

| Method | Path | Role | Description |
|--------|------|------|-------------|
| GET | `/` | ReadOnly | List events (filter: `?state=New&jobId=1`) |
| GET | `/{id}` | ReadOnly | Get event by ID |
| POST | `/{id}/acknowledge` | Operator | Acknowledge event |
| POST | `/{id}/resolve` | Operator | Resolve event |
| POST | `/{id}/suppress` | Admin | Suppress event |
| POST | `/{id}/replay` | Operator | Replay notifications |

### Schedules (`/api/v1/schedules`)

| Method | Path | Role | Description |
|--------|------|------|-------------|
| GET | `/` | ReadOnly | List all schedules |
| GET | `/{id}` | ReadOnly | Get schedule by ID |
| POST | `/` | Admin | Create schedule |
| PUT | `/{id}` | Admin | Update schedule |
| DELETE | `/{id}` | Admin | Delete schedule |
| PUT | `/{id}/toggle` | Operator | Toggle active state |

### Maintenance Windows (`/api/v1/maintenance-windows`)

| Method | Path | Role | Description |
|--------|------|------|-------------|
| GET | `/` | ReadOnly | List all windows |
| GET | `/{id}` | ReadOnly | Get window by ID |
| POST | `/` | Admin | Create window |
| DELETE | `/{id}` | Admin | Delete window |
| GET | `/active` | ReadOnly | Check active maintenance |

### Dead Letters (`/api/v1/dead-letters`)

Since 1.1.0 the queue holds failed **webhook and SNMP** deliveries; each entry
references exactly one target kind (`webhookTargetId` **or** `snmpTargetId`).

| Method | Path | Role | Description |
|--------|------|------|-------------|
| GET | `/` | Operator | List failed deliveries (both channels), filtered and paged |
| POST | `/{id}/retry` | Operator | Retry single delivery |
| POST | `/retry-all` | Admin | Retry every entry matching a filter — either target kind |
| POST | `/retry-all/{webhookTargetId}` | Admin | Retry all for one webhook target (kept for 1.1.0 compatibility) |

**Filtering and paging (`GET /`).** All parameters are optional:

| Parameter | Values | Default | Description |
|-----------|--------|---------|-------------|
| `status` | `Pending`, `Locked`, `Abandoned` | all | Restrict to one status. `Abandoned` entries are never retried automatically, so this is the filter to reach for when auditing what was permanently lost. |
| `kind` | `Webhook`, `Snmp` | both | Restrict to one target kind. |
| `targetId` | integer | all | Restrict to one target; interpreted against `kind`. |
| `skip` | integer | `0` | Rows to skip. |
| `take` | integer | `500` | Rows to return, capped at 500. |

The response body is a **bare JSON array** of entries, unchanged from 1.1.0. The number of
rows matching the filter *before* paging is returned in the **`X-Total-Count`** response
header — check it to find out whether you are seeing the whole queue.

`POST /retry-all` accepts the same `status`, `kind` and `targetId` parameters and returns
`{ "count": n, "message": "…" }`. It re-queues each matching entry exactly the way
`POST /{id}/retry` does — status back to `Pending`, lock cleared, attempt counter and last
error reset — so `Abandoned` entries become claimable again. With no parameters it
re-queues the entire queue, both kinds.

```bash
# What has been permanently abandoned, and how much of it is there?
curl -sD - -H "X-API-Key: $KEY" \
  "https://mail2snmp.example.com/api/v1/dead-letters?status=Abandoned&take=50"

# Give every abandoned SNMP trap one more chance
curl -X POST -H "X-API-Key: $KEY" \
  "https://mail2snmp.example.com/api/v1/dead-letters/retry-all?status=Abandoned&kind=Snmp"
```

### Mail Log (`/api/v1/mail-log`)

The per-mail processing trace: what arrived, what each job made of it, and whether anyone
was actually told. This is the endpoint to reach for when the question is *"we emailed an
alert at 03:14 and got no trap — why?"*.

| Method | Path | Role | Description |
|--------|------|------|-------------|
| GET | `/` | ReadOnly | Filtered, paged trace of processed mails |

All parameters are optional: `mailboxId`, `jobId`, `disposition`, `search` (matches sender
or subject), `from` / `to` (UTC bounds on receipt time), `skip`, `take` (default 100, max
500). The pre-paging total is returned in the **`X-Total-Count`** header.

One mail on a shared mailbox produces **one row per active job**, each with its own
outcome — that is the point of the per-job trace. Each row carries two independent fields:

| `disposition` | What the job made of the mail |
|---------------|-------------------------------|
| `NoMatch` | The rule did not match; no event raised |
| `EventCreated` | A new event was created |
| `Deduplicated` | Collapsed into an existing event |
| `MaintenanceSuppressed` | An event was raised but suppressed by a maintenance window |
| `RateLimited` | The job's hourly event budget was exhausted |
| `Unknown` | Claimed but not yet completed (in flight, or a crashed attempt) |

| `delivery` | Whether anyone was told |
|------------|-------------------------|
| `none` | No event was raised, so there was nothing to deliver |
| `delivered` | At least one channel reported a successful send |
| `not-delivered` | The event exists but no channel reported success |
| `suppressed` | A maintenance window was active |
| `failed` | Delivery attempts are in the dead-letter queue (`openDeadLetters` has the count) |
| `purged` | The event has been removed by retention, so its outcome is no longer knowable |

```bash
# Everything that matched but was never delivered, newest first
curl -sD - -H "X-API-Key: $KEY" \
  "https://mail2snmp.example.com/api/v1/mail-log?disposition=EventCreated&take=50"
```

### Dashboard (`/api/v1/dashboard`)

| Method | Path | Role | Description |
|--------|------|------|-------------|
| GET | `/` | ReadOnly | Get dashboard metrics |

### License (`/api/v1/license`)

| Method | Path | Role | Description |
|--------|------|------|-------------|
| GET | `/` | ReadOnly | Current edition and limits |
| POST | `/reload` | Admin | Re-read the license file without restarting |

### Workers (`/api/v1/workers`)

Active worker leases. In a multi-instance deployment the lease set is also what elects the
primary, so this is where to look when a leader-gated task (keep-alive, IDLE, update check,
ingestion-health alarm) is not running.

| Method | Path | Role | Description |
|--------|------|------|-------------|
| GET | `/` | ReadOnly | List active worker leases |
| DELETE | `/{instanceId}` | Admin | Release one lease (e.g. after a node was destroyed) |
| DELETE | `/` | Admin | Release every lease |

### Bulk Export (`/api/v1/bulk`)

| Method | Path | Role | Description |
|--------|------|------|-------------|
| GET | `/export` | Operator | Download the full configuration as one JSON bundle |

Encrypted credentials are intentionally omitted from the bundle, so an export is safe to
attach to a ticket — and so a restore needs the credentials re-entered.

### Health Checks (anonymous)

| Method | Path | Description |
|--------|------|-------------|
| GET | `/health/ready` | Database readiness |
| GET | `/health/live` | Liveness probe |

## Error Responses

| Status | Meaning |
|--------|---------|
| 400 | Validation error (see body for details) |
| 401 | Not authenticated |
| 403 | Insufficient role |
| 404 | Resource not found |
| 409 | Dependency conflict (e.g., deleting a referenced entity) |
| 429 | Rate limit exceeded |

Since 1.1.0 the `401`/`403` semantics are identical on the standalone API host
and in All-in-One mode: unauthenticated or unauthorized requests to `/api/*`
always receive a machine-readable status code (previously the All-in-One host
answered API clients with a `302` redirect to the HTML login page). The API
surface is also identical in both deployment modes, including bulk export.
