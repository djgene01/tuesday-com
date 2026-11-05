# 13 — Source Register

**Purpose.** Every citation in this set, with what it actually supports and — as importantly — what it does not.

**All sources accessed:** 3 November 2026.
**Index:** [00-Contents.md](00-Contents.md)

---

## 1. How this register is used

A requirement cites `[S#]`. This register says whether that citation is honest. The **"Does not support"** column exists so that a later reader cannot stretch a source further than it goes — the most common failure in a document set like this one.

Two source classes carry different weight:

| Class | Examples | Weight |
|---|---|---|
| **Primary product documentation** | monday.com support articles, API reference | Authoritative for behaviour, at the date accessed |
| **Framework and package registries** | Microsoft Learn, NuGet registration API | Authoritative for versions, licences, documented mechanics |

Neither class tells us how the product is *built*. That gap is stated in [09](09-Risks-and-Open-Questions.md) §7 and is not papered over anywhere in this set.

---

## 2. monday.com product and API sources

### S1 — Pricing and plan tiers

| | |
|---|---|
| **URL** | https://monday.com/pricing |
| **Accessed** | 2026-11-03 |
| **Supports** | Tier structure (Free, Basic, Standard, Pro, Enterprise, custom); feature gating by tier; automation and integration action quotas (250 / 25,000 / up to 250,000 per month); API daily call allowances by tier (1,000 Standard, 10,000 Pro); unlimited items from Basic; unlimited free viewers; 3 boards and 8 column types on Free; AI credits and AI features appearing from Basic |
| **Does not support** | Any statement about implementation, data model, or how quotas are enforced. No technical detail whatsoever. Prices are a point-in-time snapshot and drift |

Used for: FR-1.7, FR-4.6, FR-5.2, NFR context, [09](09-Risks-and-Open-Questions.md) §4.1.

### S2 — Column types reference

| | |
|---|---|
| **URL** | https://developer.monday.com/api-reference/reference/column-types-reference |
| **Accessed** | 2026-11-03 |
| **Supports** | The 39-type taxonomy: 29 writable, 6 read-only (`creation_log`, `formula`, `item_id`, `last_updated`, `mirror`, `progress`), 1 calculated (`auto_number`), 2 deprecated (`person`, `team`), 1 integration-managed; per-type API identifiers and implementation type names; read-only columns cannot be written; calculated columns are not exposed through column values |
| **Does not support** | Rendering behaviour, storage strategy, validation rules, or how derived columns are computed |

Used for: FR-1.11, FR-1.12, FR-1.13, the whole of §3.2 in [01](01-Requirements.md), [03](03-Data-Model.md) §4.3.

### S3 — Basics of columns

| | |
|---|---|
| **URL** | https://support.monday.com/hc/en-us/articles/115005466609-The-basics-of-columns |
| **Accessed** | 2026-11-03 |
| **Supports** | **500 columns per board** limit; a column applies to the entire board and cannot be added to a single item or group; per-column description; column summaries at group footer for certain types; column menu affordances |
| **Does not support** | What happens at the limit (error shape), summary formulas per type, or ordering semantics |

Used for: FR-1.4, FR-1.5, FR-1.6, U-4, U-11, VP-08.

### S4 — The Kanban view

| | |
|---|---|
| **URL** | https://support.monday.com/hc/en-us/articles/360000661379-The-Kanban-View |
| **Accessed** | 2026-11-03 |
| **Supports** | Cards are generated from **status column labels only**, with an explicit note that other column types are not yet supported; cover columns with crop/fit; card columns selectable, including subitem columns; "divide by" group/status/relation; hide empty lanes; per-lane add; card action menu; overview navigator |
| **Does not support** | WIP limits, swimlane ordering rules, or performance characteristics |

Used for: FR-3 Kanban, VP-03, VP-04, [03](03-Data-Model.md) §7.2.

### S5 — User types explained

| | |
|---|---|
| **URL** | https://support.monday.com/hc/en-us/articles/360002144900-User-types-explained |
| **Accessed** | 2026-11-03 |
| **Supports** | Four roles — Admin, Member, Viewer, Guest; explicit viewer denials including **no API use**, no board creation, no content edits, no board ownership; viewers unlimited and unbilled; guests scoped to invited boards; admin is account-level |
| **Does not support** | Board-level permission granularity, or what a guest sees inside an item |

