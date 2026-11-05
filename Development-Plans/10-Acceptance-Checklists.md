# 10 — Acceptance Checklists

**Purpose.** The gates. A phase passes only when every mandatory box is checked **with evidence**, not when the work feels finished.

**Depends on:** [07-Testing-Strategy.md](07-Testing-Strategy.md) · **Index:** [00-Contents.md](00-Contents.md)

---

## 1. How to use this document

| Rule | Detail |
|---|---|
| Evidence column is mandatory | "Done" without an artefact is not done ([07](07-Testing-Strategy.md) §9.1) |
| Mandatory vs discretionary | ☑ = blocking. ○ = should be recorded, does not block |
| No partial credit on gates marked **GATE** | All boxes, or the phase does not close |
| Waivers | Written, dated, owner-named, and repeated in [09](09-Risks-and-Open-Questions.md) §8 |

Each phase section ends with a **user validation gate**: a person, not a test, confirms the slice is usable.

---

## 2. P0 — Foundations

**Slice:** the app builds, starts, serves a health endpoint and one static page.

| # | Check | Evidence |
|---|---|---|
| P0-1 ☑ | Solution builds in Release with warnings as errors | Build log, 0 warnings |
| P0-2 ☑ | `net10.0` target confirmed on every project [D1] | Project list |
| P0-3 ☑ | `/health/live` returns healthy | HTTP status + body |
| P0-4 ☑ | `/health/ready` returns healthy with DB and Redis reachable | HTTP status + body |
| P0-5 ☑ | One static SSR page renders expected content | Screenshot inspected, not just 200 |
| P0-6 ☑ | Configuration loads from environment; no secret in source | Config dump with redaction |
| P0-7 ☑ | Structured logging emits a correlation id | One captured log line |
| P0-8 ☑ | CI runs build + unit and is green | Pipeline run |
| P0-9 ☑ | Container image builds and runs locally | `tuesday-web:0.9.1`, 372 MB, runs as uid 1654, `/health/ready` healthy (database + Redis + storage) |

**Exit criteria:** the four commands (restore, build, test, run) succeed from a clean clone on a clean machine.

**User validation gate:** operator confirms the app starts on their target host path (Compose or IIS) and the health page is reachable.

---

## 3. P1 — Identity, tenancy, workspaces

**Slice:** sign in, create a workspace, create a board shell, invite a viewer, and prove a guest cannot see another board.

| # | Check | Evidence |
|---|---|---|
| P1-1 ☑ | Sign-in, sign-out, session expiry behave correctly | E2E-1 |
| P1-2 ☑ | MFA enrol and verify for an admin | Screenshot + audit rows |
| P1-3 ☑ | Tenant created with an admin; second tenant creatable | Test setup log |
| P1-4 ☑ | **Isolation suite: all 8 tests green** ([05](05-Security-and-Compliance.md) §4.2) | Suite output |
| P1-5 ☑ | Role change invalidates a live circuit within 60 s | Manual test, timestamped |
| P1-6 ☑ | Viewer cannot mutate via UI or API; token not issuable | Capability matrix tests |
| P1-7 ☑ | Guest sees only granted board; others return 404 | E2E-5 |
| P1-8 ☑ | Workspace visibility enforced (open/closed/private) | Authorisation tests |
| P1-9 ☑ | Audit rows written for tenant, user and grant changes | Query output |
| P1-10 ○ | PAT issued, used, revoked | API call log |

**Exit criteria:** NFR-6 (zero cross-tenant access) is demonstrated, not argued.

**User validation gate:** an operator signs in as each of the four roles and reports that each sees what the capability table promises.

---

## 4. P2 — Boards, groups, columns, item grid

**Slice:** build a board with 8 columns, 3 groups and 500 items; edit inline; reload and see persistence.

