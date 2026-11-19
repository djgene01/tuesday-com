# Design system

Tuesday's interface is a calm, dense work tool: a warm paper canvas, soft-black ink, one coral
accent that carries the primary action, and colour used only where it carries meaning (status,
feedback, the active place in the navigation). Everything is built from **Tailwind CSS v4**
tokens plus a small set of Razor primitives in `src/Tuesday.Web/Components/Ui/`.

Clean-room note: colour, spacing, shape and density are functional patterns and are not
protected. No third-party logo, icon set, illustration or markup is copied — the brand mark is
a plain "T", every icon in `Icon.razor` is an original drawing on a 24px grid, and the
typefaces are open-licence files served from our own `wwwroot/fonts`.

## Tokens

Defined once in `src/Tuesday.Web/Styles/app.css` under `@theme static`, so every utility
(`bg-brand`, `text-ok`, `text-sm`, `rounded-control`) derives from them and every token is also
available as a CSS variable. The older stylesheet (`wwwroot/app.css`) only aliases them
(`--tue-ink: var(--color-ink)`), so there is one place a value is decided.

### Colour

| Token | Value | Use | Contrast |
|---|---|---|---|
| `--color-brand` | `#A8554E` | primary button fill, links, focus ring, active nav | 5.0:1 on canvas; white on it 5.2:1 |
| `--color-brand-hover` / `-press` | `#9E4B45` / `#8F423C` | pointer and pressed states | ≥ 5.9:1 with white |
| `--color-brand-ink` | `#7A3B35` | text on brand washes | 8.4:1 on white |
| `--color-brand-wash` | `#FFF1EF` | active nav item, unread rows, selected fills | — |
| `--color-peach` / `-brand-tint` | `#FFB7B2` | secondary button fill (ink text only), avatars | ink on it 9.1:1 |
| `--color-ink` | `#292524` | body and headings | 14.7:1 |
| `--color-ink-soft` | `#78716C` | secondary text | 4.7:1 on canvas |
| `--color-ink-faint` | alias of ink-soft | kept for compatibility; anything lighter fails 4.5:1 | — |
| `--color-line` / `-strong` | `#E7E5E4` / `#D6D3D1` | rules, borders, input borders | — |
| `--color-canvas` | `#FDFCF8` | page background | — |
| `--color-surface` / `-2` | `#FFFFFF` / `#FAF9F6` | cards, bars / table headers, hovers, wells | — |
| `--color-ok` on `-ok-bg` | `#007A4D` on `#E7F6F0` | success alerts, "On" pills | 4.8:1 |
| `--color-warn` on `-warn-bg` | `#8A4B00` on `#FDF3E3` | warnings | 6.2:1 |
| `--color-bad` on `-bad-bg` | `#B3243B` on `#FDECEB` | errors, destructive buttons (white text 6.5:1) | 5.7:1 |
| `--color-info` on `-info-bg` | `#2F5F9E` on `#EAF1FB` | informational alerts | 5.7:1 |
| `--color-status-done` | `#00C875` | Done (ink text) | 6.9:1 |
| `--color-status-working` | `#FDAB3D` | Working on it (ink text) | 8.0:1 |
| `--color-status-stuck` | `#C42B45` | Stuck (white text) | 5.6:1 |
| `--color-status-idle` | `#C4C4C4` | Not started (ink text) | 8.7:1 |
| `--color-status-review` | `#8E4BC4` | Review (white text) | 5.3:1 |
| `--color-status-info` | `#579BFC` | other statuses (as a 15% wash), group accent | — |

Board data never maps to hex in a component: `Components/Ui/StatusPalette.cs` turns a status
label, a group colour name or a chart series index into a token (`var(--color-status-done)`).

### Type

**Outfit** (variable, 300–600) for everything — UI, data and headings. **Reenie Beanie** only
for the decorative aside on the landing page. Both self-hosted, Outfit preloaded,
`font-display: swap`.