Used for: FR-6 roles, the capability matrix in [05](05-Security-and-Compliance.md) §3.2.

### S6 — Get started with automations

| | |
|---|---|
| **URL** | https://support.monday.com/hc/en-us/articles/360001222900-Get-started-with-monday-automations |
| **Accessed** | 2026-11-03 |
| **Supports** | The **trigger → condition → action** model, stated explicitly; multiple conditions; multiple actions chained on one recipe; trigger kinds including column change, item created, "every time period", "when a date arrives"; deactivate vs permanent delete (deleted cannot be restored); importance ranking |
| **Does not support** | The full action vocabulary, execution ordering guarantees, retry semantics, or concurrency behaviour |

Used for: FR-4.1 to FR-4.5, VP for automations, [06](06-Integrations-and-Extensibility.md) §2.

### S7 — The board views

| | |
|---|---|
| **URL** | https://support.monday.com/hc/en-us/articles/360001267945-The-board-views |
| **Accessed** | 2026-11-03 |
| **Supports** | Enumerated view list (Blank, Table, Gantt, Timeline, Files Gallery, Map, Workload, Chart, Calendar, Form, Kanban, Cards, Pivot); **100 views per board** limit; views added via a tab strip under the board title; widget names for dashboards (Table, Gantt, Calendar, Battery, Chart, Numbers) |
| **Does not support** | Per-view behaviour detail (each has its own article), or view configuration storage |

Used for: FR-3.3, P3-7, VP-07, dashboard widget naming in P6.

### S8 — All about subitems

| | |
|---|---|
| **URL** | https://support.monday.com/hc/en-us/articles/360011905480-All-about-subitems |
| **Accessed** | 2026-11-03 |
| **Supports** | Subitems have **their own column structure**, separate from parents, shared across all subitems on the board; "copy parent columns" is a one-shot copy with **no ongoing sync**; automation-created subitems can prefill from parent **at creation only**; expand/collapse from the parent row; duplicating a parent duplicates subitems |
| **Does not support** | Depth limits (a separate article covers multi-level), permission inheritance for subitems |

Used for: FR-1.8, FR-1.9, U-12, [03](03-Data-Model.md) §3.3, P2-7.

### S9 — The difference between board types

| | |
|---|---|
| **URL** | https://support.monday.com/hc/en-us/articles/115005311105-The-difference-between-board-types |
| **Accessed** | 2026-11-03 |
| **Supports** | Three board types — main, shareable, private; **structure is identical, only access differs**; main boards visible to all team members; shareable boards used for guests and available on Standard and above; private boards for restricted visibility |
| **Does not support** | Workspace interaction rules, or permission inheritance between board and workspace |

Used for: FR-1.2, [02](02-Architecture.md) §5, [05](05-Security-and-Compliance.md) §3.2.

### S10 — API basics

| | |
|---|---|
| **URL** | https://developer.monday.com/api-reference/docs/basics.md |
| **Accessed** | 2026-11-03 |
| **Supports** | The API is GraphQL; it is part of an apps framework; it supports operations over boards, items, column values, users and workspaces; stated use cases include custom reports on dashboards |
| **Does not support** | REST equivalents, pagination semantics, or authorisation model detail |

Used for: FR-5.1. **Note:** the decision to expose REST [A9] is *ours*, and this source is cited only for entity coverage, never as precedent for the transport.

### S11 — Rate limits

| | |
|---|---|
| **URL** | https://developer.monday.com/api-reference/docs/rate-limits.md |
| **Accessed** | 2026-11-03 |
| **Supports** | Six limit kinds (complexity, daily, minute, concurrency, IP, resource protection); **5,000,000 complexity points** per individual query; per-minute budgets (5M read + 5M write for app tokens, 10M combined for personal tokens, 1M for free/trial); **daily call limits — Free 200, Basic/Standard 1,000, Pro/Trial 10,000, Enterprise 25,000**, resetting at midnight UTC; exceptions table including that rate-limited requests cost 0.1 calls; every rate-limit error returns `retry_in_seconds` |
| **Does not support** | The complexity formula itself (separate page), or per-endpoint costs |

