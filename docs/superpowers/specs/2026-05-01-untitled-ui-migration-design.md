# Untitled UI migration — design

**Date:** 2026-05-01
**Author:** Roman Głogowski (AI-assisted)
**Status:** Drafted via `superpowers:brainstorming`; pending user review before plan.
**Supersedes (planned):** ADR-0088 (shadcn/ui + Radix + Tailwind v4)
**New ADR (planned):** ADR-0092 — Untitled UI free-tier as primary UI primitive layer.

## 1. Goal

Replace the existing shadcn/ui + Radix UI primitive layer in `web/` with Untitled UI's free-tier components, taking Untitled's visual language as-shipped (no DESIGN.md re-mapping). Slice-4 functionality must be preserved 1:1.

## 2. Constraints + decisions (locked during brainstorming)

| Axis | Decision |
|---|---|
| Strategy | "Raw and unstyled" interpreted as **Untitled UI as-shipped, default theme** (option C1). Their JSX, their classes, their tokens. |
| Scope | **Full rip-and-replace**. Remove `radix-ui` umbrella + `lucide-react` deps; replace all 17 owned shadcn primitives in `web/src/components/ui/`. |
| Tier | **Free tier only.** No PRO license, no `get_page_template_files`. |
| Icons | Switch to `@untitledui/icons` everywhere. |
| Theme tokens | **Untitled defaults are the new source of truth.** `DESIGN.md` color/typography references retired in favor of Untitled CSS-vars. |
| `DESIGN.md` | Kept for nav structure / layout density / information-density rules only. Add note. |
| `sonner` toast | Kept as-is (Untitled doesn't ship a toast). |
| Sequencing | **Big-bang single PR** (option A) — slice-4's UI footprint is small enough that a single coherent diff beats orchestration overhead. |

## 3. Free-tier coverage

Inventory of the 17 in-use shadcn primitives mapped to Untitled free components:

| shadcn (in use) | Untitled replacement | Source path | Notes |
|---|---|---|---|
| `avatar` | `avatar` | `base/avatar` | direct |
| `badge` | `badges` | `base/badges` | rename at consumer |
| `button` | `button` | `base/button` | direct; verify variant prop API |
| `card` | hand-rolled | n/a | ~10 LoC `div` wrapper using Untitled tokens |
| `dialog` | `modal` | `application/modal` | API differs — RAC `Modal` uses `<DialogTrigger>` + `<Modal>` + `<Dialog>` slots |
| `dropdown-menu` | `dropdown-button-simple` | `base/dropdown-button-simple` | one variant chosen from a family of ~10 |
| `form` | `hook-form` | `base/hook-form` | already integrates with `react-hook-form` |
| `input` | `input` | `base/input` | direct |
| `label` | (composed in `hook-form`/`input`) | n/a | delete file, consumers re-import |
| `separator` | `<hr>` inline | n/a | 1–2 call sites; delete file |
| `sheet` | `slideout-menu` | `application/slideout-menu` | mobile nav drawer |
| `sidebar` | `sidebar-simple` | `application/sidebar-simple` | starting point; upgradable to `-section-dividers` later |
| `skeleton` | hand-rolled | n/a | ~5 LoC `div` + `animate-pulse` |
| `sonner` | unchanged | n/a | external library, kept |
| `table` | `table` | `application/table` | RAC-based; verify column/row API matches consumer |
| `textarea` | `textarea` | `base/textarea` | direct |
| `tooltip` | `tooltip` | `base/tooltip` | direct |

## 4. Architecture (deps + config)

### 4.1 Removed

- `radix-ui` (umbrella package)
- `lucide-react`
- `web/components.json` (shadcn metadata)

### 4.2 Added

- `react-aria-components@^1.16.0`
- `@untitledui/icons@^0.0.22`
- `react-aria@^3.47.0`
- `@react-stately/utils@^3.11.0`

### 4.3 Kept

- Tailwind v4, Vite, React 19, react-router-dom, zod, react-hook-form, oidc-client-ts, react-oidc-context, openapi-fetch, @tanstack/react-query, sonner, tailwind-merge.
- `next-themes` — kept; rebound to set `data-theme` attribute instead of the shadcn class. Drop only if it conflicts with Untitled's theme mechanism in implementation.

### 4.4 Config edits

- `web/src/index.css` — replace existing `:root` CSS-var block (DESIGN.md-derived) with Untitled's token vars.
- Tailwind v4 `@theme` block (in `index.css`, since this project uses v4 inline-config) — drop slate-palette mappings; add Untitled token names.
- `web/src/lib/utils.ts` — keep `cn()` (tailwind-merge based).
- Folder layout: `web/src/components/ui/` retained as the home of owned primitives; contents replaced 1:1.

## 5. Component migration procedure

For each component:

1. Fetch via `mcp__untitledui__get_component({name, version: 8})` or `get_component_bundle` for families.
2. Write the JSON's `tsx` payload to `web/src/components/ui/<name>.tsx`.
3. Adjust import paths if the Untitled file references `@/lib/utils` or similar (it does — matches our `cn()` helper).
4. Migrate consumer imports in the same commit (no shim layer).
5. For hand-rolled `card.tsx` and `skeleton.tsx` — author from scratch using Untitled tokens; keep them in `web/src/components/ui/` for consistency.

## 6. Consumer port plan (5 files)

Implementation plan owns the line-level edits. High-level:

- **`web/src/components/layout/TopBar.tsx`** — port `avatar`, `dropdown-menu`, `separator`, `tooltip`. Swap `lucide-react` icon imports to `@untitledui/icons`.
- **`web/src/features/catalog/components/RegisterApplicationDialog.tsx`** — port `dialog`→`modal`, `form`, `input`, `textarea`, `button`. RAC `Modal` API differs from Radix `Dialog`; restructure trigger/content slots. Existing `applyProblemDetailsToForm` glue talks to react-hook-form, not the dialog wrapper, so it stays.
- **`web/src/features/catalog/pages/CatalogListPage.tsx`** — port `button`, `card` (hand-rolled), `skeleton` (hand-rolled).
- **`web/src/features/catalog/components/ApplicationsTable.tsx`** — port `table` to RAC `Table`. Re-verify sort/empty-state behavior; ADR-0088 trade-off chose not to wire TanStack Table, so the current shadcn `table` is plain — should map cleanly.
- **`web/src/features/catalog/pages/ApplicationDetailPage.tsx`** — port `card`, `badge`, `separator`. Mostly mechanical.

Cross-cutting: every file's `lucide-react` imports → `@untitledui/icons`. Resolve ambiguous names via `mcp__untitledui__search_icons`.

## 7. Theme + tokens

- `web/src/index.css`: drop existing `:root` CSS-vars, replace with Untitled's vocabulary (`--bg-primary`, `--bg-secondary`, `--fg-primary`, `--fg-secondary`, `--border-primary`, `--border-secondary`, `--utility-brand-*`, etc.). Pin actual values during implementation by reading a representative Untitled component file or `mcp__untitledui__list_components({category: "foundations"})`.
- Light/dark: `next-themes` kept; rebind to `data-theme` attribute. ~5-line change.
- Typography: Inter (already loaded). JetBrains Mono not loaded yet → no change.
- `DESIGN.md`: add header note — *"Color/typography token values are deferred to Untitled UI defaults as of 2026-05-01; this document retains nav, layout, and information-density rules."*

## 8. Testing

### 8.1 Coverage gates (vitest)

`vitest.config.ts:23` thresholds — `lines: 80, statements: 80, functions: 80, branches: 75` — kept unchanged. Migration adds component code, not measured paths (`features/**/api`, `features/**/schemas`, `shared/auth`, `shared/forms`). Re-verify after migration.

### 8.2 Test file impact

| Category | Effect |
|---|---|
| API + schemas + auth + forms | Untouched |
| Component tests (text/role/click assertions) | Survive — RAC exposes ARIA roles correctly |
| Component tests asserting shadcn-specific DOM (`data-state="open"`) | Update to RAC equivalents (`aria-expanded`, `data-pressed`) — file-by-file with each port commit |
| Smoke (`web/src/__smoke__`) | Survives; routing + auth providers unchanged |
| Backend / architecture tests | Untouched |

### 8.3 Cold-start verification (ADR-0084)

After migration: fresh `docker compose up` + cold `npm run dev`; walk `/login` → KeyCloak → `/callback` → `/catalog`; register application via dialog; verify it appears in the table. Capture console + screenshots in `docs/superpowers/evidence/2026-05-XX-untitled-ui-migration/`. Load-bearing verification — RAC overlays + portals can break in subtle real-browser ways that pass jsdom.

## 9. ADR + docs

- **New ADR-0092** — Untitled UI free-tier as primary UI primitive layer (decision, date 2026-05-01, mapping table from §3, theme decision from §7, why-changed-from-ADR-0088 paragraph).
- **ADR-0088** — set `Status: Superseded by ADR-0092`; add `Superseded-By:` link. Don't delete; history matters.
- **`docs/architecture/decisions/README.md`** — keyword index row for "Frontend UI stack" → ADR-0092.
- **`CLAUDE.md`** root project guide — update `Frontend UI stack` row in the "Key architectural decisions" table:
  - From: `shadcn/ui + Tailwind CSS v4 + Radix; ... lucide-react; nav canonical in DESIGN.md (not Stitch)`
  - To: `Untitled UI free-tier (react-aria-components + Tailwind CSS v4) + @untitledui/icons; nav canonical in DESIGN.md`. Link ADR-0092.
- **`docs/design/DESIGN.md`** — header note (per §7).
- **Slice-4 specs / `docs/ui-screens/` mockups** — left alone. Visual drift acknowledged; mockup regeneration is a future slice.

## 10. Out of scope / non-goals

- No new features; slice-4 functionality preserved 1:1.
- No backend changes; API + contracts + integration + mutation tests untouched.
- No PRO components, templates, or `get_page_template_files` calls.
- No DESIGN.md token re-mapping. Untitled defaults win. Future product-visual-identity re-skin is a separate slice.
- No test-framework migration (Vitest / RTL / jsdom stay).
- No `radix-ui` shim layer; direct cut.
- No Playwright e2e suite expansion (originally-deferred E-02.F-01 story).
- No icon search-and-replace tooling; ~10 icons enumerable manually with `search_icons`.
- No commit-time visual-regression testing.
- No mockup regeneration in `docs/ui-screens/`.

## 11. Risks + mitigations

| Risk | Mitigation |
|---|---|
| RAC `Modal` API restructure breaks `RegisterApplicationDialog`'s trigger flow | Read RAC docs; port carefully; cold-start verify the dialog open/close path in a real browser |
| Untitled token vocabulary doesn't match what we'd intuit (e.g. `--bg-primary` may mean foreground in some libs) | Pin actual values during implementation by reading a representative component file before authoring `index.css` |
| Some component test assertions hardcode shadcn `data-state` attrs | Update file-by-file as part of each port commit; the smoke test catches regressions wholesale |
| Free-tier `card` and `skeleton` hand-rolls drift from Untitled visual language over time | Keep them tiny (5–10 LoC each), token-aligned, no custom variants |
| Migration in one PR balloons unexpectedly | Pre-agreed split fallback: Section 4 architecture commit first, then per-file consumer commits — not separate PRs, just separate commits in the same branch |

## 12. Acceptance criteria

Definition-of-Done for the migration PR:

1. `web/package.json` no longer lists `radix-ui` or `lucide-react`; lists `react-aria-components`, `@untitledui/icons`, `react-aria`, `@react-stately/utils`.
2. `web/components.json` deleted.
3. `web/src/components/ui/` contains exactly: `avatar.tsx`, `badges.tsx`, `button.tsx`, `card.tsx` (hand-rolled), `modal.tsx`, `dropdown-button-simple.tsx`, `hook-form.tsx`, `input.tsx`, `slideout-menu.tsx`, `sidebar-simple.tsx`, `skeleton.tsx` (hand-rolled), `sonner.tsx` (unchanged), `table.tsx`, `textarea.tsx`, `tooltip.tsx`. `label.tsx` and `separator.tsx` removed.
4. No `lucide-react` import remaining anywhere in `web/src/`.
5. No `from "@radix-ui/*"` or `from "radix-ui"` import remaining anywhere in `web/src/`.
6. `npm run typecheck` clean.
7. `npm run test:coverage` ≥ 80% functions, ≥ 75% branches (existing thresholds).
8. `npm run build` clean.
9. Frontend CI workflow green on the PR.
10. Cold-start verification evidence committed under `docs/superpowers/evidence/<date>-untitled-ui-migration/` showing the four flows from §8.3 working in a real browser.
11. ADR-0092 written; ADR-0088 status updated; CLAUDE.md row swapped; `DESIGN.md` header note added; ADR README keyword index updated.
12. Slice-boundary `superpowers:code-reviewer` dispatched against full branch diff and findings addressed (or recorded as tracked follow-ups).

## 13. Estimated size

- Untitled component fetch + write: ~12 files × ~150 LoC each (their JSX is more verbose than shadcn; budget for it). Owned, not deps.
- Hand-rolled `card.tsx` + `skeleton.tsx`: ~15 LoC total.
- Consumer ports: 5 files, ~20–80 LoC delta each.
- `index.css` token replacement: ~80 LoC delta (replace existing var block).
- Tests: ~4 files touched, mostly attribute-name swaps.
- ADR-0092 + doc edits: ~150 LoC across 4 files.

Order-of-magnitude: ~2,500 LoC delta in `web/`, dominated by component source files. One focused day of work.

## 14. Addendum (post-CLI-spike)

After running `npx untitledui@latest add button --yes` against a scratch directory, the following points refine §3, §4, and §5:

**Install pathway:** components are installed via the Untitled UI CLI, not by writing JSON payloads to disk. The CLI:
- writes each component to `web/src/components/<category>/<group>/<name>.tsx` (e.g. `base/buttons/button.tsx`)
- creates helper utilities `web/src/lib/utils/cx.ts` and `web/src/utils/is-react-component.ts` on first install
- adds runtime npm dependencies to `package.json` automatically (e.g. `react-aria-components`, `tailwind-merge`)

**Folder layout (revised):** Untitled's native layout `web/src/components/<category>/<group>/` is adopted as-is. The pre-existing flat `web/src/components/ui/` folder is removed. Consumer imports update from `@/components/ui/<name>` → `@/components/<category>/<group>/<name>`.

**Theme tokens (revised):** sourced verbatim from Untitled's public repo at `https://raw.githubusercontent.com/untitleduico/react/main/styles/theme.css`, committed to `web/src/styles/theme.css`, imported from `index.css`. Defines brand/utility/semantic colors, typography (Inter + Roboto Mono), shadows, animations. Dark-mode toggle = `.dark-mode` class on `<html>` (rebind `next-themes` to set this class).

**Helper utility (revised):** `web/src/lib/utils.ts` (`cn()` re-export of `clsx` + `tailwind-merge`) is replaced by Untitled's `web/src/lib/utils/cx.ts` (`cx()` + `sortCx()`). Existing call sites in slice-4 update import paths.

**Acceptance criteria addendum to §12:**
- §12 #3 file list is superseded by Untitled's CLI-determined folder layout. The check becomes: "no file remains under `web/src/components/ui/` except `sonner.tsx`; every primitive imported by a consumer file resolves to a path under `web/src/components/{base,application}/...`".
- §12 #5 — also assert no `from "@/components/ui/<name>"` import remaining (except `sonner`).
- §12 — add: `web/src/styles/theme.css` exists and is imported from `index.css`; `web/src/lib/utils/cx.ts` exists; `web/src/lib/utils.ts` removed (or empty).

**Risks addendum:**
- The CLI may not respect Tailwind v4's `@theme` block presence in `index.css`; verify by running `npx untitledui add button` once during Task 1 and confirming the resulting file lints/types/builds before fanning out.
- `next-themes` dark-class binding (`class="dark"` → `class="dark-mode"`) — single config change but worth pinning with a smoke test.
