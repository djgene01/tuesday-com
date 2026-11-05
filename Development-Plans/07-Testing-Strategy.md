# 07 — Testing Strategy

**Purpose.** What gets tested, at which level, with which evidence, and what "verified" means at each phase gate.

**Depends on:** [01-Requirements.md](01-Requirements.md) · **Related:** [10-Acceptance-Checklists.md](10-Acceptance-Checklists.md), [04](04-UI-and-UX-Plan.md) §9 · **Index:** [00-Contents.md](00-Contents.md)

---

## 1. Principles

1. **Every requirement has a test, or an explicit waiver.** The traceability tags in [01](01-Requirements.md) make the gap visible.
2. **A passing build is not verification.** Compilation and HTTP 200 prove nothing about behaviour ([04](04-UI-and-UX-Plan.md) §9).
3. **Isolation and authorisation tests are gates, not coverage.** A failure blocks the build regardless of other results.
4. **Test the failure paths first** where the failure is silent — a missing tenant filter returns data, it does not throw.
5. **Deterministic time.** A `IClock` abstraction everywhere; no test touches `DateTime.UtcNow` directly, because date-arrival triggers and quota periods are both time-dependent.

---

## 2. Test pyramid, sized to this product

| Level | Share of effort | Runs | Typical duration |
|---|---|---|---|
| Unit | 30% | Every commit | < 30 s |
| Integration (real MySQL) | 35% | Every commit | 2–4 min |
| Contract (API schema) | 5% | Every PR | < 1 min |
| End-to-end (browser) | 20% | Every PR (smoke) / nightly (full) | 8–20 min |
| Performance | 5% | Nightly / pre-release | 20–40 min |
| Accessibility | 5% | Every PR on changed pages | 2–5 min |

The integration share is high on purpose. The hardest defects in this system live in SQL, indexes, JSON projections, collation and transaction boundaries — all invisible to a unit test with an in-memory provider.

**No in-memory database provider for integration tests.** It does not enforce unique indexes, ignores collation, cannot do JSON functions, and silently diverges on `READ COMMITTED`. Tests that use it pass while production fails.

---

## 3. Unit tests

| Target | What is asserted |
|---|---|
| Domain invariants | 500-column cap [S3], 100-view cap [S7], depth-2 subitem cap, position ordering |
| Permission resolver | The full capability matrix from [05](05-Security-and-Compliance.md) §3.2, as a data-driven matrix |
| Condition evaluator | Every operator × every column family, including empty-vs-null and timezone boundaries |
| Value projections | JSON → projections for all 29 writable types [S2] |
| Position keys | Midpoint generation, rebalance trigger at 64 chars, ordering stability |
| Loop guard | Depth counting and value-change suppression |
| Quota arithmetic | Monthly rollover, plan-tier limits [S1] |
| Formula/mirror evaluation | Read-only semantics; never persisted [S2] |

Framework: xUnit v3 4.0.2 (Apache-2.0), assertions built in. **FluentAssertions is not used** — it is commercial from 8.x with 7.2.2 the last free version [S19], and pinning a frozen major for assertion sugar is a bad trade.

---

## 4. Integration tests

### 4.1 Harness

- **Testcontainers.MySql 4.16.0 (MIT)** [S19] starts a real MySQL 8.4 container per test collection.
- Schema created by running the **real migration scripts**, never by `EnsureCreated`. If a migration is broken, tests must fail.
- Each test runs in a transaction that rolls back, except tests that assert commit/rollback behaviour across the outbox.
- A separate collection runs with a **non-superuser** database account, so grant-related bugs surface.

### 4.2 Mandatory suites