Used for: FR-5.2 to FR-5.7, [02](02-Architecture.md) §9.2, P5-12.

### S12 — Webhooks

| | |
|---|---|
| **URL** | https://developer.monday.com/api-reference/docs/webhooks.md |
| **Accessed** | 2026-11-03 |
| **Supports** | Webhooks deliver HTTP POST on board events; registration requires a **challenge/response handshake** — the platform POSTs a JSON body with a `challenge` field and expects the same value echoed back; webhooks are configured through a recipe in the automations centre |
| **Does not support** | Signature scheme, retry policy, or payload schema — none of which are claimed as parity, our contract is our own ([06](06-Integrations-and-Extensibility.md) §3.2) |

Used for: FR-4.7, P5-8, [06](06-Integrations-and-Extensibility.md) §3.1.

### S13 — Apps framework introduction

| | |
|---|---|
| **URL** | https://developer.monday.com/apps/docs/intro.md |
| **Accessed** | 2026-11-03 |
| **Supports** | App feature categories: board views, item views, board menu features, dashboard widgets, custom objects, account settings views, doc actions, AI assistant, integrations, workspace templates; apps are technology-agnostic; any developer can build |
| **Does not support** | Review process, sandboxing, or distribution economics — all out of scope under D5 |

Used for: context only. **No requirement in this set depends on it**, because D5 excludes the marketplace. It is registered so the exclusion is an informed one.

### S14 — API documentation index

| | |
|---|---|
| **URL** | https://developer.monday.com/api-reference/llms.txt |
| **Accessed** | 2026-11-03 |
| **Supports** | Existence and titles of: authentication, versioning, rate limits, idempotency, error handling, release notes, MCP server, and the platform MCP tool set |
| **Does not support** | Content of those pages (read separately as S11, S25, S26) |

Used for: navigation and scope confirmation only.

---

## 3. .NET platform sources

### S15 — .NET support policy

| | |
|---|---|
| **URL** | https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core |
| **Accessed** | 2026-11-03 |
| **Supports** | .NET 10: released **11 Nov 2025**, current patch **10.0.14 (25 Oct 2026)**, **LTS, supported to 14 Nov 2028**; .NET 9: STS, maintenance, **ends 10 Nov 2026**; .NET 8: LTS, maintenance, **ends 10 Nov 2026**; .NET 11 **RC3 released 25 Oct 2026** with GA scheduled 10 Nov 2026 |
| **Does not support** | Whether a given workload or SDK feature is installed on any particular machine — treated as an open prerequisite, never assumed |

Used for: D1, [02](02-Architecture.md) §11.1, [09](09-Risks-and-Open-Questions.md).

### S16 — Blazor render modes

| | |
|---|---|
| **URL** | https://learn.microsoft.com/en-us/aspnet/core/blazor/components/render-modes?view=aspnetcore-10.0 |
| **Accessed** | 2026-11-03 |
| **Supports** | The four render modes and their meaning; a Blazor Web App must be configured with `AddInteractiveServerComponents` and `AddInteractiveServerRenderMode`; render mode is applied per component or at `Routes`, and **the root `App` component cannot be interactive**; `HeadOutlet` must match; components prerender by default; standalone WebAssembly has no render-mode concept |
| **Does not support** | Performance characteristics, or which mode suits a given UI — that judgement is ours (D3) |

Used for: D3, the whole of [04](04-UI-and-UX-Plan.md) §1.

### S17 — SignalR scale-out

| | |
|---|---|
| **URL** | https://learn.microsoft.com/en-us/aspnet/core/signalr/scale?view=aspnetcore-10.0 |
| **Accessed** | 2026-11-03 |
| **Supports** | SignalR requires **sticky sessions** on a server farm; the **Redis backplane is the recommended scale-out approach for apps on your own infrastructure**; sticky sessions are still required even when a backplane is used, except in three specific cases (Azure SignalR Service, WebSocket-only with skip negotiation, single server); scale-out may also be needed to bound connections per server |
| **Does not support** | Capacity numbers for a given workload |

