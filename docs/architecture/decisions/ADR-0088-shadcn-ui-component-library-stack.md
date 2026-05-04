# ADR-0088: React Component Library — shadcn/ui + Tailwind Stack for Frontend Primitives

**Status:** Superseded by ADR-0094 (2026-05-01)
**Date:** 2026-04-21
**Deciders:** Roman Głogowski (solo developer)
**Category:** Frontend Architecture
**Related:** ADR-0039 (React SPA), ADR-0040 (dependency graph), ADR-0084 (Playwright MCP), ADR-0087 (Stitch MCP); superseded by ADR-0094

## Context

Kartova's UI (see `docs/ui-screens/` — Stitch-generated mockups) is a dense multi-dashboard admin application: catalog lists, entity detail with tabs, dependency graph, tech radar, status page, environment map, scorecards. Building primitives (accessible dropdowns, data tables, modals, tabs, command palette) from scratch is weeks of work a solo dev cannot afford.

Stitch output shows a consistent visual language — dark Slate palette, sharp corners, blue accent, Inter body + JetBrains Mono for tech metadata, Lucide icons, status pill badges, progress bars, dense cards. This aesthetic maps near-1:1 to the shadcn/ui + Tailwind ecosystem defaults.

## Decision

Adopt **shadcn/ui + Tailwind CSS v4** as the primary UI primitive layer, with a curated supporting-library set for gaps shadcn/ui does not cover:

| Role | Library |
|------|---------|
| Primitives (Button, Card, Dialog, Dropdown, Tabs, Toast, Sheet, etc.) | **shadcn/ui** (Radix UI + Tailwind under the hood) |
| Styling utility | **Tailwind CSS v4** with theme tokens from DESIGN.md |
| Icons | **lucide-react** (matches Stitch icon style) |
| Data tables | **@tanstack/react-table** (headless) wrapped in shadcn `<DataTable>` |
| Forms | **react-hook-form** + **zod** for schema validation |
| Command palette (Ctrl+K search) | **cmdk** via shadcn `<Command>` |
| Toast notifications | **sonner** |
| Charts (KPI, sparkline, bar, area) | **Recharts** wrapped in shadcn/ui `<Chart>` |
| Dependency graph | **React Flow (@xyflow/react)** (per ADR-0040) |
| Animations | **motion** (framer-motion successor) — used sparingly |
| Tech Radar (custom visualization) | Hand-rolled SVG + D3 scale utilities |

**Navigation is canonical in DESIGN.md, not Stitch output.** Stitch screens in `docs/ui-screens/` show nav structure inconsistencies across generations. When implementing nav, trust DESIGN.md (56px top bar, 260px sidebar, specific item hierarchy); Stitch output is reference only for the visual styling of individual nav items. This is an explicit exception to ADR-0087 "Stitch is canonical mockup source."

**Theme integration:** Tailwind config imports Slate palette and typography tokens from DESIGN.md as CSS variables. shadcn/ui components consume these variables — single source of truth for colors/spacing/typography.

**Component ownership:** shadcn/ui components are copied into the repo (`web/src/components/ui/`) via `npx shadcn add <component>`. No `@shadcn/ui` package dependency. Modifications are local and reviewed in PRs.

## Rationale

- **Visual fidelity** — Stitch output aligns with shadcn/ui defaults; minimal theming effort gets ~80% visual match, the remaining 20% is token mapping from DESIGN.md.
- **Code ownership** — components live in the repo; no surprise breaking changes from library major versions; refactor freely.
- **Accessibility covered** — Radix UI handles keyboard navigation, focus management, ARIA roles; the hard parts are outsourced.
- **Ecosystem momentum** — de facto standard for modern React dashboards in 2025; well-documented, AI-assistant friendly.
- **Composable with TanStack stack** — TanStack Query (per ADR-0039) and TanStack Table work naturally together; shadcn/ui has an opinionated DataTable pattern that wraps TanStack Table.
- **Modular monolith fit** (ADR-0082) — frontend module boundary: each backend module gets its own `web/src/modules/{module}/` folder; shared `web/src/components/ui/` for primitives.

## Alternatives Considered

