# 09 — Risks and Open Questions

**Purpose.** The risks we accept knowingly, the ones we must retire before a phase, and the decisions that are still open.

**Depends on:** all · **Index:** [00-Contents.md](00-Contents.md)

---

## 1. Risk method

| Field | Meaning |
|---|---|
| **Likelihood** | Low / Medium / High — probability of occurring during 1.0 |
| **Impact** | Low / Medium / High / Critical — effect on schedule, correctness or legality |
| **Owner** | The role accountable, not the person who notices |
| **Retire by** | The phase gate at which this must be closed or explicitly accepted |

A risk without a retire-by date is a wish. Every risk below has one.

---

## 2. Technical risks

### 2.1 The column-value model does not scale

| | |
|---|---|
| **Likelihood** | Medium |
| **Impact** | High |
| **Owner** | Data / backend lead |
| **Retire by** | P2 |

`ItemColumnValue` becomes the largest table, and its composite indexes are wide (`TenantId`, `BoardId`, `ColumnId`, value). At 100k items × 12 columns, a Kanban grouping query must still use the status index rather than a scan.

**Early warning:** the P2 100k checkpoint ([07](07-Testing-Strategy.md) §7) shows p95 > 3 s or a filesort in the plan.

**Mitigation:** prove the query plan in P2 with real data; keep the projection-only index set minimal; archive closed boards. **Contingency:** move long-text and rarely-queried types out of the hot row, then partition by tenant hash.

### 2.2 MySQL provider licence becomes a blocker

| | |
|---|---|
| **Likelihood** | Medium |
| **Impact** | High |
| **Owner** | Product owner |
| **Retire by** | Before any external distribution (Q1) |

D2 originally chose Oracle's `MySql.EntityFrameworkCore`, licensed GPLv2 with a FOSS exception [S19], which implied a commercial MySQL connector licence for closed-source distribution.

**Superseded (implementation):** the system of record is now **PostgreSQL 17 with Npgsql (MIT)**. The licence risk is closed rather than budgeted. Measured cost in this codebase: about one day, not the 2–4 person-weeks estimated in `01-Requirements.md` §6.3 — the typed projection layer was already provider-neutral and only `DependencyInjection.ConfigureDatabase` named the provider. Migrations were regenerated as a single `InitialPostgres` plus `OpenIddictStores`; the seeded workspace was rebuilt from `DevelopmentSeeder`.

**Mitigation:** budget it; contain the provider behind `Infrastructure` ([02](02-Architecture.md) §3.2 rule 3); keep a MIT Dapper path for hot reads. **Contingency:** if the licence is refused, the only MIT EF-Core-compatible MySQL path today is unavailable (Pomelo has no EF 10 build [S20]), so the options are PostgreSQL or Dapper-only — both are re-plans, which is why this must be settled early rather than at release.

### 2.3 Pomelo catches up and we are on the wrong provider

| | |
|---|---|
| **Likelihood** | Medium |
| **Impact** | Medium |
| **Owner** | Backend lead |
| **Retire by** | P2 review |

If Pomelo ships 10.x during the build, we are paying for a licence we no longer need. **Mitigation:** re-check the milestone at each phase gate; the provider is behind one interface, so switching is a contained change.

### 2.4 Blazor circuit memory under real load

| | |
|---|---|
| **Likelihood** | Medium |
| **Impact** | High |
| **Owner** | Frontend lead |
| **Retire by** | P2 |

A 5,000-row board held in circuit state, times 50 users, is a memory cliff. **Mitigation:** virtualisation, per-item version signals, bounded page size ([04](04-UI-and-UX-Plan.md) §4.2), circuit cap with a clear error page. **Early warning:** soak test shows rising memory that does not settle after navigation.

### 2.5 Automation engine runaway

| | |
|---|---|
| **Likelihood** | Medium |
| **Impact** | Critical |
| **Owner** | Backend lead |
| **Retire by** | P5 |

A recipe cascade can saturate the worker and the database. **Mitigation:** depth cap, value-change suppression, per-board execution budget, quota metering before execution ([06](06-Integrations-and-Extensibility.md) §2.6). **Test:** the self-triggering recipe is an explicit acceptance case, not an afterthought.

### 2.6 On-prem variance breaks the deployment story