| Token | Size / line | Use |
|---|---|---|
| `text-2xs` | 11 / 16 | pills, meta, the floor — nothing is smaller |
| `text-xs` | 12 / 16 | labels, hints, timestamps |
| `text-sm` | 13 / 20 | body text in the app, grid cells |
| `text-base` | 14 / 22 | larger controls |
| `text-md` | 15 / 22 | card titles |
| `text-lg` | 18 / 26 | — |
| `text-xl` | 22 / 28 | auth and empty-state titles |
| `text-2xl` | 26 / 32 | page titles (`PageHeader`) |

Arbitrary pixel sizes (`text-[13px]`) are not used outside the marketing page.

### Shape and depth

`--radius-control` (8px) for buttons and inputs, `--radius-card` (12px) for cards, dialogs and
menus, `--radius-pill` for pills and the marketing CTAs, `--radius-marketing` (2rem) only on the
landing page. Shadows: `--shadow-card` (resting cards on hover), `--shadow-overlay` (dialogs,
menus, toasts).

No dark mode yet, but nothing hard-codes a colour outside the tokens, so one can be added by
redefining them.

## Primitives (`Components/Ui/`)

All are render-mode-agnostic unless marked interactive; extra attributes (`name`, `aria-*`,
`@onclick`) pass through.

| Component | What it is |
|---|---|
| `Button` | `Variant` Primary / Secondary / Ghost / Quiet / Danger / QuietDanger, `Size` Sm 28 / Md 32 / Lg 40, `Busy` (spinner + `aria-busy` + disabled), `Href` renders a link, `IconStart`/`IconEnd`, `IconOnly`. |
| `Field` | Label + control slot + hint + error. With `For`, an explicit `label for` and hint id for `aria-describedby`. Controls use `.ui-input`, `.ui-select`, `.ui-textarea`. |
| `Alert` | Inline feedback; `role="alert"` for errors, `role="status"` otherwise. The fallback for static SSR pages. |
| `PageHeader` | Breadcrumbs, eyebrow, the `h1`, subtitle, badges and actions. |
| `Breadcrumbs` | `<nav aria-label="Breadcrumb">` with `aria-current` on the last crumb. |
| `EmptyState` | Icon or code, title, explanation, next action. `NotFoundPanel` wraps it for every 404. |
| `Spinner`, `Skeleton` | Loading states; reduced-motion safe; one polite "Loading" status. |
| `Avatar` | Initials from a display name or email. |
| `Icon` | `IconName` → original stroke icon, `aria-hidden` unless given a `Label`. |
| `ConfirmDialog` *(interactive)* | Native `<dialog>` with `showModal()`: focus trap, Escape, backdrop; focus returns to the trigger. Required before every destructive action. |
| `ToastService` + `ToastHost` *(interactive)* | Scoped per circuit; the layout renders one polite live region for signed-in users. Max three, five seconds each, dismissible. |

The `.ui-*` classes they render live in `Styles/components.css`, which is imported **before**
the utilities so a utility on the same element always wins (no `!important` needed).

## Layout

- **Navigation** — one list, three presentations, CSS only: a bottom bar with five labelled
  destinations below 768px; a 56px icon rail with hover/focus tooltips from 768px; a 208px
  labelled sidebar from 1024px. The active item gets `bg-brand-wash`, `text-brand-ink` and a
  2px brand edge. Notifications shows an unread count.
- **Top bar** — 56px, sticky: brand (phones), a GET search form, and a `<details>` account
  menu (security, API tokens, sign out). No JavaScript.
- **Content** — `<main id="main">` behind a "Skip to content" link, wrapped in an
  `ErrorBoundary` with a friendly recovery state. Columns: `max-w-[1600px]` boards,
  `max-w-[1200px]` workspace and dashboard, `max-w-[820px]` account pages, `max-w-[400px]` auth.
- **Board header** — breadcrumb, title with type and read-only badges, secondary actions on
  the right, then the view links (an underline bar; they are links with `aria-current`, because
  each view is an address).

## Board grid