- **Mantine v7** — batteries-included, faster to first dashboard. Rejected: visual defaults do not match Stitch aesthetic; theming to Slate palette would require significant custom work; larger library surface, less composable.
- **Material UI (MUI)** — mature but heavy Material aesthetic mismatches Stitch dark Slate dashboard look.
- **Ant Design** — strong admin catalog (Pro Components) but opinionated aesthetic clashes with design system.
- **Chakra UI** — solid primitives but weaker admin component set vs Mantine; still visual mismatch.
- **Tremor for charts** — considered for KPI cards and charts. Rejected: stylistic opinionation conflicts with shadcn/ui; Recharts + shadcn/ui `<Chart>` wrapper achieves the same with better theme coherence.
- **Raw Radix + Tailwind (no shadcn)** — maximum control but shadcn adds the right amount of opinion (defaults, variants, class compositions); rebuilding those wastes time.

## Consequences

**Positive:**
- Visual match with Stitch output requires minimal re-theming
- Components owned by the repo — freely modifiable, no library update pain
- Standard modern React stack, fastest AI-assisted implementation velocity
- DESIGN.md tokens → Tailwind config → shadcn/ui components: one CSS-var flow, no design-drift
- Accessibility handled by Radix primitives

**Negative / Trade-offs:**
- More libraries to learn than a single batteries-included option (Mantine) — offset by each being small and focused
- Composite work on data tables (TanStack Table integration) is non-trivial — one-time cost
- Tech Radar remains custom work (no library shortcut)
- Must maintain Tailwind config consistency with DESIGN.md manually

**Neutral:**
- Frontend dependency footprint: ~10 small focused libraries vs 1 large one
- shadcn/ui components receive updates via `shadcn update` — opt-in

## Implementation Notes

**Setup (Phase 0, E-01.F-01 frontend scaffolding):**

```
web/
  package.json                 # React 19 + TypeScript strict + Vite
  tailwind.config.ts           # imports tokens from DESIGN.md
  components.json              # shadcn/ui config
  src/
    components/ui/             # shadcn components (copied, owned)
      button.tsx
      card.tsx
      dialog.tsx
      data-table.tsx
      chart.tsx
      command.tsx
      sidebar.tsx              # NOT auto-generated; built per DESIGN.md nav spec
      ...
    modules/
      catalog/
        components/            # feature-specific composed components
        pages/
      organization/
      ...
    lib/
      api.ts                   # TanStack Query client
      utils.ts                 # cn() helper (Tailwind class merging)
```

**Tailwind config imports DESIGN.md tokens:**

```ts
export default {
  theme: {
    extend: {
      colors: {
        background: "hsl(var(--bg-900))",
        card: "hsl(var(--bg-800))",
        border: "hsl(var(--bg-700))",
        foreground: "hsl(var(--text-primary))",
        muted: "hsl(var(--text-muted))",
        primary: "hsl(var(--accent-blue))",
      },
      fontFamily: {
        sans: ["Inter", "sans-serif"],
        mono: ["JetBrains Mono", "monospace"],
      },
    },
  },
}
```

**Adding a component:** `npx shadcn add card` copies `card.tsx` to `components/ui/`. Modify freely. Commit. No package dependency.

**Navigation canonical rule:**
- Structure: `web/src/components/layout/Sidebar.tsx` + `TopBar.tsx`
- Items + hierarchy: read from DESIGN.md nav spec, NOT from Stitch output
- Stitch inconsistency: documented; when Stitch shows nav variation, prefer the latest DESIGN.md spec

## References

- shadcn/ui: https://ui.shadcn.com/
- Tailwind CSS v4: https://tailwindcss.com/
- TanStack Table: https://tanstack.com/table
- Radix UI: https://www.radix-ui.com/
- Recharts: https://recharts.org/
- React Flow: https://reactflow.dev/
- lucide-react: https://lucide.dev/
- `docs/design/DESIGN.md` (tokens, nav spec — canonical)
- `docs/ui-screens/` (Stitch-generated reference; nav inconsistent — see Decision text)
- ADR-0039 (React SPA), ADR-0040 (dependency graph), ADR-0087 (Stitch MCP)