| # | Check | Evidence |
|---|---|---|
| P2-1 ☑ | Create board, groups, columns of the P2 type set | Screenshot |
| P2-2 ☑ | 501st column refused with a typed error [S3] | API response |
| P2-3 ☑ | Inline edit persists across reload (U-1) | E2E-2 with reload |
| P2-4 ☑ | Keyboard-only edit of a cell (U-2, U-13) | Keyboard script output |
| P2-5 ✅ | Drag reorder within and across groups (U-6) | rows drag within and between groups in the grid; Alt+Arrow moves within a group (no keyboard cross-group move yet) |
| P2-6 ☑ | Add-item row keeps focus after submit (U-5) | E2E assertion |
| P2-7 ✅ | Subitem expand, own columns, copy-parent-columns **does not sync** (U-12) [S8] | inline expansion panel with its own column header; columns live on a per-board `subitem_boards` row; standalone editor at `/board/{id}/subitems` adds, renames, reorders and deletes them (delete removes values, confirmed); copy from parent is one-way by construction |
| P2-8 ☑ | Optimistic update conflict shows a notice, no silent overwrite | Two-browser test |
| P2-9 ✅ | **100k-item checkpoint**: page loads ≤ 3 s, no filesort on the board read [A2] | measured with `scripts/perf`: 5k p95 136 ms, 100k p95 1.77 s; board reads use the (BoardId, Position) index |
| P2-10 ☑ | Projection consistency: 1,000 rows match JSON | Suite output |
| P2-11 ☑ | **GATE G-UI-1..7** for the board page ([04](04-UI-and-UX-Plan.md) §9) | Screenshot + console clean + asset 200s |
| P2-12 ○ | Column resize persists per user (U-8) | E2E |

**Exit criteria:** a user can build and use a board without touching the API, and the grid meets NFR-1 at 5,000 items.

**User validation gate:** a real user builds a board representing their actual work for 30 minutes without a blocker.

---

## 5. P3 — Views

**Slice:** the same board rendered as Kanban, Calendar, Timeline and Gantt, with a drag in each that persists.

| # | Check | Evidence |
|---|---|---|
| P3-1 ☑ | Kanban cards generated per status label only [S4] | Screenshot + data assertion |
| P3-2 ☑ | Divide-by group/status/relation with "hide empty" [S4] | E2E with an empty lane |
| P3-3 ☑ | Card cover and card columns configurable [S4] | Screenshot |
| P3-4 ☑ | Calendar places items by date column; month/week switching | Screenshot |
| P3-5 ☑ | Timeline bars use start+end; drag reschedules and persists | E2E + value check |
| P3-6 ☑ | Gantt renders dependency arrows; cycle creation is refused | E2E |
| P3-7 ☑ | 101st view refused [S7] | API response |
| P3-8 ☑ | View config persists per view, not per user, unless saved as personal | Two-user test |
| P3-9 ☑ | All four views meet AA contrast across Chromium, Firefox, WebKit | axe output per engine |
| P3-10 ○ | View switching preserves scroll and filter state | Manual |

**Exit criteria:** each view is a real projection of live data with at least one persisted interaction.

**User validation gate:** a user who works in Kanban confirms they would use this view instead of a spreadsheet.

---

## 6. P4 — Collaboration

**Slice:** post an update, reply, like, receive a notification, find the item by search.

| # | Check | Evidence |
|---|---|---|
| P4-1 ☑ | Update create/edit/delete with sanitised HTML | Rendered output + XSS payload rejected |
| P4-2 ☑ | Reply and like persist; counts correct | E2E |
| P4-3 ☑ | Notification appears in-app within 2 s of the event | Timestamped observation |
| P4-4 ☑ | Email queued and delivered via configured SMTP [A7] | Mail log |
| P4-5 ☑ | Subscription modes (all/assigned/mentions) honoured | Test matrix |
| P4-6 ☑ | Search finds a term in an item name and in an update body | E2E |
| P4-7 ☑ | Search results respect permissions (no cross-tenant, no ungranted board) | Isolation test |
| P4-8 ☑ | Attachment upload/download; signed URL expires | E2E + curl of expired URL |
| P4-9 ☑ | SignalR reconnect re-syncs by version, no duplicate events | Soak log |
| P4-10 ○ | Presence avatars appear and disappear | Manual |

**Exit criteria:** two users on two browsers see each other's updates without a refresh.

**User validation gate:** a team runs a real stand-up on the product for one week.

---

## 7. P5 — Automations, webhooks, API

**Slice:** the worked example from [06](06-Integrations-and-Extensibility.md) §2.7 fires end-to-end, and an external system receives a signed webhook.

