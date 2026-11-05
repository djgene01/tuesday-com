# Release notes — Tuesday.com 0.9.0 (release candidate)

**Status:** release candidate for an internal work-tracking deployment. Not 1.0.
**Date:** 2026-11-04 · **Version:** 0.9.0 · **Image tag:** `tuesday-web:0.9.0`

This build exists so the team has a real, installable system to work from while the UI/UX pass
begins. It is feature-complete for the core loop — boards, views, collaboration, automations,
API — and deliberately unproven on operational load. Read "Known limitations" before deploying.

---

## What works

| Area | State |
|---|---|
| Identity | Cookie sign-in, TOTP second factor with recovery codes, OpenIddict token server (`/connect/token`), API tokens, bearer auth on `/api/v1` |
| Tenancy | Tenant-scoped query filters; admin/member/guest roles; cross-tenant isolation suite green |
| Boards | Groups, columns, 500-item grids, inline edit, attachments, item detail |
| Views | Kanban, calendar, timeline, Gantt |
| Collaboration | Threaded updates with HTML sanitisation, likes, reply/edit/delete, notifications, email preference, in-app search |
| Automations | Trigger → condition → action rules; scheduled date-passed sweep with per-item-per-day dedupe |
| Integrations | Signed webhooks with challenge handshake, REST API, OpenAPI-less but stable JSON |
| Dashboard | Per-board counts, by-status and by-group breakdown, automation run history |
| Ops | `/health/live`, `/health/ready`, correlation ids, security headers, Docker image, Compose stack |

**Verification at this tag:** 103 tests green (52 unit, 38 isolation, 13 integration), Release build
with zero warnings, container healthy in Production behind the deployment path in
`docs/runbooks/signing-certificate.md`.

---

## Known limitations

Stated plainly because they are real. Each maps to an open item in
`Development-Plans/10-Acceptance-Checklists.md` §8.

1. **The item grid is virtualised**, so a 500-item board keeps ~37 rows in the DOM and scrolls
   smoothly. The 100k-item checkpoint has still not been run, so treat "large" as unmeasured
   beyond a few thousand.
2. **No subitems.** Specified (U-12), not built.
3. **Drag reordering is not available in the grid.** Kanban, calendar and timeline are
   drag-enabled; the table grid is not.
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
11. **API hardening is partial.** No cursor pagination, no `Idempotency-Key`, no SSRF guard on
    webhook URLs, no automatic webhook retry or auto-disable.
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
  been reset to their true state.
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
