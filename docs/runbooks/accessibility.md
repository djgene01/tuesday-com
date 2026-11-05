# Accessibility

The audit uses [axe-core](https://github.com/dequelabs/axe-core) (MPL-2.0, a dev dependency; it is
not part of the runtime image) against WCAG 2.0/2.1 A and AA rules.

## Running it

```bash
# 1. app on a Development database, seeded accounts
npm run css && dotnet build Tuesday.slnx -c Release
(cd src/Tuesday.Web && ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:5205 \
  dotnet bin/Release/net10.0/Tuesday.Web.dll)

# 2. a browser to drive
msedge --headless=new --remote-debugging-port=9222 --user-data-dir=<scratch dir> about:blank

# 3. the sweep
BASE=http://127.0.0.1:5205 node scripts/a11y/axe.js
```

`BOARD` and `ITEM` override the fixture ids used for the board-scoped routes. The script writes
`artifacts/axe.json` (id, impact, help, node count, first target, failure summary) and exits non-zero
if any serious or critical violation remains.

## Result at 0.9.4

Twelve routes — landing, login, workspace, board, dashboard, item, sub-item columns, search,
notifications, security, tokens, 404 — with **0 violations** overall.

Two real defects were found and fixed:

| Finding | Measured | Fix |
|---|---|---|
| Breadcrumb and link text in brand purple | 4.2:1 on `#f6f7fb` at 12 px (needs 4.5:1) | brand `#6161ff` → `#5252e0` (5.4:1 on canvas, 5.8:1 under white) |
| "Remove" button in the sub-item column editor | 4.4:1 on a hovered row | `text-ink-faint` → `text-ink-soft` (4.8:1) |

The first was also the reason white text on brand buttons sat exactly on the 4.5:1 boundary; darkening
the hue improved both directions at once.

## What this does not cover

- **Firefox and WebKit.** The sweep runs in Chromium (Edge). Contrast maths is engine-independent, but
  focus-ring and scroller behaviour are not.
- **Colour-blind simulation, zoom to 200%, screen-reader traversal.** axe checks structure, names and
  contrast, not comprehension.
- **The board in card view at 390 px** — the mobile board still scrolls sideways; see the checklist.
