# Design system

The visual language follows monday.com: a violet primary, soft grey canvas, pill-shaped
actions, a compact data grid, and colour used only where it carries meaning. Everything is
built with **Tailwind CSS v4** utilities; there is no hand-written component CSS for new
surfaces.

Clean-room note: colour, spacing, shape and density are functional patterns and are not
protected. No monday.com logo, icon set, illustration, font file or markup is copied — the
brand mark is a plain "T", the icons are our own SVGs, and both typefaces are open-licence
files served from our own `wwwroot/fonts`.

## Tokens

Defined once in `src/Tuesday.Web/Styles/app.css` under `@theme`, so every utility
(`bg-brand`, `text-ink-soft`, `rounded-pill`) derives from them.

| Token | Value | Use |
|---|---|---|
| `--color-brand` | `#6161FF` | primary actions, active tab, focus ring |
| `--color-brand-hover` / `-press` | `#5151D5` / `#4747BB` | pointer and pressed states |
| `--color-brand-tint` | `#ECECFF` | selected rail item, subtle fills |
| `--color-ink` | `#323338` | body and headings |
| `--color-ink-soft` | `#676879` | secondary text (5.5:1 on white) |
| `--color-ink-faint` | `#6B6F80` | tertiary text (4.7:1 on canvas) |
| `--color-line` / `-strong` | `#E6E9EF` / `#D0D4E4` | rules, borders |
| `--color-canvas` | `#F6F7FB` | page background |
| `--color-surface` | `#FFFFFF` | cards, bars, grid |
| `--color-status-done` | `#00C875` | Done (ink text, 5.7:1) |
| `--color-status-working` | `#FDAB3D` | Working on it (ink text, 6.6:1) |
| `--color-status-stuck` | `#C42B45` | Stuck (white text, 5.6:1) |
| `--color-status-idle` | `#C4C4C4` | Not started (ink text, 7.2:1) |
| `--color-status-review` | `#8E4BC4` | Review (white text, 5.3:1) |
| `--color-status-info` | `#579BFC` | group accent, informational fills |

Type: **Poppins** (500/600/700) for display headings and board titles; **Figtree**
(variable) for UI, data and body. Both self-hosted, preloaded, `font-display: swap`.

Shape: `--radius-pill` (999px) for actions, `--radius-card` (10px) for cards,
`--radius-control` (6px) for inputs.

## Layout

- **Left rail** — 56px, white, border-right. One icon per destination, active item gets
  `bg-brand-tint` + `text-brand-ink`. Never more than seven items.
- **Top bar** — 48px, white, sticky. Brand, then the signed-in identity. No board actions
  here; they belong to the board.
- **Canvas** — `bg-canvas`, content in a `max-w-[1600px]` column for boards,
  `max-w-[1200px]` for workspace, `max-w-[400px]` for auth.
- **Board header** — breadcrumb (workspace / type), 26px Poppins title, one primary action
  at the right, then the view tabs as a 2px underline bar.

## Components

`.ui-btn-primary`, `.ui-btn-ghost`, `.ui-btn-quiet`, `.ui-card`, `.ui-pill` — defined with
`@apply` so a call site stays readable.

Rules that keep it coherent:

1. **Colour means something.** Violet = primary/active. Status colours appear only in
   status cells and status-derived alerts. Everything else is neutral.
2. **One primary action per screen**, at the right edge of the header row.
3. **Density beats decoration.** Grid rows are 34px, cells have no borders of their own —
   the row rule is the only line.
4. **Pills for labels, chips for statuses.** Pills are pale and round; statuses are solid
   fills inside the cell, inset by 4px.
5. **Text is never placed on a variable image.**

## Accessibility

- Every pairing in the token table meets **WCAG 2.2 AA (4.5:1)** for its actual text colour;
  white-on-green was specifically rejected (2.2:1) in favour of ink-on-green.
- Focus is visible on every control (`focus-visible` ring in brand violet). Headings never
  paint a ring — Blazor moves focus to the `h1` after navigation.
- The icon rail is `aria-label`led per link; grid cells carry `aria-label="Item: Column"`.
- Row actions are hidden until hover but reachable by keyboard (`focus-visible:opacity-100`).

## Surfaces

| Surface | Where |
|---|---|
| Landing, sign-in | `Home.razor`, `Login.razor` (`EmptyLayout`) |
| Workspace | `Workspace.razor` — board cards, create/invite cards |
| Board shell | `BoardView.razor` — breadcrumb, title, view tabs |
| Grid | `BoardGrid.razor` — virtualised rows, status cells, group bands |
| Sub-item columns | `/board/{id}/subitems` — table + inline add form |
| Item | `ItemDetail.razor` + `UpdateThread` / `AttachmentPanel` |
| Views | `KanbanView`, `CalendarView`, `TimelineView`, `GanttView` |
| Dashboard | `BoardDashboard.razor` — stat cards, share-of-board bars |
| Search, notifications, account | `Search`, `Notifications`, `ApiTokens`, `AccountSecurity` |
| Status pages | `Denied`, `NotFound`, `Error` |

The four board views keep their geometry in `wwwroot/app.css` (calendar cells, timeline
tracks, gantt paths are positioning logic, not decoration); their colour, type and chrome
were moved onto the tokens above.

## Building the CSS

```bash
npm install        # once
npm run css        # writes src/Tuesday.Web/wwwroot/css/tailwind.css
npm run css:watch  # during design work
```

The compiled file is committed so `dotnet build` and the Docker image need no Node
toolchain. CI regenerates it and fails if the committed file has drifted.

## Why utilities are imported unlayered

`@import "tailwindcss/utilities.css"` is loaded **without** `layer()`. The app still ships
its long-standing stylesheet (`wwwroot/app.css`), which is unlayered, and an unlayered rule
always beats a layered one. With Tailwind's default layers, `a { color }` overrode
`.ui-btn-primary { color: #fff }` and every primary button rendered invisible. Loading
utilities unlayered after `app.css` makes source order decide instead.

Preflight is deliberately not imported: the reset would silently change margins on the
older pages that still rely on their own styles.
