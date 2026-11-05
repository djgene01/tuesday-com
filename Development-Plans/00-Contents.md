# Tuesday.com — Development & Implementation Manuals

**Master index.** A clean-room, .NET implementation of a work-operating-system in the behavioural shape of monday.com.

| | |
|---|---|
| **Product codename** | Tuesday.com |
| **Documentation status** | Baseline, version 1.0 |
| **Compiled** | 3 November 2026 |
| **Target stack** | .NET 10 LTS, ASP.NET Core, Blazor Web App, EF Core 10 + MySQL 8.4 |
| **Hosting decision** | On-premises: Docker Compose primary, Windows Server + IIS secondary |
| **Scope of this set** | Documentation only — no application code, no source listings |
| **Clean-room posture** | Behavioural parity only. No monday.com source, assets, trademarks or trade dress |

---

## 1. How to read this set

Read in numeric order the first time. After that, each manual stands alone and links to its neighbours.

1. **[01-Requirements.md](01-Requirements.md)** — what we are building, what we are not, and the parity matrix that anchors every later claim.
2. **[02-Architecture.md](02-Architecture.md)** — the shape of the system: modules, tenancy, identity, eventing, the automation engine, real-time, API.
3. **[03-Data-Model.md](03-Data-Model.md)** — the MySQL schema, the dynamic-column strategy, indexing, migrations.
4. **[04-UI-and-UX-Plan.md](04-UI-and-UX-Plan.md)** — Blazor render-mode boundaries, routes, component boundaries, JS interop, visual-parity method.
5. **[05-Security-and-Compliance.md](05-Security-and-Compliance.md)** — authN/authZ, tenant isolation, audit, OWASP mapping, secrets, clean-room guardrails.
6. **[06-Integrations-and-Extensibility.md](06-Integrations-and-Extensibility.md)** — webhooks, public API, automation DSL, plugin contract.
7. **[07-Testing-Strategy.md](07-Testing-Strategy.md)** — unit through performance and accessibility, plus per-phase verification hooks.
8. **[08-Deployment-and-Operations.md](08-Deployment-and-Operations.md)** — on-prem topology, CI/CD, observability, backup, DR, rollback.
9. **[09-Risks-and-Open-Questions.md](09-Risks-and-Open-Questions.md)** — technical, legal and product risk; decision log; unresolved questions.
10. **[10-Acceptance-Checklists.md](10-Acceptance-Checklists.md)** — per-phase and overall acceptance gates.
11. **[11-Clean-Room-Protocol.md](11-Clean-Room-Protocol.md)** — the working rules that keep the implementation defensible.
12. **[12-Phase-Plan-Summary.md](12-Phase-Plan-Summary.md)** — human-readable view of the machine plan.
13. **[13-Source-Register.md](13-Source-Register.md)** — every citation with URL, access date and what it actually supports.

The machine-readable plan is **[phase-plan.json](phase-plan.json)**. It is the only non-Markdown file in this folder apart from the rendered diagrams.

---

## 2. Document-to-phase map

Phases are defined operationally in [phase-plan.json](phase-plan.json) and summarised in [12-Phase-Plan-Summary.md](12-Phase-Plan-Summary.md).

| Phase | Name | Primary manuals | Slice diagram |
|---|---|---|---|
| **P0** | Foundations & walking skeleton | [02](02-Architecture.md), [08](08-Deployment-and-Operations.md), [07](07-Testing-Strategy.md) | [P0 slice](Diagrams/p0-foundations-slice.pdf) |
| **P1** | Identity, tenancy, workspaces | [02](02-Architecture.md), [05](05-Security-and-Compliance.md) | [P1 slice](Diagrams/p1-identity-slice.pdf) |
| **P2** | Boards, groups, columns, item grid | [03](03-Data-Model.md), [04](04-UI-and-UX-Plan.md) | [P2 slice](Diagrams/p2-board-grid-slice.pdf) |
| **P3** | Views: Kanban, Calendar, Timeline, Gantt | [03](03-Data-Model.md), [04](04-UI-and-UX-Plan.md) | [P3 slice](Diagrams/p3-views-slice.pdf) |
| **P4** | Collaboration: updates, notifications, search | [02](02-Architecture.md), [06](06-Integrations-and-Extensibility.md) | [P4 slice](Diagrams/p4-collaboration-slice.pdf) |
| **P5** | Automations, webhooks, public API | [06](06-Integrations-and-Extensibility.md), [05](05-Security-and-Compliance.md) | [P5 slice](Diagrams/p5-automation-slice.pdf) |
| **P6** | Dashboards, hardening, on-prem release | [08](08-Deployment-and-Operations.md), [10](10-Acceptance-Checklists.md) | [P6 slice](Diagrams/p6-release-slice.pdf) |