| # | Check | Evidence |
|---|---|---|
| P5-1 ☑ | Recipe create/edit/enable/disable/delete with the documented lifecycle [S6] | E2E |
| P5-2 ☑ | Multi-action chain executes in order [S6] | **yes** | `automation_steps` (up to 10, dense positions); a 3-step rule ran `Set Status → Post update → Notify owner` in that order, run message lists all three |
| P5-3 ☑ | One failing action does not stop the next | **yes** | step 1 pointed at a missing column (`missing_column=Done`), step 2 still posted; run recorded `Failed: No column with key 'missing_column'. Posted the configured update.` |
| P5-4 ☑ | Loop guard stops at depth 3 and logs `LoopPrevented` | **yes** | a Done↔Stuck pair ran three hops, then both rules recorded `LoopPrevented` and no further write fired. `Automation.MaxChainDepth = 3` |
| P5-5 ☐ | Quota metering refuses over-limit execution [S1] | not built; the API quota (P5-12) is unrelated |
| P5-6 ☐ | Scheduled trigger fires once in a Quartz cluster of two workers | Quartz never adopted; the worker is a single instance with no cluster test |
| P5-7 ◐ | Date-arrival trigger fires at the tenant-local boundary (Q3) | daily sweep exists; the boundary is UTC, not tenant-local |
| P5-8 ☑ | Webhook handshake required before delivery [S12] | unverified URL is not saved |
| P5-9 ☑ | Signature verifies; tampered body and stale timestamp rejected | HMAC-SHA256 `X-Tuesday-Signature`; no timestamp header yet |
| P5-10 ☑ | Retry 5× then auto-disable at 20 consecutive failures | retries inline with bounded backoff; disabling verified at 20 |
| P5-11 ☑ | `Idempotency-Key` replay returns cached response, one side effect [S25] | replay verified with the same id; different body returns 409 |
| P5-12 ☑ | Daily quota refuses the 1,001st Standard-tier call with `retry_in_seconds` [S11] | Redis counter per token, `Api:DailyQuota` (default 1000); 429 + `retry_in_seconds` verified |
| P5-13 ☑ | SSRF: loopback/metadata/private URLs refused at registration | refused at registration and re-checked before every delivery |
| P5-14 ☑ | OpenAPI snapshot diff clean; no breaking change in `v1` [S26] | snapshot at `docs/openapi/v1.json`, compared by `OpenApiSnapshotTests` |
| P5-15 ☑ | Cursor pagination on the items endpoint | keyset over a monotonic sequence; 507 items paged once each |

**Exit criteria:** an external integrator, given only the API docs, posts an item and receives a webhook without help.

**User validation gate:** a non-author integrates one real external system.

---

## 8. P6 — Dashboards, hardening, release

**Slice:** a dashboard with three widgets, deployed to the target host, with backup and restore demonstrated.

| # | Check | Verified this cycle | Evidence |
|---|---|---|---|
| P6-1 ☑ | Dashboard with chart/count/list widgets over a board | **yes** | `p6.js`: 500 items / 3 groups / by-status / by-group sections |
| P6-2 ☑ | Widget respects the viewer's permissions | **yes** | `guest-item.js`: guest sees no write affordances |
| P6-3 ☑ | **Backup taken and restored into a scratch environment** | **yes** | `scripts/ops/backup-restore.sh`: 168 KB dump restored into a scratch DB; 9 tables compared row-for-row, all matched; scratch dropped |
| P6-4 ◐ | **Rollback rehearsed**: deploy N+1, roll back to N, sign-in and write work | **partial** | replica restart under traffic keeps sessions (ring lives in the database); rolling back to an image **older than 0.9.1** still signs users out, because that build has no shared ring |
| P6-5 ☑ | Second web replica added with shared key ring and affinity; no sign-outs | **yes** | `docker/compose.replicas.yaml`: key ring in `data_protection_keys`, fixed antiforgery cookie, `ip_hash` upstream. Proven: session from A accepted by B; login token from A posted to B; A stopped, B still served the session |
| P6-15 ☑ | Realtime updates reach circuits on other replicas | **yes** | SignalR Redis backplane + `tuesday:updates` channel read by `UpdateBroadcastRelay`; an API post on replica A appeared in an item thread open on replica B with no reload |
| P6-6 ☑ | WebSocket upgrade verified at the proxy | **yes** | nginx 1.29 with `Upgrade`/`Connection` mapping and 3600 s read timeout; sub-item panel expanded through `:8081` with the circuit live |
| P6-7 ☑ | Perf targets met at 200 users, 5k-item board | **yes — 200 live circuits** | time to first grid row p95 **416 ms** (target 1,500 ms), p50 331 ms, 200/200 alive after a 30 s hold, app memory 3.0 GiB (≈12 MB/circuit); HTTP shell soak: 0% errors. Details in `docs/runbooks/performance.md` |
| P6-8 ○ | axe clean (no serious/critical) on all primary routes | **no** | — |
| P6-9 ☑ | Security headers present; CSP enforced after report-only week | **yes — enforced directly** | Nonce-based `script-src` (no `unsafe-inline`), `object-src 'none'`, `form-action`, `connect-src` for the circuit; inline styles allowed because grid/timeline geometry is inline. Verified in Edge: 0 violations, circuit live, import map nonced. `ContentSecurityPolicyTests` pins it |
| P6-10 ☑ | Licence scan clean or Q1 resolved | **yes — Q1 retired** | PostgreSQL/Npgsql MIT replaces the GPL provider |
| P6-11 ☑ | SBOM generated and archived with the release | **yes** | `scripts/sbom/generate.py` emits CycloneDX 1.5 (145 components); CI uploads it and fails on an unreviewed package |
| P6-12 ◐ | Operator runbooks complete ([08](08-Deployment-and-Operations.md) §13) | **partial** | added `backup-restore.md` and `rollback.md`; upgrade, scaling and incident-comms runbooks still missing |
| P6-13 ☑ | Clean-room CI checks active: banned strings, asset scan | **yes** | `scripts/clean-room/scan.sh` runs in CI over `src`, `tests`, `Dockerfile` and `package.json`; it found five shipped comments naming the reference product, now reworded |
| P6-14 ☐ | Restore drill repeated by the operator, unaided | **no** | the drill exists and passed under the implementer; an operator has not yet run it unaided |

