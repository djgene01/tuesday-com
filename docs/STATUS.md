# Development status and design notes

Detailed, phase-by-phase record of what is implemented, what was deliberately deferred, and why.
The front page is [`../README.md`](../README.md); known limitations for the current build are in
[`../RELEASE-NOTES.md`](../RELEASE-NOTES.md).


A clean-room work operating system: boards, groups, items, dynamic columns and multiple
views, built on .NET 10. Behavioural parity with public monday.com surfaces only — no
copied code, assets, trademarks or visual identity. The clean-room rules are recorded in
[`Development-Plans/11-Clean-Room-Protocol.md`](Development-Plans/11-Clean-Room-Protocol.md).

## Status

Phases **P0–P6** are implemented; each was verified against live PostgreSQL 17 and Redis.

| Phase | Scope | State |
|---|---|---|
| P0 | Foundations, health, logging, worker | done |
| P1 | Identity, tenancy, workspaces, roles | done — OpenIddict token server + TOTP MFA included |
| P2 | Boards, groups, columns, item grid | core done — see gaps below |
| P3 | Kanban, calendar, timeline, gantt | core done — see gaps below |
| P4 | Collaboration | done — see gaps below |
| P5 | Automations, webhooks, public API | core done — see gaps below |
| P6 | Dashboards, hardening, on-prem release | core done — see gaps below |

### Data store: PostgreSQL (decided)

The system of record is **PostgreSQL 17** (`postgres:17-alpine`, host port 5433), not MySQL.
The move retired the last licence risk in `09-Risks-and-Open-Questions.md`: the MySQL provider
(Oracle `MySql.EntityFrameworkCore`) is GPLv2-with-FOSS-exception and needed a paid connector for
closed-source distribution; Npgsql is MIT. The plan estimated 2–4 person-weeks for this change;
in this codebase it took about a day, because the typed projection layer was provider-neutral
already and only one registration line named the provider. MySQL remains available for
comparison behind `--profile legacy-mysql` in `docker/compose.yaml`.

### P1 identity (done)

- **OpenIddict token server** at `/connect/token`: password, refresh and client-credentials
  grants, scopes `boards`/`profile`/`email`/`roles`, 1-hour access tokens, 14-day refresh.
  Access tokens are accepted by the existing `Authorization: Bearer` API surface alongside
  hand-issued API tokens. Password grant refuses any account with a second factor rather than
  silently downgrading it.
- **TOTP MFA**: `/account/security` enrols an authenticator (shared key + `otpauth://` URI),
  verifies a real code before enabling, and issues 10 one-time recovery codes. The sign-in page
  challenges for the code after the password step, on its own endpoint, so the code is never
  rendered back. Production requires `IdentityServer:SigningCertificatePath` (a PFX) — without
  it the app fails at start-up instead of at the first request.


### P4 (done)

- **Shipped:** `Update` (sanitised HTML + plain text, soft delete, one level of replies) and
  `UpdateLike` with a unique per-person index; `UpdateService` (post, reply, edit, delete,
  idempotent like toggle) and `UpdateBodySanitizer` — a whitelist that runs on every write, so a
  post cannot store `<script>`, `onclick`, `javascript:` links, images or style attributes.
  Item detail page at `/board/{id}/item/{itemId}` with the thread and the attachments panel; the
  grid has an "Open" link per row; guests get both read-only.
- **Realtime:** `BoardHub` + `BoardBroadcaster`. A post/refresh reaches every open circuit on the
  same item in-process, and SignalR clients in the board group get `update-changed`. The Redis
  backplane for multi-instance fan-out is **not** wired (one process is assumed).
- **Notifications:** `@Display Name` mentions (whole-name match, spaces included) and thread
  participants, minus the actor; `/notifications` with per-row and mark-all read; SMTP behind
  `IEmailSender`, off by default (`Email:Enabled`), gated by an opt-in preference row.