---

## 3. Diagram index

All diagrams are generated from editable Mermaid sources and exported to PDF (primary) with PNG previews. Sources (`.mmd`) sit next to the exports and are the authoritative version.

| # | Diagram | Source | Used by |
|---|---|---|---|
| 1 | System context | [.mmd](Diagrams/01-system-context.mmd) · [PDF](Diagrams/01-system-context.pdf) | [02](02-Architecture.md) |
| 2 | Module & layer view | [.mmd](Diagrams/02-module-layers.mmd) · [PDF](Diagrams/02-module-layers.pdf) | [02](02-Architecture.md) |
| 3 | Request lifecycle | [.mmd](Diagrams/03-request-lifecycle.mmd) · [PDF](Diagrams/03-request-lifecycle.pdf) | [02](02-Architecture.md), [04](04-UI-and-UX-Plan.md) |
| 4 | Data model (ER) | [.mmd](Diagrams/04-data-model-er.mmd) · [PDF](Diagrams/04-data-model-er.pdf) | [03](03-Data-Model.md) |
| 5 | Automation engine flow | [.mmd](Diagrams/05-automation-engine-flow.mmd) · [PDF](Diagrams/05-automation-engine-flow.pdf) | [06](06-Integrations-and-Extensibility.md) |
| 6 | Deployment topology | [.mmd](Diagrams/06-deployment-topology.mmd) · [PDF](Diagrams/06-deployment-topology.pdf) | [08](08-Deployment-and-Operations.md) |
| 7 | Render-mode boundaries | [.mmd](Diagrams/07-render-mode-boundaries.mmd) · [PDF](Diagrams/07-render-mode-boundaries.pdf) | [04](04-UI-and-UX-Plan.md) |
| 8 | Permission model | [.mmd](Diagrams/08-permission-model.mmd) · [PDF](Diagrams/08-permission-model.pdf) | [05](05-Security-and-Compliance.md) |
| 9 | Phase dependencies | [.mmd](Diagrams/09-phase-dependencies.mmd) · [PDF](Diagrams/09-phase-dependencies.pdf) | [12](12-Phase-Plan-Summary.md) |
| 10–16 | Per-phase slices (P0–P6) | see table in §2 | each phase |

---

## 4. Decisions already taken

These were settled with the product owner on 3 November 2026. They are binding on every manual; changing one requires a revision of this set.

| ID | Decision | Rationale | Consequence |
|---|---|---|---|
| **D1** | **.NET 10 LTS** | Only supported stable runtime: GA 11 Nov 2025, current patch 10.0.14, support to 14 Nov 2028 [S15]. .NET 11 is RC3 with GA 10 Nov 2026 and is STS; .NET 8 and 9 both leave support on 10 Nov 2026 [S15]. | Framework pin is `net10.0`; .NET 11 adoption is a documented future gate, not a baseline. |
| **D2** | **EF Core 10.0.14 + PostgreSQL 17**, via Npgsql.EntityFrameworkCore.PostgreSQL 10.0.0 | Superseded the original MySQL choice: Oracle's `MySql.EntityFrameworkCore` is GPLv2-with-FOSS-exception and Pomelo has no EF Core 10 build. Npgsql is MIT and the typed projection layer was already provider-neutral. | No licence action required. |
| **D3** | **Blazor Web App**: Interactive Server for board surfaces, static SSR for auth/marketing/admin shells | The board grid is dense and stateful; SignalR is required regardless for presence and live updates. SSR keeps auth pages without a runtime download. Render modes verified against Microsoft docs [S16]. | No WebAssembly payload for the grid. Data Protection keys must be shared across instances or Blazor circuits break on scale-out — see [08](08-Deployment-and-Operations.md) §6.3. |
| **D4** | **On-premises**: Docker Compose primary, Windows Server + IIS secondary | Owner infrastructure. | Self-managed MySQL, Redis backplane, TLS, backups, DR. No managed identity — secrets are operator-supplied. |
| **D5** | **Automations**: core recipes + multi-action + scheduled triggers; **no third-party marketplace** | Matches the public trigger→condition→action model with plan quotas [S6][S1] without marketplace economics. | Documented plugin contract only; no app review process, no revenue share, no sandbox runtime. |
| **D6** | **Clean-room discipline** | Legal defensibility. | Behavioural observation from public documentation and marketing only. No assets, strings, CSS, icons, fonts or trade dress copied. See [11](11-Clean-Room-Protocol.md). |