| Suite | Asserts | Blocking |
|---|---|---|
| **Tenant isolation** | All eight tests in [05](05-Security-and-Compliance.md) §4.2 | ✅ |
| **Authorisation matrix** | Each capability × each role, per endpoint and per UI route | ✅ |
| **Outbox atomicity** | A failed transaction leaves no outbox row; a committed one always does | ✅ |
| **Optimistic concurrency** | Two writers, same version, one wins with 409 | ✅ |
| **Projection consistency** | 1,000 random rows: projections match `ValueJson` | ✅ |
| **Migration from baseline** | Fresh install, then N migrations, schema matches model | ✅ |
| **Query plan regression** | Board read and Kanban grouping use the intended index | ✅ on nightly |
| **FULLTEXT behaviour** | Terms below `innodb_ft_min_token_size` are absent — documented, not surprising | ⬜ |

The last row is included because it is the kind of behaviour that gets reported as a bug three weeks after release. A test that *asserts the limitation* turns it into a known characteristic.

---

## 5. Contract tests

| Contract | Method |
|---|---|
| Public REST API | OpenAPI document generated at build; a snapshot diff fails the build on unintended change |
| Breaking-change detection | Compare against the previous release's snapshot; only additive changes allowed in `v1` [S26] |
| Webhook payloads | JSON schema per event type; schema is versioned with the API |
| Automation DSL | JSON schema for recipe documents; validated on save and in tests |

A snapshot diff is chosen over hand-written assertions because it catches *accidental* change, which is the actual risk.

---

## 6. End-to-end tests

Playwright 1.64.1 (MIT) [S19], against the real app with a real database.

### 6.1 Smoke set (every PR)

| # | Scenario |
|---|---|
| E2E-1 | Sign in, land on a workspace, open a board |
| E2E-2 | Create an item, edit a status cell, reload, value persists |
| E2E-3 | Switch table → kanban → table, no data loss |
| E2E-4 | Open an item panel by deep link on a cold load |
| E2E-5 | Guest sees only the granted board and gets 404 for others |

### 6.2 Full set (nightly)

| Area | Scenarios |
|---|---|
| Grid | Reorder, cross-group drag, multi-select, keyboard-only edit, column resize persistence |
| Subitems | Expand, edit a subitem column, copy parent columns (and prove no ongoing sync [S8]) |
| Views | Kanban divide-by with empty lanes hidden [S4]; timeline drag; gantt dependency arrow |
| Collaboration | Update, reply, like, notification arrives in the bell, email queued |
| Automations | The worked example in [06](06-Integrations-and-Extensibility.md) §2.7 |
| Webhooks | Handshake, delivery, signature verification against a local receiver |
| Permissions | Viewer cannot mutate; role change invalidates a live circuit |
| Search | Term found in item name and in an update body |

### 6.3 Test-data strategy

| Layer | Approach |
|---|---|
| Unit | Builders with sane defaults; a board fixture factory |
| Integration | Per-test transaction + factory; seeded column-type registry |
| E2E | A scripted fixture tenant created by an API call, not by SQL, so the fixture exercises the same path as a user |
| Performance | Generated at scale: 20k items × 12 columns, plus 100k for the checkpoint [A2] |
| Never | Production data, anonymised or otherwise |

Fixtures are created through the API in E2E deliberately: a SQL-inserted fixture can succeed where the real creation path would fail, which hides bugs in exactly the flow under test.

---

## 7. Performance testing

| Scenario | Load | Pass criteria |
|---|---|---|
| Board open, 5,000 items, table | 50 concurrent users | p95 ≤ 1.5 s (NFR-1) |
| Cell edit round-trip | 50 users, 1 edit/s each | p95 ≤ 250 ms (NFR-2) |
| Board open, 100,000 items | 10 users, paginated | No timeout, p95 ≤ 3 s |
| SignalR soak | 200 connections, 10 min | No dropped circuit, memory flat (NFR-3) |
| Automation throughput | 10k events/min | Backlog drains within 5 min |
| API | 100 rps sustained | Quota errors behave; no 5xx |
| Search | 20 qps, mixed terms | p95 ≤ 400 ms |

