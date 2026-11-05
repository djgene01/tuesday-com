# 01 — Requirements

**Purpose.** Define what Tuesday.com must do, what it must not do, and the evidence behind each claim. Every downstream manual derives from this one.

**Precedes:** [02-Architecture.md](02-Architecture.md) · **Siblings:** [09-Risks-and-Open-Questions.md](09-Risks-and-Open-Questions.md), [10-Acceptance-Checklists.md](10-Acceptance-Checklists.md) · **Index:** [00-Contents.md](00-Contents.md)

---

## 1. Product statement

Tuesday.com is a work-operating-system in which a user builds a board, defines its columns, fills it with items grouped into coloured bands, and then looks at the same data through several views. Work is automated by recipes and extended through an API and webhooks.

This is a **functional** description of a well-documented public product category. It is not a description of monday.com's implementation, which is not public. See [11-Clean-Room-Protocol.md](11-Clean-Room-Protocol.md).

**Definition of done for the product:** a user can create a workspace, build a board from scratch, add items and subitems, switch between table/kanban/calendar/timeline/gantt views of that data, collaborate through updates and notifications, automate a status change, and call the REST API — on a single on-premises deployment, with tenant isolation that is proven by test.

---

## 2. Requirement conventions

| Tag | Meaning |
|---|---|
| `FR-x.y` | Functional requirement |
| `NFR-x.y` | Non-functional requirement |
| `[S#]` | Source, in [13-Source-Register.md](13-Source-Register.md) |
| `[D#]` / `[A#]` | Decision / assumption, in [00-Contents.md](00-Contents.md) §4 |
| **MUST / SHOULD / MAY** | RFC 2119 sense |

Anything not marked MUST is negotiable at the phase gate. Anything marked **Out of scope** is not negotiable without a decision revision.

---

## 3. Feature parity matrix

The matrix is the contract. "Parity" means **behaviour that a user could rely on**, never pixel geometry, wording, iconography or data format.

### 3.1 Structure

| # | Capability | Public behaviour observed | Tuesday.com requirement | Tag |
|---|---|---|---|---|
| FR-1.1 | Workspaces | Boards live inside workspaces with open/closed/private visibility | Workspace entity with visibility; boards scoped to exactly one workspace | [A1] |
| FR-1.2 | Board types | Three types — main, shareable, private — differing only in access level, not structure [S9] | `BoardType` enum; identical schema, different authorisation resolver | [S9] |
| FR-1.3 | Groups | Coloured bands inside a board; collapsible; carry summaries | Group entity with title, colour token, fractional position | [S3] |
| FR-1.4 | Columns | Typed, board-wide, added to the entire board, never to one item or group; **500 per board** [S3] | Column definition table; hard limit enforced server-side with a typed error | [S3] |
| FR-1.5 | Column descriptions | Optional help text per column | `Description` on column definition | [S3] |
| FR-1.6 | Column summaries | Some types show an aggregate at the group footer | Summary strategy per type (sum, count, distribution) | [S3] |
| FR-1.7 | Items | Rows with a name; unlimited on paid tiers [S1] | Item entity; no artificial cap, pagination everywhere | [S1] |
| FR-1.8 | Subitems | A child board per parent board; subitems share **their own** column structure, separate from parents [S8] | Child board materialised per parent board; independent columns | [S8] |
| FR-1.9 | Subitem copy | "Copy parent columns" copies values once; **no ongoing sync** [S8] | Copy is a one-shot command; explicitly not a live link | [S8] |
| FR-1.10 | Subitem multiplicity | Multi-level subitems exist in the product | **Level 2 only** at launch; deeper nesting is out of scope (§5) | [S8] |
| FR-1.11 | Connections | Board-relation column linking items across boards | `board_relation` column with one- or two-way semantics | [S2] |
| FR-1.12 | Dependencies | Predecessor/successor links driving Gantt | `dependency` column; cycle detection on write | [S2] |
| FR-1.13 | Mirror & formula | Derived, read-only columns | Read-only computed columns; never directly writable | [S2] |

### 3.2 Column types

The API reference declares **39 column types**: 29 writable, 6 read-only, 1 calculated, 2 deprecated, 1 integration-managed [S2]. Tuesday.com implements all 29 writable types and the 6 read-only ones; deprecated and integration-managed types are **not** implemented.