Used for: [02](02-Architecture.md) §8, [08](08-Deployment-and-Operations.md) §2.3, P6-6.

### S18 — Host ASP.NET Core on IIS

| | |
|---|---|
| **URL** | https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0 |
| **Accessed** | 2026-11-03 |
| **Supports** | IIS integration via the ASP.NET Core Module; the app runs in a process separate from `w3wp.exe`; request flow through HTTP.sys and ANCM; process management on first request |
| **Does not support** | WebSocket feature enablement specifics or ARR affinity configuration — those come from Windows/IIS documentation and are treated as operator prerequisites ([08](08-Deployment-and-Operations.md) §5) |

Used for: D4 secondary path, [08](08-Deployment-and-Operations.md) §5.

### S27 — What's new in .NET 10

| | |
|---|---|
| **URL** | https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/overview |
| **Accessed** | 2026-11-03 |
| **Supports** | .NET 10 is an **LTS release supported for three years**, successor to .NET 9 |
| **Does not support** | Feature-by-feature detail relevant to this design |

Used for: corroborating D1 alongside S15.

---

## 4. Package and licence sources

### S19 — NuGet registration API

| | |
|---|---|
| **URL** | `https://api.nuget.org/v3/registration5-semver1/{package-id}/index.json` (queried per package) |
| **Accessed** | 2026-11-03 |
| **Supports** | Published versions, publication dates, `licenseExpression` where declared, and dependency groups. Verified values used in this set include: EF Core **10.0.14** (MIT); **Npgsql.EntityFrameworkCore.PostgreSQL 10.0.0 (MIT, PostgreSQL provider — system of record)**; Dapper 2.1.89 (Apache-2.0); OpenIddict 7.7.2 (Apache-2.0); MudBlazor 9.12.0 (MIT); Radzen.Blazor 12.1.10 (MIT); Quartz 4.7.0 (Apache-2.0); FluentValidation 12.2.0 (Apache-2.0); Mapperly 4.3.1 (Apache-2.0); MySqlConnector 2.6.2 (MIT); MySql.EntityFrameworkCore **10.0.12** (licence by URL, GPLv2 + FOSS exception — superseded); Pomelo.EntityFrameworkCore.MySql **9.0.0** (MIT, latest stable); Microsoft.AspNetCore.SignalR.StackExchangeRedis 10.0.14 (MIT); Polly 8.9.0 (BSD-3-Clause); Testcontainers.MySql 4.16.0 (MIT); Microsoft.Playwright 1.64.1 (MIT); xunit.v3 4.0.2 (Apache-2.0); FluentAssertions 8.11.0 (licence by URL; **last permissive 7.2.2**) |
| **Does not support** | The full text of non-SPDX licences — those were read directly (S21–S24) |

Used for: every version and licence claim in [02](02-Architecture.md) §11, [05](05-Security-and-Compliance.md) §9, [07](07-Testing-Strategy.md).

### S20 — Pomelo EF Core 10 tracking issue

| | |
|---|---|
| **URL** | https://github.com/PomeloFoundation/Pomelo.EntityFrameworkCore.MySql/issues/2007 |
| **Accessed** | 2026-11-03 |
| **Supports** | Issue "EF Core 10 support" is **open**, assigned, labelled `type-update`, milestone **10.0.0**, opened 17 Aug 2025; no 10.x release exists |
| **Does not support** | Any delivery date. Milestones slip; the register records the state at access, not a promise |

Used for: D2 rationale, [02](02-Architecture.md) §11.2, [09](09-Risks-and-Open-Questions.md) §2.3.

### S21 — MediatR licence

| | |
|---|---|
| **URL** | https://www.nuget.org/packages/MediatR/14.2.0/license |
| **Accessed** | 2026-11-03 |
| **Supports** | MediatR 14 is licensed under **RPL-1.5** or a paid commercial licence from Lucky Penny Software; RPL requires releasing source of software built with it |
| **Does not support** | Which earlier versions were permissive — that came from S19 (last Apache-2.0: 12.5.0) |

Used for: [02](02-Architecture.md) §11.2 rejection.

### S22 — Hangfire licence