Tooling: k6 or NBomber for HTTP/SignalR (NBomber's current licence should be confirmed before adoption — its NuGet entry now points at a licence page rather than an SPDX id [S19]). Blazor-specific load uses a headless Playwright fleet, because a circuit is not an HTTP request and an HTTP load tool will not reproduce circuit memory behaviour.

**The 100k checkpoint exists to falsify the design early**, not to promise 100k as a supported size [A2]. If it fails, the fix is pagination and index work, and it is cheaper to learn that in P2 than in P6.

---

## 8. Accessibility testing

| Check | Tool | Gate |
|---|---|---|
| Automated rules | axe-core via Playwright | No serious/critical violations on changed pages |
| Keyboard-only grid | Scripted Tab/Arrow/Enter/Escape sequence | All of U-1..U-7 reachable ([04](04-UI-and-UX-Plan.md) §4) |
| Focus restoration | After optimistic update and after dialog close | Focus on the same cell/trigger |
| Contrast | Token pairs computed | ≥ 4.5:1 text, ≥ 3:1 UI |
| Screen reader | Manual NVDA pass per phase | Documented, not automated |

**Contrast must be verified in Chromium, Firefox and WebKit.** Engines disagree at the 4.5 boundary: a token that passes in Chromium can fail in Firefox because of subpixel and colour-space handling. Running only the default engine produces false confidence. This is a known trap and is scheduled, not left to chance.

---

## 9. Verification hooks per phase

Each phase's exit criteria in [phase-plan.json](phase-plan.json) reference these hooks. A phase is not complete until its hooks have real output.

| Hook | Produces | Recorded in |
|---|---|---|
| `build` | Release build, zero warnings | Phase log |
| `unit` | Test count + pass/fail | Phase log |
| `integration` | Test count, including isolation suite | Phase log |
| `e2e-smoke` | Screenshot + pass/fail per scenario | Phase log |
| `ui-evidence` | Screenshot inspected by a human/vision check, plus asset/console diagnostics ([04](04-UI-and-UX-Plan.md) §9) | Phase log |
| `a11y` | Violation count by severity | Phase log |
| `perf` | p95 per scenario | Phase log |
| `isolation` | The eight blocking tests | Must be green to pass the gate |

### 9.1 What counts as evidence

| Claim | Acceptable evidence | Not acceptable |
|---|---|---|
| "The page works" | Screenshot showing content, plus console clean | HTTP 200 |
| "Persistence works" | Reload after edit shows the new value | A green unit test on the repository |
| "Tenant isolation holds" | The isolation suite green | Code review |
| "Accessible" | axe clean + keyboard script + manual note | axe alone |
| "Performant" | p95 from a load run | "Feels fast" |

---

## 10. Continuous integration

| Stage | Trigger | Blocking |
|---|---|---|
| Restore + build (warnings as errors) | Every push | ✅ |
| Unit | Every push | ✅ |
| Integration (MySQL container) | Every push | ✅ |
| Isolation + authorisation suites | Every push | ✅ |
| Contract snapshot | Every PR | ✅ |
| E2E smoke | Every PR | ✅ |
| E2E full | Nightly | ⬜ (raises alert) |
| Performance | Nightly + pre-release | ⬜ until P6, then ✅ |
| Accessibility | Every PR on changed routes | ✅ |
| Dependency + licence scan | Every PR | ✅ |
| Secret scan | Every push | ✅ |
| SBOM generation | Every release | ✅ |

Warnings-as-errors is deliberate: nullable warnings and unresolved-component Razor warnings ([04](04-UI-and-UX-Plan.md) §8.1) are precisely the ones that become production defects.

---

## 11. Known testing limitations

| Limitation | Effect | Mitigation |
|---|---|---|
| No Safari on Windows CI | WebKit-only coverage | Playwright WebKit approximates; real Safari is a pre-release manual check |
| SignalR soak is expensive | Runs nightly only | Circuit leaks may survive a PR | 
| No true multi-region latency | On-prem single-site, so acceptable | Documented |
| MySQL container version pinned | Production 8.4 drift | Upgrade tested in P6 |
| Browser zoom/OS scaling | Manual only | Per-phase manual note |

These are stated so that "all tests pass" is never read as "everything is verified".

---

**Next:** [08-Deployment-and-Operations.md](08-Deployment-and-Operations.md).