- **Search:** `/search?q=` over item names and update bodies, restricted to boards the caller can
  open. LIKE-based with escaped wildcards — no FULLTEXT index yet, so it is fine at this corpus
  and will not stay fine at a million updates.
- **Attachments:** local storage under `Storage:Root`, GUID fan-out paths, 25 MB cap, metadata in
  `attachments`; downloads go through a data-protected ticket that names the board and item and
  expires in an hour. A tampered token is a 404.
- **Not yet:** mention autocomplete, unread push to the bell, and rich-text toolbar (bodies accept
  simple HTML but the composer is plain text).

### P5 (core done)

- **Automations:** `Automation` (trigger → closed condition grammar → one action), `AutomationRun`
  audit rows including skipped runs, `AutomationService.FireAsync`. Two live triggers:
  `CellChanged` from the grid, and `DatePassed` from `ScheduledAutomationWorker` in the worker
  (one sweep per minute; one run per item per day). `UpdatePosted` exists in the model but no write
  path fires it yet.
- **Actions:** `SetStatus` (`columnKey=value`), `SetDate` (today), `NotifyOwner` (resolves the
  people column to a user), `PostUpdate`, `CallWebhook` (model only).
- **Webhooks:** registration with the challenge echo (a lying endpoint is refused), HMAC-SHA256
  signature in `X-Tuesday-Signature`, dispatch through a named `HttpClient`, last status recorded.
  **No retry/backoff queue** — a failed call is recorded and dropped.
- **Public API:** bearer tokens issued at `/account/tokens` (hash stored, plaintext shown once),
  `GET /api/v1/boards`, `GET /api/v1/boards/{id}/items`, `POST /api/v1/boards/{id}/items/{itemId}/updates`,
  `POST /api/v1/boards/{id}/webhooks`, `POST /api/v1/boards/{id}/webhooks/test`. Fixed-window rate
  limit, 120 requests/minute. 401 without a token, 404 for a board the token may not see.

### P6 (core done)

- **Dashboard:** `/board/{id}/dashboard` — item/group/overdue/done totals, by-status and by-group
  bars, the automation list with recent runs, and registered webhooks.
- **Hardening:** security headers middleware, API-aware status-code pages (an API client sees 401,
  not the HTML 404 page), rate limiting, health checks unchanged, `h1` focus ring suppressed.
- **Packaging:** multi-stage `Dockerfile` running as a non-root user on the ASP.NET 10 runtime,
  `docker/compose.release.yaml` with PostgreSQL, Redis, a files volume, the IdP signing
  certificate mount and health-gated start-up.
  The image was built here (`tuesday-web:local`, 374 MB) and run as a container against the live
  PostgreSQL and Redis: `/health/ready` returned `Healthy` and `/login` returned 200, and a
  token request over plain HTTP was correctly refused in Production. It has **not**
  been deployed to a server.
- **Not yet:** FULLTEXT search, a Redis backplane for multi-instance realtime, webhook retries,
  backup/restore runbook execution, signed release artifacts, and a Firefox/WebKit accessibility
  pass.

### P3 gaps (deliberate, tracked)

P3 is an 8–11 person-week band; what shipped is the four projections and one persisted
interaction each, not the whole view product.

- **Shipped:** persisted views with the 100-per-board ceiling, the active view in the URL, Kanban
  grouped by status with drag and Alt+Arrow keyboard moves, calendar month/week placement by date
  with drag and keyboard rescheduling, timeline bars from two date columns with pointer drag
  (`wwwroot/js/timeline.js`) and ±1 day buttons, Gantt with dependency connectors and a cycle
  refusal, plus `Dependency`, `Location` and `Country` column types.
- **Not yet:** a create-view UI (views are seeded; the 100 limit is enforced in `ViewService`),
  divide-by and cover/column card settings, the doc / board-relation / button column types,
  Gantt dependency editing by dragging a connector, and the AA contrast run across Chromium,
  Firefox and WebKit (only Chromium has been checked). Gantt draws the first 40 rows, so only
  links with both ends drawn become arrows; the panel says how many it drew.

