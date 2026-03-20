# REST API Usage

The Mail2SNMP REST API is available at `http://localhost:5094` (default). All endpoints are under `/api/v1/` and require authentication.

Swagger UI is available at `/swagger` during development.

## Authentication

Requests must include a valid session cookie (`Mail2SNMP.Auth`). Authenticate via the Web UI or the Identity endpoints.

## Roles

| Role | Permissions |
|------|-------------|
| ReadOnly | View all resources, dashboard |
| Operator | ReadOnly + test connections, acknowledge/resolve events, retry dead letters |
| Admin | Full access including create/modify/delete |

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
| POST | `/{id}/dryrun` | Operator | Execute dry-run |

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

| Method | Path | Role | Description |
|--------|------|------|-------------|
| GET | `/` | Operator | List failed deliveries |
| POST | `/{id}/retry` | Operator | Retry single delivery |
| POST | `/retry-all/{webhookTargetId}` | Admin | Retry all for target |

### Dashboard (`/api/v1/dashboard`)

| Method | Path | Role | Description |
|--------|------|------|-------------|
| GET | `/` | ReadOnly | Get dashboard metrics |

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