| Group | Types | Count | Launch phase |
|---|---|---|---|
| Text-ish | `text`, `long_text`, `name`, `email`, `phone`, `link` | 6 | P2 |
| Numeric | `numbers`, `rating`, `hour`, `vote` | 4 | P2 |
| Choice | `status`, `dropdown`, `color_picker` | 3 | P2 |
| Temporal | `date`, `timeline`, `week`, `world_clock` | 4 | P2 (`date`, `timeline`) / P3 (rest) |
| People & tags | `people`, `tags` | 2 | P2 |
| Location & country | `location`, `country` | 2 | P3 |
| Files & docs | `file`, `doc`, `subtasks` | 3 | P3 |
| Structural | `board_relation`, `dependency`, `checkbox`, `button` | 4 | P3 |
| Tracking | `time_tracking` | 1 | P4 |
| **Total writable** | | **29** | |
| Read-only | `creation_log`, `last_updated`, `item_id`, `formula`, `mirror`, `progress` | 6 | P3 |
| Calculated | `auto_number` | 1 | P3 |

`button` and `checkbox` are grouped as structural here because both are event sources for automations rather than data carriers.

### 3.3 Views

Thirteen view types exist publicly, with a **limit of 100 views per board** [S7]. Tuesday.com delivers the six that carry real behavioural weight, and treats the rest as later work.

| View | Public behaviour observed | Requirement | Phase |
|---|---|---|---|
| Table (main) | Grid, inline edit, group bands, add-item row | **MUST** — the reference view | P2 |
| Kanban | Cards generated from **status labels only**; cover columns; card columns; divide-by group/status/relation; hide empty groups [S4] | **MUST** | P3 |
| Calendar | Items placed by date column; month/week | **MUST** | P3 |
| Timeline | Two-date bars, drag to reschedule | **MUST** | P3 |
| Gantt | Bars from timeline/dependency; dependency arrows | **MUST** | P3 |
| Chart | Aggregate by column | SHOULD | P6 |
| Cards / Files gallery | Card layout over files column | MAY | post-1.0 |
| Workload | Capacity per person | Out of scope | — |
| Map, Form, Pivot, Blank | Location map, public form intake, pivot, empty canvas | Out of scope | — |

**Why Kanban is status-only:** this is explicitly documented behaviour, including the note that other column types are not yet supported [S4]. Mirroring the constraint is cheaper and more faithful than exceeding it.

### 3.4 Automation, integration, collaboration

| # | Capability | Public behaviour | Requirement | Tag |
|---|---|---|---|---|
| FR-4.1 | Recipe model | Every automation has a **trigger**, a **condition**, and one or more **actions** [S6] | Three-part recipe schema | [S6] |
| FR-4.2 | Multi-action | Multiple actions chained on one recipe [S6] | Ordered action list, executed serially, failures isolated | [S6] |
| FR-4.3 | Recipe lifecycle | Deactivate (toggle off) vs permanent delete; deleted cannot be restored [S6] | `IsEnabled` flag; soft-delete with hard-delete confirmation | [S6] |
| FR-4.4 | Importance ordering | Recipes can be ranked by importance [S6] | `Importance` integer; execution order tie-break | [S6] |
| FR-4.5 | Scheduled triggers | "Every time period" and "when a date arrives" [S6] | Cron-like + date-arrival scheduler | [S6] |
| FR-4.6 | Plan quotas | Action counts vary by tier — 250/25,000/250,000 per month [S1] | Metered action counter per tenant per month, enforced before execution | [S1] |
| FR-4.7 | Outbound webhooks | HTTP POST with `challenge` handshake on registration [S12] | Handshake endpoint + signed delivery | [S12] |
| FR-4.8 | Updates | Comments on items, with replies and likes | Update, reply, like entities | [S5] |
| FR-4.9 | Notifications | "Notify someone" action; subscription to item/board [S5][S6] | In-app notification + email digest | [S5] |
| FR-4.10 | Attachments | Files column and updates attachments | Binary store keyed by tenant | [S2] |
| FR-4.11 | Search | Cross-board term search | MySQL FULLTEXT over item names, updates, column text projections | [A10] |

### 3.5 API surface