### P2 gaps (deliberate, tracked)

The plan's P2 band is 9–12 person-weeks. What shipped is the load-bearing core; the rest is
not pretended:

- **Shipped:** board/group/item/column schema, eight column types with one validating value
  writer (canonical JSON plus typed projections), inline cell editing over Interactive Server,
  Tab/Enter/Escape keyboard handling, 500-column limit with a typed error, 500-item seed board.
- **Not yet:** row virtualisation, drag reorder within and across groups, subitems, the
  100k-item load probe, and the p95 1.5 s checkpoint at 5,000 items (500 items render in
  ~0.4 s server-side, which is not the same measurement).


## Layout

```
Tuesday.slnx
global.json                  SDK pin (10.0.401, roll forward within the feature band)
Directory.Build.props        net10.0, nullable, warnings as errors
src/Tuesday.Contracts        public DTOs and API contracts
src/Tuesday.Domain           entities and invariants, no infrastructure references
src/Tuesday.Application      use cases, tenant context, permission logic
src/Tuesday.Infrastructure   EF Core, PostgreSQL, Redis, storage
src/Tuesday.Web              Blazor Web App: static SSR + Interactive Server surfaces
src/Tuesday.Worker           background jobs
tests/Tuesday.Tests.Unit             pure logic
tests/Tuesday.Tests.Integration      full web pipeline via WebApplicationFactory
tests/Tuesday.Tests.Isolation        tenant isolation and value projections
docker/compose.yaml          PostgreSQL 17 and Redis 7.4 for local development
```

## Run it

```bash
docker compose -f docker/compose.yaml up -d
dotnet run --project src/Tuesday.Web          # http://localhost:5205
dotnet run --project src/Tuesday.Worker
```

Health: `/health/live` (process only) and `/health/ready` (database, Redis, storage).
Both return JSON with per-check status, duration and the request's correlation id.

## Build and test

```bash
dotnet build Tuesday.slnx -c Release
dotnet test  Tuesday.slnx -c Release
```

## Shipping 0.9.0

This is a **release candidate**, not 1.0. Known limitations are listed in
[`RELEASE-NOTES.md`](RELEASE-NOTES.md) and mapped to open gates in
[`10-Acceptance-Checklists.md`](Development-Plans/10-Acceptance-Checklists.md) §8.

```bash
cp docker/.env.example docker/.env     # fill in every CHANGE ME
# generate the signing certificate: docs/runbooks/signing-certificate.md
docker compose -f docker/compose.release.yaml --env-file docker/.env up -d --build
curl -s http://127.0.0.1:8080/health/ready
```

**Installing is not the same as verifying.** After the container is up, load the board page in a
browser and confirm `/_framework/blazor.web.js` returns 200 `text/javascript` — a health check
alone once hid an image with no Blazor framework files at all.

## Decisions already made

Recorded with rationale in [`Development-Plans/`](Development-Plans/00-Contents.md):

- **.NET 10 LTS** — the only supported stable release in this window.
- **PostgreSQL 17 via EF Core 10**, Npgsql (MIT) — chosen over MySQL because Oracle's EF provider
  is GPLv2-with-FOSS-exception and needs a paid connector for closed-source distribution. The
  licence risk in [`09-Risks-and-Open-Questions.md`](Development-Plans/09-Risks-and-Open-Questions.md)
  is now **closed**.
- **Blazor Web App**, Interactive Server for board surfaces, static SSR for auth and shell.
- **On-prem**, Docker Compose behind IIS; SignalR needs a Redis backplane *and* sticky sessions.

## Local notes

- Use `127.0.0.1`, not `localhost`, in local connection strings: Windows resolves `localhost`
  to `::1` first while the containers publish on IPv4, which shows up as a 2-second timeout.
- Development credentials in `appsettings.Development.json` are throwaway values that match
  `docker/compose.yaml`; production values come from the environment.
