# Release notes — Tuesday.com 0.9.9 (release candidate)

**Status:** release candidate for an internal work-tracking deployment. Not 1.0.
**Date:** 2026-11-05 · **Version:** 0.9.9 · **Image tag:** `tuesday-web:0.9.9`

Changes since 0.9.8:

- **A real marketing home page.** The landing page now carries the shape a buyer expects — sticky
  header, hero with a product image and floating proof chips, live-status strip, six use-case cards,
  alternating feature rows (updates, automations, self-hosting), a closing call to action and a
  four-column footer — inside the Digital Wellness language. Copy is this repository's own; the
  composition is standard SaaS, not another product's assets.
- The public shell is its own layout (`MarketingLayout`), so visitors never see the signed-in app
  rail. The signed-in product still uses `MainLayout`.
- Three illustrations generated locally with the self-hosted FLUX endpoint, stored in
  `wwwroot/img/` (no third-party image host, no licence ambiguity).
- Horizontal overflow fixed on a 390 px viewport: decorative blobs are desktop-only, the compose
  snippet wraps instead of forcing its grid wider, and `html/body` clip stray decoration.
- axe stays clean on all twelve routes after the rebuild.

Changes since 0.9.4:

- **Digital Wellness visual language.** Warm paper canvas `#FDFCF8`, soft-black `#292524`, sage and
  lavender surfaces, peach `#FFB7B2` behind dark text and a deeper coral `#A8554E` wherever the accent
  has to carry words (peach is 1.6:1 — it is never used for text). Outfit for structure, Reenie
  Beanie for a single hand-written aside, 2rem containers, `0 4px 20px -2px` shadows, a 35% fractal
  grain overlay, reveal-on-scroll and floating blobs — all disabled under `prefers-reduced-motion`.
- **Viewers can no longer write (P6-19).** The grid's add row was ungated and every service method
  checked only tenant binding; role checks now live in `ItemEditor` and `ColumnValueWriter`.
- axe remains clean on all twelve routes after the palette change.

Changes since 0.9.3:

- **Accessibility: axe is clean.** All twelve primary routes (landing, login, workspace, board,
  dashboard, item, sub-item columns, search, notifications, security, tokens, 404) have **zero**
  WCAG 2.0/2.1 A+AA violations, down from serious colour-contrast failures on four of them.
- **Brand colour darkened** from `#6161ff` to `#5252e0`: 4.2:1 against the canvas at 12 px was below
  AA, and 4.5:1 was exactly the limit under white button text. The new hue measures 5.4:1 and 5.8:1.
- The sub-item column editor's "Remove" control moved from `ink-faint` to `ink-soft` (4.4:1 → 4.8:1).
- `scripts/a11y/axe.js` is now in the repo: it sweeps every route, writes `artifacts/axe.json`, and
  exits non-zero on any serious or critical finding.
- **Invitations** (0.9.3 had the code but not the release): single-use `tue_inv_` links stored only as
  a SHA-256 hash, 7-day expiry, invitee-chosen password at `/invite/{token}`, link shown once to the
  admin when email is disabled (P6-18 closed).
- Verified inside the Production container: sign-in as the bootstrap admin, invite
  `quinn@tuesday.local`, accept the link, sign in as that user (200 `/workspace`, Viewer role).

---


---

## What works

| Area | State |
|---|---|
| Identity | Cookie sign-in, TOTP second factor with recovery codes, OpenIddict token server (`/connect/token`), API tokens, bearer auth on `/api/v1` |
| Tenancy | Tenant-scoped query filters; admin/member/guest roles; cross-tenant isolation suite green |
| Boards | Groups, columns, 500-item grids, inline edit, attachments, item detail |
| Views | Kanban, calendar, timeline, Gantt |
| Collaboration | Threaded updates with HTML sanitisation, likes, reply/edit/delete, notifications, email preference, in-app search |
| Automations | ordered multi-action chains, depth-3 loop guard | Trigger → condition → action rules; scheduled date-passed sweep with per-item-per-day dedupe |
| Agent access | MCP stdio server (`src/Tuesday.Mcp`) with four tools and a Claude Code skill |
| Integrations | Signed webhooks with challenge handshake, REST API with cursor pagination, idempotency keys, per-token quota and a committed OpenAPI snapshot |
| Dashboard | Per-board counts, by-status and by-group breakdown, automation run history |
| Ops | `/health/live`, `/health/ready`, correlation ids, security headers, Docker image, Compose stack |

**Verification at this tag:** 110 tests green (59 unit, 38 isolation, 13 integration), Release build
with zero warnings, container healthy in Production behind the deployment path in
`docs/runbooks/signing-certificate.md`.

---

## Known limitations

Stated plainly because they are real. Each maps to an open item in
`Development-Plans/10-Acceptance-Checklists.md` §8.

1. **The item grid is virtualised.** A 500-item board keeps ~37 rows in the DOM. Measured on
   this hardware against the fixture scripts in `scripts/perf`: 5,000 items give p50 105 ms /
   p95 136 ms for the full page render; a 100,000-item board gives p50 1.72 s / p95 1.77 s.
   The 100k figure is a single-user render checkpoint, not a concurrent-load result.