### Assumptions (not decisions)

Labelled `A#` throughout. These are ours, reversible, and must be re-checked before the phase that depends on them.

| ID | Assumption |
|---|---|
| **A1** | Single-tenant-per-deployment is the default commercial shape; multi-tenant shared-database is supported but not the primary on-prem mode. |
| **A2** | Typical board size in year one is ≤ 20,000 items; the design must stay correct at 100,000 with pagination and must not be optimised past that without measurement. |
| **A3** | Concurrency target is 200 simultaneous interactive users per deployment, 50 per board. |
| **A4** | All timestamps stored UTC; date-only columns are calendar dates, not instants. |
| **A5** | English-only UI at launch; string externalisation is designed in but translations are out of scope. |
| **A6** | File attachments stored on a filesystem volume or S3-compatible endpoint, not in MySQL. |
| **A7** | Email is the only notification channel at launch; SMTP is operator-configured. |
| **A8** | No AI features in the documented scope, despite their prominence on the public site [S1]. |
| **A9** | Public API is REST/JSON with cursor pagination; GraphQL is a later, separately-decided option (see [09](09-Risks-and-Open-Questions.md) Q2). |
| **A10** | Operators run MySQL 8.4 with InnoDB, `utf8mb4`, and binary logging enabled. |

---

## 5. Glossary

| Term | Meaning in this set |
|---|---|
| **Account / Tenant** | The billing and isolation boundary. One deployment may host several. |
| **Workspace** | A folder-like container for boards with its own visibility (open, closed, private). |
| **Board** | The unit of structure: columns, groups, items, views. Types mirror the public model — main, shareable, private [S9]. |
| **Group** | A coloured band of items inside a board; the unit of collapse, summary and reorder. |
| **Item** | A row. Holds one value per board column, plus a name and position. |
| **Subitem** | A child row living on an implicit child board with **its own** column structure [S8]. |
| **Column** | A typed field definition applied board-wide; 500 per board maximum [S3]. |
| **Column value** | The per-item payload for a column, stored as JSON with typed projections — see [03](03-Data-Model.md) §4. |
| **View** | A saved presentation of one board: table, kanban, calendar, timeline, gantt and others; 100 per board maximum [S7]. |
| **Handshake** | The challenge/response verification an endpoint must complete before receiving webhooks [S12]. |
| **Recipe** | One automation definition: trigger, conditions, one or more ordered actions [S6]. |
| **Effective permission** | The resolved union of tenant role, workspace membership, board grant and item scope. |
| **Render mode** | Blazor's per-component decision of where and whether interactivity runs [S16]. |
| **Slice** | The smallest end-to-end increment that is runnable and observable on its own. |

---

## 6. Conventions used in these manuals

- **Traceability tags.** `[S#]` = source in [13-Source-Register.md](13-Source-Register.md). `[D#]` = decision in §4. `[A#]` = assumption in §4. Every requirement carries at least one.
- **"Decided" vs "Assumed" vs "Open".** Never blurred. Open items live in [09](09-Risks-and-Open-Questions.md) with an owner and a deadline.
- **No code listings.** Type and member names appear in prose or tables, never as C# source.
- **Commands are future tense.** Every command in these manuals is something to run later, by an implementer, in a build environment. None has been executed as part of this documentation.
- **Limits are quoted, not invented.** Where a number appears (500 columns, 100 views, 1,000 API calls/day) it is cited.

---

## 7. Status of this documentation

| Item | State |
|---|---|
| Research against public monday.com surfaces | Complete for the surfaces listed in [01](01-Requirements.md) §3 |
| Package research and licence review | Complete; versions verified against the NuGet registration API on 3 Nov 2026 [S19] |
| Diagrams rendered and visually checked | See §3 — PDF + PNG + `.mmd` |
| Application code written | **None, by design** |
| Commands executed | **None, by design** |
| Workloads installed | **None, by design** |

Known documentation gaps are listed in [09](09-Risks-and-Open-Questions.md) §6. The largest is that monday.com's internal architecture is not public, so every parity claim here is behavioural.