- One table: sticky header row and sticky item column (with a soft shadow edge), 36px rows,
  hairline rules, row hover, an inset 2px focus ring on every cell.
- Group bands are tinted with the group colour; every group ends with its own "Add item" row.
- Typed editors where the value round-trips exactly: Status → a coloured picker built from the
  column's configured labels; Date → `<input type="date">` (yyyy-MM-dd); Checkbox → a checkbox
  ("true"/"false"); Number → right-aligned with a decimal keypad. Everything else, and a status
  without configured labels, stays a text cell.
- Keyboard: Enter saves, Escape restores the focused cell only (via `wwwroot/js/grid.js`,
  keeping focus), Alt+↑/↓ moves the row, Tab is native.
- The row's "open" link shows on hover and focus, and always on coarse pointers.

## Rules that keep it coherent

1. **Colour means something.** Coral = the primary action and "you are here". Status colours
   appear only in status cells and status charts. Feedback uses ok / warn / bad / info.
2. **One primary action per surface.** Everything else is Ghost or Quiet.
3. **Every destructive action asks first**, in a `ConfirmDialog` that names what is lost.
4. **Every async action shows it is working** (`Busy`), and reports success with a toast
   (interactive) or an `Alert` (static SSR).
5. **Every empty list says what it is for and what to do next** (`EmptyState`).
6. **Density beats decoration** on app surfaces. The paper grain and blobs are scoped to the
   marketing layout (`.marketing`).

## Accessibility

- Every text pairing in the colour table meets **WCAG 2.2 AA (4.5:1)**.
- One focus ring everywhere: 2px brand outline, 2px offset (inset inside grid cells). Headings
  and `<main>` never paint a ring — Blazor moves focus to the `h1` after navigation.
- Icon-only controls carry `aria-label`; decorative icons are `aria-hidden`.
- Live regions: errors `role="alert"`, confirmations `role="status"`, toasts `aria-live="polite"`.
- Motion respects `prefers-reduced-motion`.

## Surfaces

| Surface | Where |
|---|---|
| Landing | `Home.razor` + `MarketingLayout` |
| Sign-in, invitation | `Login.razor` (`EmptyLayout`), `InviteAccept.razor` |
| Workspace | `Workspace.razor` — board cards, create / invite forms |
| Board shell | `BoardView.razor` + `BoardViewsHost` |
| Grid | `BoardGrid.razor` |
| Sub-item columns | `SubitemColumns.razor` + `SubitemColumnsEditor` |
| Item | `ItemDetail.razor` + `SubItemsPanel`, `UpdateThread`, `AttachmentPanel` |
| Views | `KanbanView`, `CalendarView` (agenda list below 640px), `TimelineView`, `GanttView` |
| Dashboard | `BoardDashboard.razor` |
| Search, notifications, account | `Search`, `Notifications`, `ApiTokens`, `AccountSecurity` |
| Status pages | `Denied`, `NotFound` (`NotFoundPanel`), `Error` |

The board views keep their geometry in `wwwroot/app.css` (calendar cells, timeline tracks,
gantt overlays). The timeline label column is one variable, `--tl-label` (12rem, 8rem on
phones), which the gantt arrow overlay also reads.

## Building the CSS

```bash
npm install        # once
npm run css        # writes src/Tuesday.Web/wwwroot/css/tailwind.css
npm run css:watch  # during design work
```

The compiled file is committed so `dotnet build` and the Docker image need no Node
toolchain. CI regenerates it and fails if the committed file has drifted.

## Why utilities are imported unlayered

`@import "tailwindcss/utilities.css"` is loaded **without** `layer()`. The app still ships a
plain stylesheet (`wwwroot/app.css`), which is unlayered, and an unlayered rule always beats a
layered one. With Tailwind's default layers, `a { color }` overrode
`.ui-btn-primary { color: #fff }` and every primary button rendered invisible. Loading
utilities unlayered after `app.css` makes source order decide instead — which is also why
`components.css` is imported between the theme and the utilities.

Preflight is deliberately not imported: the reset would change margins on every element the
older stylesheet relies on.