2. **Sub-items are one level deep.** Expansion, creation, ordering and cell editing work inline.
   Sub-item columns are owned by a per-board sub-item surface: they can be copied from the
   parent board once (a one-way copy), then added, renamed, reordered and deleted at
   `/board/{id}/subitems`. Deleting a column deletes its values after an explicit confirmation.
3. **Grid drag is pointer-only for cross-group moves.** Alt+Arrow moves a row within its
   group and is the keyboard path; dragging across groups has no keyboard equivalent yet.
4. **No load test has been run.** The 200-user / 5,000-item targets in
   `01-Requirements.md` §4 are **unmeasured**, not met.
5. **Single web instance only.** No second replica, no Redis backplane for SignalR, no affinity
   verification. Real-time collaboration is in-process fan-out.
6. **No backup or restore drill has been performed.** Compose volumes exist; a restore has never
   been executed.
7. **No rollback has been rehearsed.**
8. **No script/style Content-Security-Policy.** The app sends `frame-ancestors 'self'`
   (from Blazor's frame-options middleware) plus `X-Content-Type-Options`, `Referrer-Policy`,
   `Permissions-Policy` and `X-Permitted-Cross-Domain-Policies`. A `script-src`/`style-src`
   policy is what is missing, and Blazor's inline bootstrapping makes it a real task.
9. **No SBOM** is produced with this release.
10. **Operator runbooks are incomplete.** Only the signing-certificate runbook exists
    (`docs/runbooks/`). Restore, session revocation, replica drain, automation disable and
    disk recovery are not written.
11. **Multi-instance is real, realtime is best-effort.** Two replicas share a key ring in the
    database, so sessions, antiforgery tokens and API quotas cross replicas, and a Redis backplane
    plus the `tuesday:updates` relay carries updates and hub messages between them. Redis is not a
    durable queue: if it is unreachable the write still succeeds locally and only the cross-replica
    hop is lost.
12. **API hardening is partial.** Cursor pagination, `Idempotency-Key`, a per-token daily quota,
    an SSRF guard and webhook retry/auto-disable are shipped and tested. Still missing: OpenAPI
    coverage is a single document snapshot rather than per-endpoint contract tests, the webhook
    signature carries no timestamp so a captured body can be replayed inside its window, and
    webhook retries run inline with the triggering request instead of on a durable queue.
12. **No accessibility audit.** axe has not been run on primary routes.
13. **Guests cannot search across boards** and see only granted items.
14. **Sign-in rejects accounts with a second factor over the password grant** by design; use the
    interactive form for MFA users.

---

## Upgrading / installing

```bash
cp docker/.env.example docker/.env      # fill in every CHANGE ME
openssl ...                             # docs/runbooks/signing-certificate.md §1
docker compose -f docker/compose.release.yaml --env-file docker/.env up -d --build
curl -s http://127.0.0.1:8080/health/ready
```

The database is PostgreSQL 17 and migrations apply at start-up. **Database choice changed in
this cycle**: MySQL/Oracle was replaced by PostgreSQL/Npgsql, which retired the GPLv2 licence
risk tracked as Q1. There is no MySQL upgrade path from a pre-0.9 install — 0.9.0 is the first
PostgreSQL release.

**Token endpoints require HTTPS in Production.** Over plain HTTP `/connect/token` returns
`This server only accepts HTTPS requests.` That is intended; terminate TLS at IIS or a reverse
proxy.

---

## Fixed in this cycle

- **No MFA user could sign in.** The second-factor form posted a field the handler did not read,
  and Blazor enhanced navigation rejected the form post outright.
- **The container could not run in Production.** OpenIddict had no signing key outside
  Development; it now fails at start-up with an actionable message instead of 500-ing per request.
- **The acceptance checklist over-claimed.** P6-3 to P6-14 were ticked without evidence and have
  been reset to their true state. What is now real: a restore rehearsal with row counts
  (`scripts/ops/backup-restore.sh`), an enforced nonce CSP checked in a browser and pinned by
  tests, a CycloneDX SBOM with a reviewed dependency allowlist, and a clean-room scan that runs
  in CI. Measured since: 200 live circuits on a 5,000-item board, p95 416 ms to the first grid row.
  Still unproven: 200 users editing concurrently, 200 circuits across replicas, a second replica with a shared key ring, and an
  operator-run restore drill.
- **Every unknown URL returned a 404 with an empty body**, so browsers replaced it with their own
  error screen. `UseStatusCodePagesWithReExecute` never fired (Blazor answers an unmatched route
  with `Content-Length: 0`, which the middleware treats as already handled), and a 4xx status set
  inside a component makes Blazor discard the rendered page. Now a catch-all route renders the
  styled page and the status is corrected on `OnStarting`.
- **An ungranted board showed the browser error page** for the same reason; it now renders
  "Board not found" with a real 404.
- **Typing in a grid cell dropped characters.** `@oninput` fed the value the input rendered, so
  each keystroke re-rendered the field and reset the caret; typing "VIRT EDIT" produced "VIRIT".
  In-flight text is now buffered separately from committed state.
- **The grid rendered every row and scrolled as one table per group**, giving several scroll
  boxes and column widths that disagreed between groups. One table, one scroll area, virtualised.
- **Empty boards showed a bare header with no rows**; they now explain what to do next.