| | |
|---|---|
| **Likelihood** | High |
| **Impact** | Medium |
| **Owner** | Ops lead |
| **Retire by** | P6 |

Every operator's network, TLS and Windows build differ. **Mitigation:** two documented topologies with identical prerequisites (§6.3 of [08](08-Deployment-and-Operations.md)); a preflight script that checks WebSockets, affinity, key-ring writability and NTP before install; a support matrix rather than "it should work".

### 2.7 Timezone semantics of date triggers

| | |
|---|---|
| **Likelihood** | High |
| **Impact** | Medium |
| **Owner** | Product owner |
| **Retire by** | P5 (Q3) |

"Date arrives" is ambiguous across DST and multi-timezone tenants. **Mitigation:** decide Q3 before P5; store UTC, evaluate in tenant timezone, test the DST boundary explicitly.

---

## 3. Legal and IP risks

### 3.1 Trade-dress and trademark exposure

| | |
|---|---|
| **Likelihood** | Medium if unmanaged |
| **Impact** | Critical |
| **Owner** | Product owner + counsel |
| **Retire by** | Continuous; reviewed at every gate |

Visual and verbal similarity is the classic clean-room failure: an engineer "makes it look right" by copying a palette, a font or a wordmark.

**Mitigation:** [11-Clean-Room-Protocol.md](11-Clean-Room-Protocol.md); original tokens only; banned-string CI check; no screenshots of their product in the repository; behaviour-only pattern registry ([04](04-UI-and-UX-Plan.md) §6.2).

### 3.2 Evidence that a behaviour was derived from public documentation

| | |
|---|---|
| **Likelihood** | Low |
| **Impact** | High |
| **Owner** | Documentation lead |
| **Retire by** | Continuous |

**Mitigation:** every requirement carries `[S#]` and the register records URL + access date + what it supported ([13](13-Source-Register.md)). This is the single most useful artefact if the question is ever asked.

### 3.3 Dependency licence drift

| | |
|---|---|
| **Likelihood** | High |
| **Impact** | Medium |
| **Owner** | Build lead |
| **Retire by** | Every release |

Three packages in this space changed licence to RPL/commercial within a year (MediatR, AutoMapper, MassTransit) [S21][S23], and FluentAssertions went commercial too [S19]. **Mitigation:** licence scan in CI; permissive-only policy for new dependencies; the rejected-package table in [02](02-Architecture.md) §11.2 records *why*, so nobody re-adds them.

### 3.4 Third-party code in the plugin path

| | |
|---|---|
| **Likelihood** | Low (operators only) |
| **Impact** | Medium |
| **Owner** | Architect |
| **Retire by** | Before any plugin ships |

A plugin runs in-process with database credentials ([06](06-Integrations-and-Extensibility.md) §6.3). **Mitigation:** operator-only installation, explicit documentation that it is not a sandbox, no marketplace.

---

## 4. Product risks

| # | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| 4.1 | Users expect AI features that are out of scope [A8] | High | Medium | Explicit scope statement in the UI's help, not just in docs |
| 4.2 | "Parity" is judged against a moving target — the public product ships quarterly | High | Medium | Parity is defined as a dated snapshot (3 Nov 2026), recorded in [01](01-Requirements.md) |
| 4.3 | Multi-level subitems requested immediately | Medium | Medium | Depth-2 is stated; the data model already allows depth-N, so it is a UI change, not a migration |
| 4.4 | GraphQL demanded by integrators | Medium | Medium | Q2; the API layer is versioned so a GraphQL facade can sit beside REST |
| 4.5 | Import from competitors demanded | High | Low | Deliberately excluded for legal reasons ([01](01-Requirements.md) §5); CSV import covers the real need |
| 4.6 | On-prem operators expect a managed backup | Medium | Low | Runbook + documented RPO/RTO; no claim of managed service |

---

## 5. Decision log

Chronological, with the reasoning that produced the decision. Superseded decisions stay visible.