| | |
|---|---|
| **URL** | https://www.nuget.org/packages/Hangfire.Core/1.8.25/license |
| **Accessed** | 2026-11-03 |
| **Supports** | Hangfire is dual-licensed **LGPL-3.0** or commercial; commercial is required to distribute private forks and modifications |
| **Does not support** | Whether LGPL is acceptable for a given distribution model — that is a legal judgement |

Used for: scheduler choice in [02](02-Architecture.md) §7.3.

### S23 — AutoMapper licence

| | |
|---|---|
| **URL** | https://www.nuget.org/packages/AutoMapper/16.2.0/license |
| **Accessed** | 2026-11-03 |
| **Supports** | Same RPL-1.5 / commercial terms as S21 |
| **Does not support** | Last permissive version — from S19 (14.0.0, MIT) |

Used for: Mapperly selection, [02](02-Architecture.md) §11.2.

### S24 — Duende IdentityServer licence

| | |
|---|---|
| **URL** | https://www.nuget.org/packages/Duende.IdentityServer/8.0.10/license |
| **Accessed** | 2026-11-03 |
| **Supports** | Duende products are governed by a commercial licence agreement at duendesoftware.com/license |
| **Does not support** | Revenue thresholds or free-tier terms — not relied upon; OpenIddict was chosen regardless |

Used for: identity server choice, [02](02-Architecture.md) §11.2.

---

## 5. Additional monday.com API sources

### S25 — Idempotency

| | |
|---|---|
| **URL** | https://developer.monday.com/api-reference/docs/idempotency.md |
| **Accessed** | 2026-11-03 |
| **Supports** | `Idempotency-Key` header on mutations; first request executes and **caches the response for 30 minutes**; a retry with the same key returns the cached response with `Idempotency-Replayed: true`; a concurrent duplicate returns **409 Conflict** with `Retry-After` |
| **Does not support** | Storage design, or key scoping rules |

Used for: FR-5.5, [02](02-Architecture.md) §9.3, P5-11.

### S26 — API versioning

| | |
|---|---|
| **URL** | https://developer.monday.com/api-reference/docs/api-versioning.md |
| **Accessed** | 2026-11-03 |
| **Supports** | A new version is released **every quarter**; at least **three versions in parallel**; three version types (release candidate, current, maintenance); each version is stable for **at least six months**; current version at access time: 2026-10 |
| **Does not support** | Deprecation mechanics for consumers |

Used for: FR-5.6, contract rules in [06](06-Integrations-and-Extensibility.md) §4.2.

---

## 6. Sources attempted and rejected

Recorded so the gap is visible rather than silently filled.

| Attempted | Result | Consequence |
|---|---|---|
| `support.monday.com/.../Workspaces-on-monday-com` | **404 — page not found** | Workspace visibility semantics rest on S9 (board types), S5 (roles) and assumption A1. Workspace open/closed/private is modelled from the observed board-type pattern, not from a direct source. Flagged in [09](09-Risks-and-Open-Questions.md) §7 |
| Search behaviour | No specification retrieved | Search is an assumption ([01](01-Requirements.md) FR-4.11), validated with users in P4 |
| Automation action vocabulary | Not enumerated in S6 | The action set in [06](06-Integrations-and-Extensibility.md) §2.4 is **our design**, not a reproduction, and is documented as such |
| Internal architecture of monday.com | Not public | No architectural parity is claimed anywhere in this set |

---

## 7. Citation hygiene

| Rule | Reason |
|---|---|
| Cite the page that states the fact, not the site | A pricing page cannot support an automation claim |
| Record the access date | Product documentation changes; the claim is true at a date |
| Record what the source does **not** support | Prevents later over-claiming, which is how documentation sets lose credibility |
| Never cite a screenshot or an asset | Those are not sources; under [11](11-Clean-Room-Protocol.md) they are prohibited artefacts |
| Prefer primary documentation over secondary commentary | Third-party reviews were read for context and are **not** cited for any requirement |

No third-party blog, review site or forum post is cited for a requirement in this set. Where they informed context — for example, the industry-wide awareness of the Pomelo gap — the actual claim rests on the primary source (S20).

---

**Return to:** [00-Contents.md](00-Contents.md)