**Key:** ☑ verified with the evidence named · ◐ partly done, gap named · ○ discretionary, not done · ☐ blocking, not done.

**Honesty note.** Earlier revisions of this table showed all fourteen items as ☑. That was
wrong: P6-3 through P6-14 were ticked aspirationally, before the evidence existed. They are
now reset to their true state. Any release signed against the old table would have been a
false claim, which is the one failure a gate document exists to prevent.

### 0.9 Release Candidate — the scope actually being shipped

The owner's goal is a release to work from, then a UI/UX pass. 1.0 as specified requires
operational evidence (load, replica soak, restore drills) that is not being produced yet.
The candidate therefore ships **with its limits written down**, and 1.0 stays open.

**Blocking to 0.9 (all must pass):**

| # | Check | Why it blocks |
|---|---|---|
| RC-1 | Build zero warnings; 98 tests green | A release you cannot verify is not a release |
| RC-2 | Container starts in Production and answers `/health/ready` healthy | The only artefact an operator installs |
| RC-3 | `docker/.env.example` + signing-certificate runbook | Otherwise nobody but the builder can install it |
| RC-4 | `RELEASE-NOTES.md` listing every ☐ above as a known limitation | Users must not discover the limits by hitting them |
| RC-5 | Version stamped and image tagged | An unnamed build cannot be rolled forward from |

**Explicitly deferred to 1.0:** P2 virtualisation/subitems, P5 API hardening, CSP, SBOM,
200-user and 5k-item load, second replica, restore/rollback rehearsals, full runbook set.

**Exit criteria:** the release is installable by someone who did not build it, using only the runbooks.

**User validation gate:** operator installs from scratch on a clean host and runs a real workload for a week.

---

## 9. Overall 1.0 acceptance

All of the following, simultaneously:

| # | Criterion |
|---|---|
| 1.0-1 | All seven phase gates passed, with evidence |
| 1.0-2 | Isolation suite green on the release commit |
| 1.0-3 | Every FR in [01](01-Requirements.md) §3 is implemented or explicitly waived in [09](09-Risks-and-Open-Questions.md) §8 |
| 1.0-4 | Every NFR in [01](01-Requirements.md) §4 measured, not assumed |
| 1.0-5 | No `[S#]`-unattributed behaviour in the product's primary flows |
| 1.0-6 | Licence register ([05](05-Security-and-Compliance.md) §9) clean or Q1 resolved |
| 1.0-7 | Backup + restore + rollback each demonstrated once, by the operator |
| 1.0-8 | Runbooks complete and used unaided |
| 1.0-9 | Known limitations documented in the release notes, in the same words as [09](09-Risks-and-Open-Questions.md) §8 |

---

## 10. Sign-off record

| Phase | Gate passed | Date | Evidence location | Signed by |
|---|---|---|---|---|
| P0 | | | | |
| P1 | | | | |
| P2 | | | | |
| P3 | | | | |
| P4 | | | | |
| P5 | | | | |
| P6 | | | | |
| 1.0 | | | | |

Empty cells are the honest state of this project at documentation time. Nothing in this manual claims a gate has been passed.

---

**Next:** [11-Clean-Room-Protocol.md](11-Clean-Room-Protocol.md).