| # | Requirement | Evidence | Tag |
|---|---|---|---|
| FR-5.1 | Authenticated programmatic API exposing boards, groups, columns, items, values, users, workspaces | Public API covers these entities [S10] | [S10] |
| FR-5.2 | Per-tenant **daily call quota** with tiered limits | 200 / 1,000 / 10,000 / 25,000 by tier [S11] | [S11] |
| FR-5.3 | Per-query cost ceiling | 5M complexity points per single query [S11] | [S11] |
| FR-5.4 | Per-minute budget with sliding window | 5M read + 5M write per minute for app tokens; 10M combined for personal tokens [S11] | [S11] |
| FR-5.5 | Idempotent mutation retries | `Idempotency-Key` header, 30-minute response cache, replay header, 409 on concurrent duplicate [S25] | [S25] |
| FR-5.6 | Versioned API with parallel support window | Quarterly versions, ≥3 in parallel, each stable ≥6 months [S26] | [S26] |
| FR-5.7 | Explicit `retry_in_seconds` on every rate-limit error | Documented error contract [S11] | [S11] |

FR-5.3–5.5 are the ones most often skipped and most expensive to retrofit. They are scheduled in P5, not "later".

### 3.6 Roles

| Role | Capabilities that matter | Requirement | Tag |
|---|---|---|---|
| Admin | Account-wide configuration, security, billing, user management | `TenantAdmin` | [S5] |
| Member | Create/edit boards, items, folders; invite; use the API | `Member` | [S5] |
| Viewer | Read-only, unlimited and unbilled, **cannot use the API** | `Viewer` — explicitly denied API scope | [S5] |
| Guest | External, scoped to individually granted shareable boards only | `Guest` with per-board grants and no workspace visibility | [S5] |

The "viewers cannot use the API" rule [S5] is a real authorisation constraint, not a UI nicety. It becomes a policy in [05](05-Security-and-Compliance.md) §4.

---

## 4. Non-functional requirements

| # | Requirement | Target | Verification |
|---|---|---|---|
| NFR-1 | Board first paint (5,000 items, table view) | ≤ 1.5 s p95 server render, ≤ 400 ms interaction response | Load test + browser timing in P2 |
| NFR-2 | Cell edit round-trip (optimistic UI, server-confirmed) | ≤ 250 ms p95 | Instrumented E2E |
| NFR-3 | Concurrent interactive users | 200 per deployment [A3] | SignalR connection soak test |
| NFR-4 | Availability (single site, on-prem) | 99.5% monthly, planned maintenance excluded | Uptime probe |
| NFR-5 | Durability | RPO ≤ 15 min, RTO ≤ 4 h [08](08-Deployment-and-Operations.md) §9 | Restore drill |
| NFR-6 | Tenant isolation | Zero cross-tenant reads under adversarial test | Isolation suite, mandatory gate |
| NFR-7 | Accessibility | WCAG 2.2 AA for grid, dialogs, forms | axe + manual keyboard pass |
| NFR-8 | Auditability | Every mutation attributable to actor, tenant, timestamp, before/after | Audit table + query test |
| NFR-9 | Data residency | All state on operator infrastructure | Architecture review |
| NFR-10 | Upgrade safety | Rolling deploy with no downtime for readers; expand/contract migrations only | Rehearsal in P6 |
| NFR-11 | Observability | Every request traceable by correlation id across web, jobs, automation | Trace assertion test |
| NFR-12 | Browser support | Current Chrome, Edge, Firefox, Safari (last 2 versions) | Playwright matrix |

---

## 5. Out of scope (explicit)

| Item | Why |
|---|---|
| AI features (AI columns, agents, assistants) | Prominent on the public site [S1], but a separate product decision [A8] |
| Marketplace / third-party app store | Owner decision D5; plugin contract only |
| Multi-level subitems beyond depth 2 | Cost/benefit; documented as a known gap |
| Mobile native apps | Web-responsive only at launch |
| Workload, map, form, pivot board views | See §3.3 |
| Real-time co-editing of a single cell (CRDT) | Presence + last-write-wins with conflict notice instead |
| Billing, invoicing, seat metering | Deployment is licensed out-of-band |
| SSO federation beyond OIDC/SAML basics | Phase P6 stretch, see [05](05-Security-and-Compliance.md) §3.4 |
| Data import from monday.com | Deliberately excluded: importing their export format invites copying of their schema and content. See [11](11-Clean-Room-Protocol.md) §5 |
| Localisation | Strings externalised, translations not delivered [A5] |

---

## 6. Project size assessment

### 6.1 T-shirt size: **XL**

Justified against the surface actually documented above, not against ambition.

