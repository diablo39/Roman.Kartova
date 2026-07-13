# Design — Tabbed entity-detail layout (E-11.F-02.S-04)

**Date:** 2026-07-11
**Story:** E-11.F-02.S-04 — API "Definition" tab (tabbed entity-detail layout)
**Type:** Frontend-only (React/TypeScript). No backend, DB, auth, or C# changes.
**Related:** ADR-0084 (browser verification, react-aria `isRowHeader` gotcha), ADR-0094 (Untitled UI / react-aria-components), ADR-0040 (graph views), openapi-spec-render slice (PR #69, `SpecRender`).

## Goal

Move the API spec render off the API detail page's long scroll onto a dedicated **Definition** tab, and — in the same slice — generalize a **tabbed detail-page layout** across all three catalog entity detail pages (API, Service, Application). This replaces today's single long scrolling `Card` per page.

Rationale: neither Backstage nor Compass renders a spec inline on an overview; both use a dedicated definition surface. A tab gives Scalar the full card width (it has its own per-operation sidebar) and defers its ~2.8 MB chunk until the tab is opened. The tabbed shell is the natural home for future per-entity surfaces (Documentation, Deployments, Settings) as those features land.

## Scope

**In scope**
- A reusable `<DetailTabs>` primitive (react-aria `Tabs`), Untitled-UI-styled.
- Migrate `ApiDetailPage`, `ServiceDetailPage`, `ApplicationDetailPage` to the tabbed layout.
- Deep-linkable active tab via `?tab=` query param.
- Move `ApiSpecSection` render into the API **Definition** tab (the S-04 goal).
- ADR-0114 documenting the pattern.

**Out of scope (follow-ups)**
- New per-entity tabs for unbuilt features: Documentation (E-11.F-01), Deployments (E-02.F-05), Settings. We render **no empty tabs** — a tab appears only when it has real content today.
- gRPC/GraphQL spec rendering + try-it-out (E-11.F-02.S-02).
- Spec version diffing / changelog (E-21, E-11.F-02.S-03).
- Playwright E2E spec for tab switching (expected FU per "any bug it finds becomes a regression test").

## Tab structure

One shared primitive; each entity shows only tabs whose content exists today. Tab sets need not be identical across entities.

| Tab | Application | Service | API |
|-----|-------------|---------|-----|
| **Overview** | description · metadata · successor | description · metadata · endpoints table | description · metadata (incl. spec-URL) |
| **Dependencies** | API surface (provides/consumes/exposes) · mini-graph + Relationships (depends-on/part-of) | API surface · mini-graph + derived depends-on + Relationships | Relationships (incoming consumers/providers) |
| **Definition** | — | — | Scalar spec render (`ApiSpecSection`/`SpecRender`), lazy on open; empty-state when no spec |

Tab order: **Overview → Dependencies → Definition**. Default tab: **Overview**.

Design decisions:
- The "APIs" surface (`ApiSurfaceSection`) is **folded into Dependencies** as a labeled section, not a separate tab — it is a short list today and both read as connectivity. If it later grows (per-operation drill-down, consumer lists) it can be promoted to its own badge-carrying tab.
- The **entity header** (name, style/health/lifecycle badges, action buttons — Edit, `LifecycleMenu`, `AssignTeamPicker`, successor actions) stays **above** the tab bar, always visible regardless of active tab.

## Architecture

### `<DetailTabs>` primitive

`web/src/components/application/tabs/detail-tabs.tsx` — thin Untitled-UI-styled wrapper over react-aria `Tabs/TabList/Tab/TabPanel`.

```tsx
<DetailTabs aria-label="Payment Gateway API">
  <DetailTabs.Tab id="overview" label="Overview">…</DetailTabs.Tab>
  <DetailTabs.Tab id="dependencies" label="Dependencies">…</DetailTabs.Tab>
  <DetailTabs.Tab id="definition" label="Definition">…</DetailTabs.Tab>
</DetailTabs>
```

- **Styling** matches the Stitch mockup: horizontal bar, active tab has a bottom-border indicator (`border-b-2` brand), inactive `text-tertiary` → hover. react-aria provides roving-tabindex + arrow-key nav.
- **Reason for a shared primitive** (vs inline react-aria per page): one owner for the URL-sync contract, styling, and the ADR-0084 mount reasoning; three pages stay declarative.

### URL sync

- `selectedKey` ↔ `?tab=` via react-router `useSearchParams` (already used by `useListUrlState`).
- Read on mount; write with `replace: true` on change (no history spam).
- Unknown `?tab`, absent, or a tab not valid for this entity (e.g. `?tab=definition` on a Service) → fall back to `overview` **and normalize the URL** so a broken deep-link never renders an empty shell.
- Consistent with the graph explorer's URL-driven state; survives token-expiry re-auth.

### Panel mount behavior

- react-aria `Tabs` mounts **only the active panel** (inactive panels unmount). Desired here:
  - Definition's `lazy()` Scalar bundle stays unloaded until the tab is opened — no first-paint cost change.
  - Trade-off: switching re-mounts a panel (re-runs its queries). Acceptable — mini-graph/relationships queries are cheap and React Query caches them.
- We do **not** use `shouldForceMount` (would defeat the lazy Definition win).

## Error / loading / empty states

**Entity level (unchanged):** `query.isLoading` → skeleton card; `isError || !data` → "not found" card. Tabs render only once the entity is loaded, so header + tab bar always have data.

**Panel level:** each moved section keeps its existing states (mini-graph Suspense skeleton, relationships loading/empty, derived-deps states). New — **Definition**: spec present → render; no spec → empty-state ("No specification attached" + attach link), reusing the existing `ApiSpecSection` empty path.

## ADR-0084 mitigation (primary hazard)

Tab switch is a heavy re-render — the same event class that blank-pages a mis-configured react-aria `<Table>`. Tables now inside panels: Service **endpoints** table, **ApiSurfaceSection** tables.

- Audit each in-panel `<Table>` for exactly one `isRowHeader` column (endpoints already has it).
- Unit guard: after selecting a table-bearing tab, assert `getAllByRole("rowheader").length > 0`.
- **Gate-10 is the real evidence** — jsdom recovers silently; a real browser does not.

## Testing strategy

Frontend-only slice. Gate profile:

| Gate | Applies |
|------|---------|
| 1 build (0 warnings) | Yes — `tsc -b` / `npm run build` binding gate |
| 2 per-task reviews | Yes |
| 3 real-seam integration | **N/A** — no new HTTP/auth/DB/middleware seam |
| 4 container (web image) | Yes — web build in CI |
| 5 `/simplify` | Yes |
| 6 mutation | **N/A** — no Domain/Application (C#) logic in diff |
| 7 requesting-code-review | Yes |
| 8 pr-review-toolkit | Yes (run for real — no folding) |
| 9 deep-review | Yes |
| 10 visual/browser | **Primary** — see below |
| 11 CI green | Yes |

**Unit (Vitest + RTL):**
- `DetailTabs`: renders tab set, arrow-key nav, active indicator; `?tab=` read on mount + write-with-replace; unknown/invalid tab → `overview` + URL normalized.
- Each page: correct tab set (App/Service = 2, API = 3), default Overview, deep-link `?tab=dependencies` selects it, Definition only on API.
- ADR-0084 guard (rowheader assertion) on table-bearing panels.

**Gate-10:** cold-start dev server, authenticate, for each entity type open every tab in-SPA, screenshot, confirm no blank-page + 0 console errors + Definition lazy-chunk loads only on open. Evidence under `docs/superpowers/verification/2026-07-11-tabbed-entity-detail/`.

## Impact analysis (C#)

**N/A** — frontend-only slice. No C# symbol signatures or behavior change; no new endpoints, permissions, or codegen. The plan's `## Impact Analysis (codelens)` section will carry `N/A — frontend-only, no C# symbols changed`.

## ADR-0114 (to draft for review before saving)

*"Tabbed entity-detail layout."* Decision: react-aria `Tabs` via a shared `DetailTabs` primitive; per-entity tab sets reflect real content (no empty tabs); `?tab=` deep-link; active-panel-only mount (lazy Definition); `isRowHeader` guard + gate-10 browser evidence; future tabs (Documentation, Deployments, Settings) slot in per feature. Amends the detail-page UI convention; supersedes nothing.

## Follow-ups

- **FU-1** — Playwright E2E spec: default tab + deep-link + tab switch per entity type.
- **FU-2** — promote Documentation / Deployments / Settings tabs as those features ship.
- **FU-3** — optional Compass-style "N endpoints/APIs" teaser count on the Dependencies tab label.