| ID | Date | Decision | Rationale | Reversible? |
|---|---|---|---|---|
| D1 | 2026-11-03 | .NET 10 LTS | Only supported stable release; 11 is RC3/STS; 8 & 9 EOL 10 Nov 2026 [S15] | Yes, at a major-version cadence |
| D2 | 2026-11-03 | EF Core 10 + MySQL (Oracle provider) | Owner choice; the only EF 10 MySQL provider [S19][S20] | Yes, contained in `Infrastructure` |
| D3 | 2026-11-03 | Blazor Web App, Interactive Server + static SSR | Dense grid, SignalR already required, no WASM payload [S16] | Yes, per-surface |
| D4 | 2026-11-03 | On-prem: Compose primary, IIS secondary | Owner infrastructure | Yes |
| D5 | 2026-11-03 | Core recipes + multi-action + schedules, no marketplace | Parity without marketplace economics [S6][S1] | Yes |
| D6 | 2026-11-03 | Clean-room: behavioural parity only | Legal defensibility | No |
| A1–A10 | 2026-11-03 | Assumptions as listed in [00](00-Contents.md) §4 | Reasonable defaults pending owner input | Yes |

### 5.1 Decisions deliberately deferred

| Decision | Deferred to | Why deferring is safe |
|---|---|---|
| GraphQL | P5 | REST contract is versioned; a facade is additive |
| Per-tenant databases | Post-1.0 | Shared schema works for the documented scale |
| Column-level permissions | Post-1.0 | Model carries `column_restriction` in the permission formula but no UI |
| CRDT co-editing | Post-1.0 | Last-write-wins with notice is honest and cheap |
| Multi-level subitems | Post-1.0 | Data model supports it; UI does not |
| AI columns | Separate initiative | No architectural dependency |

---

## 6. Open questions

| # | Question | Owner | Needed by | Default if unanswered | Cost of a late answer |
|---|---|---|---|---|---|
| **Q1** | Is the Oracle MySQL connector licence purchased, or does distribution stay internal-only? | Product owner | Before external distribution | Internal-only deployment; licence tracked as a release blocker | Re-platform the data layer late |
| **Q2** | REST only, or REST + GraphQL? | Product owner | P5 start | REST only [A9] | A GraphQL facade adds ~2–3 pw later |
| **Q3** | "Date arrives" evaluated in tenant timezone or per-user timezone? | Product owner | P5 start | Tenant timezone | Retrofitting per-user semantics touches scheduler and tests |
| **Q4** | May guests see subitems of a granted item? | Product owner | P1 | No — granted items only | Permission model change plus audit |
| **Q5** | Audit and automation-run retention period? | Ops lead | P6 | 24 months hot, then archive | Storage sizing, not architecture |
| **Q6** | Is enterprise SSO (OIDC federation) in 1.0? | Product owner | P6 | Stretch only | Later addition; no data impact |
| **Q7** | Will a hosted multi-tenant edition ever be required? | Product owner | Post-1.0 | No | Per-tenant databases are a migration, not a rewrite |

Q1 and Q4 are the two that change code. Q3 changes behaviour subtly and is the most likely to be discovered as a bug in production.

---

## 7. Documentation gaps

| Gap | Consequence | Plan |
|---|---|---|
| monday.com internal architecture is not public | All parity claims are behavioural; performance parity is not claimed | Stated in [01](01-Requirements.md) and here |
| The Workspaces support article returned 404 at research time | Workspace semantics rest on the board-types and user-types articles [S9][S5] plus assumption | Re-verify before P1; the model is simple enough that the risk is low |
| No public specification of their automation semantics beyond trigger/condition/action | D5's engine is our design, not a reproduction | The DSL is documented as ours ([06](06-Integrations-and-Extensibility.md) §2) |
| No public search behaviour specification | Search is an assumption ([01](01-Requirements.md) FR-4.11) | Validate with users in P4 |
| Pricing figures are a point-in-time snapshot | Quotas may drift | Re-check at each release; quotas are configuration, not code |

---

## 8. Risks we are explicitly accepting

Stated so they are not mistaken for oversights:

1. **Last-write-wins editing** will occasionally lose a concurrent edit, with a notice. CRDT is out of scope.
2. **Two hosting paths** double the deployment test surface, accepted for on-prem reach.
3. **A GPL/commercial MySQL driver** is in the dependency tree, accepted with a budget line.
4. **No marketplace** will not satisfy every integrator, accepted for scope control.
5. **Depth-2 subitems** will be requested on day one, accepted for 1.0.

---

**Next:** [10-Acceptance-Checklists.md](10-Acceptance-Checklists.md).