| Driver | Weight | Why it is expensive |
|---|---|---|
| Dynamic column-value engine | **Very high** | 29 writable types × (render, edit, validate, filter, sort, aggregate, API serialise, automation-addressable). The storage strategy must serve all of them from one schema — see [03](03-Data-Model.md) §4. |
| Four date-aware views | **High** | Kanban, calendar, timeline and gantt each re-project the same data with different interaction models; timeline and gantt both need drag-to-reschedule plus dependency maths. |
| Permission resolution | **High** | Four roles × three board types × workspace visibility × per-item grants. Every read path is a policy decision; a single missed filter is a tenant leak. |
| Automation engine | **High** | Trigger fan-out, condition evaluation, ordered multi-action execution, idempotency, loop prevention, quota metering, retry/dead-letter. |
| Real-time + Blazor Server state | **Medium-high** | Circuit lifecycle, presence, backplane, reconnect, Data Protection key sharing. |
| MySQL under a dynamic schema | **Medium-high** | JSON + projection columns + multi-valued indexes; no transactional DDL; keyset pagination. |
| On-prem operations | **Medium** | Backup/PITR, TLS, IIS + WebSockets + ARR affinity, upgrade rehearsals. |

### 6.2 Effort bands

Person-weeks, one competent full-stack .NET developer, including tests and review but excluding idle time.

| Phase | Band | Confidence |
|---|---|---|
| P0 Foundations | 2–3 | High |
| P1 Identity, tenancy, workspaces | 4–6 | High |
| P2 Boards, groups, columns, grid | 9–12 | Medium |
| P3 Views (4 views) | 8–11 | Medium |
| P4 Collaboration | 4–6 | Medium |
| P5 Automations, webhooks, API | 6–9 | Low-medium |
| P6 Dashboards, hardening, release | 4–6 | Medium |
| **Total** | **37–53** | |

A four-person team with a dedicated tester lands near **5–7 calendar months** to 1.0. The band is wide because P5 is genuinely uncertain: automation semantics are the least specified part of the public surface.

### 6.3 What would change the size

| If this changes | Size moves |
|---|---|
| Drop Kanban/Calendar/Timeline/Gantt to table-only | −8 to −11 pw (L) |
| Add a marketplace with review and sandboxing | +12 to +20 pw (XXL) |
| Move to multi-tenant SaaS with per-tenant databases | +5 to +8 pw |
| Add AI columns with a provider abstraction | +6 to +10 pw |
| Replace MySQL with PostgreSQL | −2 to −4 pw (no provider licence risk, better JSON indexing) |

That last row is deliberately included: D2 was an owner choice, and the manuals must show its cost honestly rather than pretend MySQL was neutral.

---

## 7. Open questions

Full register with owners in [09-Risks-and-Open-Questions.md](09-Risks-and-Open-Questions.md) §6. The ones that block a phase:

| # | Question | Blocks | Default if unanswered |
|---|---|---|---|
| Q1 | Is the Oracle MySQL connector licence purchased, or does the product avoid closed-source distribution? | P2 (data layer) | Proceed with Oracle provider; licence tracked as release blocker |
| Q2 | REST-only or REST + GraphQL for the public API? | P5 | REST only [A9] |
| Q3 | Does "date arrives" include timezone-local midnight per user, or UTC? | P5 | Tenant timezone |
| Q4 | Are guests allowed to see subitems of granted items? | P1 | No — items only |
| Q5 | Retention period for audit and automation-run history? | P6 | 24 months hot, then archive |

---

## 8. Traceability summary

| Source | Requirements it anchors |
|---|---|
| [S1] pricing | Tier gating, quotas, unlimited items, viewer economics |
| [S2] column types | The 39-type taxonomy, read-only semantics |
| [S3] columns | 500-column cap, board-wide scope, summaries |
| [S4] kanban | Status-only card generation, divide-by, cover/card columns |
| [S5] user types | Four roles and their denials |
| [S6] automations | Trigger/condition/action, multi-action, lifecycle, importance |
| [S7] board views | 13 view types, 100-view cap |
| [S8] subitems | Child board, own columns, copy-not-sync |
| [S9] board types | Main/shareable/private, access-only difference |
| [S10] API basics | Entity coverage of the public API |
| [S11] rate limits | Complexity, daily, per-minute, concurrency |
| [S12] webhooks | Challenge handshake |
| [S25] idempotency | Retry semantics |
| [S26] versioning | Quarterly cadence, ≥3 parallel, 6-month stability |

Every FR in §3 cites at least one of these, a decision, or an assumption. No requirement in this document is unattributed.

---

**Next:** [02-Architecture.md](02-Architecture.md) turns these requirements into modules, boundaries and runtime behaviour.
