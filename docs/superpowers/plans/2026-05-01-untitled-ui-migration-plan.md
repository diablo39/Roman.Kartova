# Untitled UI Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the existing shadcn/ui + Radix UI primitive layer with Untitled UI's free-tier components on a single feature branch (big-bang PR), preserving slice-4 functionality 1:1.

**Architecture:** Untitled UI components are installed via `npx untitledui@latest add <name>` into Untitled's native folder layout (`web/src/components/<category>/<group>/`). Theme tokens come from Untitled's public `styles/theme.css`. Consumer files are ported one-at-a-time so the build stays green at every commit. Old shadcn primitives are deleted after all consumers are migrated; `radix-ui` and `lucide-react` packages are removed in the same cleanup commit.

**Tech Stack:** React 19 · TypeScript strict · Vite 6 · Tailwind CSS v4 · `react-aria-components` · `@untitledui/icons` · `react-hook-form` + `zod` · TanStack Query · `sonner` (toast, kept) · `next-themes` (rebound to `.dark-mode` class).

**Spec:** [`docs/superpowers/specs/2026-05-01-untitled-ui-migration-design.md`](../specs/2026-05-01-untitled-ui-migration-design.md)

**Branch:** `feat/untitled-ui-migration` (created in Task 1).

---

## File Structure

**Created:**
- `web/src/styles/theme.css` — Untitled token definitions (sourced from `untitleduico/react`)
- `web/src/utils/cx.ts` — `cx()` + `sortCx()` helpers (auto-created by CLI)
- `web/src/utils/is-react-component.ts` — auto-created by CLI
- `web/src/components/base/buttons/button.tsx` — and other CLI-installed primitives
- `web/src/components/base/avatars/avatar.tsx`
- `web/src/components/base/badges/badges.tsx`
- `web/src/components/base/forms/hook-form.tsx`
- `web/src/components/base/input/input.tsx`
- `web/src/components/base/textarea/textarea.tsx`
- `web/src/components/base/tooltip/tooltip.tsx`
- `web/src/components/base/dropdown/dropdown-button-simple.tsx`
- `web/src/components/application/modals/modal.tsx`
- `web/src/components/application/slideout-menus/slideout-menu.tsx`
- `web/src/components/application/app-navigation/sidebar-simple.tsx`
- `web/src/components/application/table/table.tsx`
- `web/src/components/base/card/card.tsx` — hand-rolled (~10 LoC)
- `web/src/components/base/skeleton/skeleton.tsx` — hand-rolled (~5 LoC)
- `docs/architecture/decisions/ADR-0094-untitled-ui-component-library.md`

(Exact CLI file paths confirmed in Task 3; if the CLI groups differently the plan defers to its output.)

**Modified:**
- `web/package.json` — add deps in setup, remove `radix-ui` + `lucide-react` in cleanup
- `web/src/index.css` — import `./styles/theme.css`; remove the existing slate `@theme` block
- `web/src/app/Providers.tsx` (or wherever `next-themes` is wired) — change `attribute="class"` configuration to use `.dark-mode`
- `web/src/components/layout/TopBar.tsx`
- `web/src/features/catalog/components/RegisterApplicationDialog.tsx`
- `web/src/features/catalog/components/ApplicationsTable.tsx`
- `web/src/features/catalog/pages/CatalogListPage.tsx`
- `web/src/features/catalog/pages/ApplicationDetailPage.tsx`
- `web/src/components/layout/__tests__/*.test.tsx`
- `web/src/features/catalog/components/__tests__/*.test.tsx`
- `web/src/features/catalog/pages/__tests__/*.test.tsx`
- `docs/architecture/decisions/ADR-0088-react-component-library.md` — status → Superseded
- `docs/architecture/decisions/README.md` — keyword-index row
- `CLAUDE.md` — frontend stack row
- `docs/design/DESIGN.md` — header note

**Deleted:**
- `web/src/components/ui/avatar.tsx`, `badge.tsx`, `button.tsx`, `card.tsx`, `dialog.tsx`, `dropdown-menu.tsx`, `form.tsx`, `input.tsx`, `label.tsx`, `separator.tsx`, `sheet.tsx`, `sidebar.tsx`, `skeleton.tsx`, `table.tsx`, `textarea.tsx`, `tooltip.tsx` (16 files; **`sonner.tsx` is kept**)
- `web/components.json`
- `web/src/lib/utils.ts` (replaced by `web/src/utils/cx.ts`)

---

## Task 1: Branch + ADR-0094 stub + tooling check

**Files:**
- Create: `docs/architecture/decisions/ADR-0094-untitled-ui-component-library.md`

- [ ] **Step 1: Create the feature branch**

```bash
git checkout master
git pull --ff-only
git checkout -b feat/untitled-ui-migration
```

- [ ] **Step 2: Verify the Untitled UI CLI works against the project**

```bash
cd web
npx -y untitledui@latest --version
```

Expected: prints a version string. If the command fails because npm cache has a stale entry, run `npm cache verify` and retry.

- [ ] **Step 3: Author ADR-0094 stub (Status: Proposed)**

Create `docs/architecture/decisions/ADR-0094-untitled-ui-component-library.md`:

```markdown
# ADR-0094: Untitled UI Free-Tier as Primary UI Primitive Layer

**Status:** Proposed
**Date:** 2026-05-01
**Deciders:** Roman Głogowski (solo developer)
**Category:** Frontend Architecture
**Related:** ADR-0039, ADR-0040, ADR-0084, ADR-0087, ADR-0088 (superseded by this)

## Context

Slice-4's first-cut catalog UI was built on shadcn/ui + Radix (ADR-0088). The footprint is small (5 consumer files, 17 owned primitives). The author prefers Untitled UI's visual language and is comfortable accepting Untitled's tokens as the new design-token source of truth, deferring DESIGN.md color/typography references for now.

## Decision

Adopt **Untitled UI free-tier** as the primary UI primitive layer:

- Install via `npx untitledui@latest add <name>`; components land in `web/src/components/<category>/<group>/`.
- Token vocabulary comes from `untitleduico/react/styles/theme.css` (committed to `web/src/styles/theme.css`).
- Icons via `@untitledui/icons` everywhere.
- Hand-roll only `card.tsx` and `skeleton.tsx` (not in free tier; ~15 LoC total).
- Keep `sonner` for toasts (Untitled does not ship a toast).
- Dark mode via `.dark-mode` class; rebind `next-themes` to set this class.

## Rationale

- Visual language preferred by the project owner.
- Free tier covers 15 of 17 in-use shadcn primitives; 2 are trivially hand-rolled.
- Single coherent design system; no shadcn/Untitled coexistence.
- PRO upgrade path remains available later for larger templates.

## Alternatives Considered

- **Stay on shadcn/ui (status quo, ADR-0088):** rejected — owner preference for Untitled.
- **Coexistence (shadcn for slice-4, Untitled for new screens):** rejected — slice-4 is small enough that a clean cut beats two parallel design languages.
- **Untitled UI PRO:** deferred — free tier covers slice-4 needs.

## Consequences

**Positive:**
- One coherent design language across the SPA.
- Untitled's CLI-driven workflow installs deps automatically.
- Token vocabulary maintained upstream.

**Negative / Trade-offs:**
- DESIGN.md color/typography references go silent until a future re-skin slice.
- `card` and `skeleton` are hand-rolled and may drift from Untitled's evolution.
- RAC API differs from Radix; `RegisterApplicationDialog` requires a structural restructure (not a one-line swap).
```

- [ ] **Step 4: Commit**

```bash
git add docs/architecture/decisions/ADR-0094-untitled-ui-component-library.md
git commit -m "docs(adr): ADR-0094 — Untitled UI free-tier as primary UI primitive (Proposed)"
```

---

## Task 2: Install theme tokens + helper utilities

**Files:**
- Create: `web/src/styles/theme.css`
- Modify: `web/src/index.css`

- [ ] **Step 1: Fetch Untitled's theme.css verbatim**

```bash
mkdir -p web/src/styles
curl -fsSL https://raw.githubusercontent.com/untitleduico/react/main/styles/theme.css -o web/src/styles/theme.css
```

Verify the file is non-empty:

```bash
wc -l web/src/styles/theme.css
```

Expected: file > 100 lines (full token system).

- [ ] **Step 2: Replace the slate `@theme` block in `index.css` with theme.css import**

Edit `web/src/index.css` to:

```css
@import "tailwindcss";
@import "./styles/theme.css";

html, body, #root {
  height: 100%;
  margin: 0;
}

body {
  font-family: var(--font-display, "Inter", system-ui, sans-serif);
}
```

The previous `@theme { ... slate palette ... }` block, the `:root { --sidebar-* }` block, the `.dark { ... }` block, and the `@theme inline { --color-sidebar-* }` block are all removed. Untitled's `theme.css` owns these now.

- [ ] **Step 3: Run typecheck to confirm no broken imports**

```bash
cd web && npm run typecheck
```

Expected: PASS (no `@/components/ui/*` imports broken yet — those are still in place; this step proves the CSS edit doesn't break TS).

- [ ] **Step 4: Run dev server cold, walk one screen visually**

```bash
cd web && npm run dev
```

Open `http://localhost:5173/`, accept that the visual is now broken (Untitled tokens haven't propagated to shadcn classes). This is expected — the CSS swap alone won't keep slate styling. Note any console errors. Stop the server.

- [ ] **Step 5: Commit**

```bash
git add web/src/index.css web/src/styles/theme.css
git commit -m "feat(web): import Untitled UI theme.css; remove slate @theme block (visual will be inconsistent until consumers migrate)"
```

---

## Task 3: CLI-bootstrap helper utilities + smoke-install one component

**Files (CLI-created):**
- `web/src/utils/cx.ts`
- `web/src/utils/is-react-component.ts`
- `web/src/components/base/buttons/button.tsx`

- [ ] **Step 1: Bootstrap by installing one component (button) via CLI**

```bash
cd web
npx -y untitledui@latest add button --yes
```

Expected output: `✔ button is added successfully` and `✔ component dependencies installed`.

- [ ] **Step 2: Verify the CLI created the expected files**

```bash
ls web/src/utils/cx.ts web/src/utils/is-react-component.ts web/src/components/base/buttons/button.tsx
```

Expected: all three files present. If file paths differ from the plan, update subsequent task paths to match.

- [ ] **Step 3: Verify deps were added to package.json**

```bash
grep -E "react-aria-components|tailwind-merge" web/package.json
```

Expected: `react-aria-components` listed. `tailwind-merge` was already present.

- [ ] **Step 4: Typecheck the new file**

```bash
cd web && npm run typecheck
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add web/package.json web/package-lock.json web/src/utils/ web/src/components/base/
git commit -m "feat(web): bootstrap Untitled UI CLI — install button + cx/is-react-component utilities"
```

---

## Task 4: Install remaining Untitled primitives via CLI

- [ ] **Step 1: Install all primitives in one batch**

```bash
cd web
npx -y untitledui@latest add avatar badges input textarea tooltip dropdown-button-simple hook-form modal slideout-menu sidebar-simple table --yes
```

Expected: each listed as `✔ <name> is added successfully`.

- [ ] **Step 2: Inspect the new file paths and dependencies added**

```bash
find web/src/components/base web/src/components/application -type f -name "*.tsx" 2>&1 | sort
grep -E "@untitledui/icons|react-aria@|@react-stately" web/package.json
```

Expected:
- 12 component files listed (1 from Task 3 + 11 added here).
- `@untitledui/icons` and likely `react-aria` + `@react-stately/utils` now in dependencies.

- [ ] **Step 3: Typecheck**

```bash
cd web && npm run typecheck
```

Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add web/package.json web/package-lock.json web/src/components/
git commit -m "feat(web): install Untitled primitives — avatar, badges, input, textarea, tooltip, dropdown, hook-form, modal, slideout-menu, sidebar-simple, table"
```

---

## Task 5: Hand-roll card + skeleton

**Files:**
- Create: `web/src/components/base/card/card.tsx`
- Create: `web/src/components/base/skeleton/skeleton.tsx`
- Test: `web/src/components/base/card/__tests__/card.test.tsx`
- Test: `web/src/components/base/skeleton/__tests__/skeleton.test.tsx`

- [ ] **Step 1: Write the failing test for card**

Create `web/src/components/base/card/__tests__/card.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import { describe, it, expect } from "vitest";
import { Card, CardContent } from "../card";

describe("Card", () => {
  it("renders a rounded container with border + shadow utilities and Untitled bg token", () => {
    render(
      <Card data-testid="card">
        <CardContent>hello</CardContent>
      </Card>
    );
    const card = screen.getByTestId("card");
    expect(card.className).toContain("rounded-xl");
    expect(card.className).toContain("border");
    expect(card.className).toContain("bg-primary");
    expect(screen.getByText("hello")).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

```bash
cd web && npm run test -- card.test.tsx
```

Expected: FAIL with `Cannot find module '../card'`.

- [ ] **Step 3: Implement card.tsx**

Create `web/src/components/base/card/card.tsx`:

```tsx
import type { HTMLAttributes } from "react";
import { cx } from "@/utils/cx";

export function Card({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      className={cx(
        "rounded-xl border border-secondary bg-primary shadow-xs",
        className
      )}
      {...props}
    />
  );
}

export function CardContent({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return <div className={cx("p-6", className)} {...props} />;
}

export function CardHeader({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return <div className={cx("p-6 pb-4", className)} {...props} />;
}
```

- [ ] **Step 4: Run test to verify it passes**

```bash
cd web && npm run test -- card.test.tsx
```

Expected: PASS.

- [ ] **Step 5: Write the failing test for skeleton**

Create `web/src/components/base/skeleton/__tests__/skeleton.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import { describe, it, expect } from "vitest";
import { Skeleton } from "../skeleton";

describe("Skeleton", () => {
  it("renders a pulsing block with Untitled tokens", () => {
    render(<Skeleton data-testid="sk" className="h-4 w-32" />);
    const sk = screen.getByTestId("sk");
    expect(sk.className).toContain("animate-pulse");
    expect(sk.className).toContain("bg-secondary");
    expect(sk.className).toContain("rounded-md");
  });
});
```

- [ ] **Step 6: Run test to verify it fails**

```bash
cd web && npm run test -- skeleton.test.tsx
```

Expected: FAIL with `Cannot find module '../skeleton'`.

- [ ] **Step 7: Implement skeleton.tsx**

Create `web/src/components/base/skeleton/skeleton.tsx`:

```tsx
import type { HTMLAttributes } from "react";
import { cx } from "@/utils/cx";

export function Skeleton({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return <div className={cx("animate-pulse rounded-md bg-secondary", className)} {...props} />;
}
```

- [ ] **Step 8: Run test to verify it passes**

```bash
cd web && npm run test -- skeleton.test.tsx
```

Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add web/src/components/base/card web/src/components/base/skeleton
git commit -m "feat(web): hand-roll card + skeleton primitives (not in Untitled free tier)"
```

---

## Task 6: Rebind next-themes to `.dark-mode` class

**Files:**
- Modify: `web/src/app/Providers.tsx` (or wherever `ThemeProvider` is wired — locate via `grep -rn "next-themes" web/src/`)

- [ ] **Step 1: Locate the existing next-themes wiring**

```bash
grep -rn "next-themes\|ThemeProvider\|attribute=\"class\"" web/src/ --include="*.tsx" --include="*.ts" | grep -v node_modules | grep -v __tests__
```

Note the file path that contains `attribute="class"` or imports from `next-themes`. Treat that as the "providers file" referenced below.

- [ ] **Step 2: Modify the providers file to use Untitled's class name**

Change the `ThemeProvider` props from:

```tsx
<ThemeProvider attribute="class" defaultTheme="dark" enableSystem={false}>
```

to:

```tsx
<ThemeProvider attribute="class" defaultTheme="dark" enableSystem={false} value={{ light: "", dark: "dark-mode" }}>
```

This maps the `dark` theme name to a `dark-mode` class on `<html>`, matching Untitled's `theme.css`. Light theme drops the class entirely.

- [ ] **Step 3: Typecheck + tests**

```bash
cd web && npm run typecheck && npm run test
```

Expected: typecheck PASS; all tests PASS.

- [ ] **Step 4: Commit**

```bash
git add web/src/app/Providers.tsx
git commit -m "feat(web): rebind next-themes class from `dark` to `dark-mode` (Untitled UI convention)"
```

---

## Task 7: Migrate `TopBar.tsx`

**Files:**
- Modify: `web/src/components/layout/TopBar.tsx`
- Test: `web/src/components/layout/__tests__/TopBar.test.tsx` (existing)

- [ ] **Step 1: Run the existing TopBar test (baseline)**

```bash
cd web && npm run test -- TopBar
```

Expected: PASS (baseline before migration).

- [ ] **Step 2: Resolve the icon names in `@untitledui/icons`**

Use the MCP `search_icons` tool to resolve:
- `Search` (lucide) → likely `SearchLg` or `SearchSm`
- `ChevronDown` (lucide) → likely `ChevronDown`
- `LogOut` (lucide) → likely `LogOut01` or `LogOut02`

Pin the exact names by checking via:

```bash
# Run inside Claude Code only:
# mcp__untitledui__search_icons({query: "search magnifier", limit: 5})
# mcp__untitledui__search_icons({query: "chevron down", limit: 3})
# mcp__untitledui__search_icons({query: "logout exit door arrow", limit: 5})
```

Record chosen names; use them in Step 3.

- [ ] **Step 3: Update imports + JSX in TopBar.tsx**

In `web/src/components/layout/TopBar.tsx`, replace the import block:

```tsx
// before
import { Search, ChevronDown, LogOut } from "lucide-react";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import { Button } from "@/components/ui/button";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from "@/components/ui/dropdown-menu";

// after
import { SearchLg, ChevronDown, LogOut01 } from "@untitledui/icons";
import { Avatar } from "@/components/base/avatars/avatar";
import { Badge } from "@/components/base/badges/badges";
import { Skeleton } from "@/components/base/skeleton/skeleton";
import { Button } from "@/components/base/buttons/button";
import { Dropdown } from "@/components/base/dropdown/dropdown-button-simple";
```

(Replace icon names with the ones pinned in Step 2.)

In the JSX body:
- `<Avatar><AvatarFallback>{initials}</AvatarFallback></Avatar>` → `<Avatar initials={initials} size="sm" />` (Untitled's avatar takes `initials` as a prop; verify by reading `web/src/components/base/avatars/avatar.tsx`).
- `<DropdownMenu>...<DropdownMenuTrigger>...<DropdownMenuContent>...<DropdownMenuItem>...` → Untitled `Dropdown` API; read `web/src/components/base/dropdown/dropdown-button-simple.tsx` and follow its trigger/items pattern.
- `<Search />` → `<SearchLg className="size-5 text-fg-quaternary" />` (Untitled icons take className for sizing/colour).
- All `text-foreground`, `bg-card`, `border-border` etc. classes → Untitled token equivalents (`text-primary`, `bg-primary`, `border-secondary`).

- [ ] **Step 4: Update TopBar test if assertions reference shadcn-specific DOM**

If the test contains `data-state="open"`, `aria-haspopup="menu"`, or class-name assertions tied to shadcn variants, update them to RAC equivalents. Run:

```bash
cd web && npm run test -- TopBar
```

Expected: PASS. If FAIL, update assertions to match RAC's emitted attributes (`aria-expanded`, `data-pressed`, etc.).

- [ ] **Step 5: Typecheck**

```bash
cd web && npm run typecheck
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add web/src/components/layout/TopBar.tsx web/src/components/layout/__tests__/TopBar.test.tsx
git commit -m "refactor(web): port TopBar to Untitled UI primitives + @untitledui/icons"
```

---

## Task 8: Migrate `ApplicationDetailPage.tsx`

**Files:**
- Modify: `web/src/features/catalog/pages/ApplicationDetailPage.tsx`
- Test: `web/src/features/catalog/pages/__tests__/ApplicationDetailPage.test.tsx` (existing)

- [ ] **Step 1: Run the existing test (baseline)**

```bash
cd web && npm run test -- ApplicationDetailPage
```

Expected: PASS.

- [ ] **Step 2: Update imports + JSX**

Replace import block in `web/src/features/catalog/pages/ApplicationDetailPage.tsx`:

```tsx
// before
import { Card, CardContent, CardHeader } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import { Separator } from "@/components/ui/separator";

// after
import { Card, CardContent, CardHeader } from "@/components/base/card/card";
import { Badge } from "@/components/base/badges/badges";
import { Skeleton } from "@/components/base/skeleton/skeleton";
```

Remove the `Separator` import. Replace `<Separator className="my-4" />` with `<hr className="my-4 border-secondary" />`.

In JSX, swap any `text-muted-foreground` for `text-secondary`, `text-foreground` for `text-primary`, etc.

- [ ] **Step 3: Run test + typecheck**

```bash
cd web && npm run test -- ApplicationDetailPage && npm run typecheck
```

Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add web/src/features/catalog/pages/ApplicationDetailPage.tsx
git commit -m "refactor(web): port ApplicationDetailPage to Untitled UI primitives"
```

---

## Task 9: Migrate `ApplicationsTable.tsx`

**Files:**
- Modify: `web/src/features/catalog/components/ApplicationsTable.tsx`
- Test: `web/src/features/catalog/components/__tests__/ApplicationsTable.test.tsx` (existing)

- [ ] **Step 1: Read Untitled's table.tsx to learn its API**

```bash
cat web/src/components/application/table/table.tsx | head -120
```

Note the exposed components — likely a `Table`, `TableHeader`, `TableRow`, `TableCell` family (RAC's pattern). The exact prop shape of column sorting and row keying is critical for the consumer rewrite.

- [ ] **Step 2: Run the existing test (baseline)**

```bash
cd web && npm run test -- ApplicationsTable
```

Expected: PASS.

- [ ] **Step 3: Restructure `ApplicationsTable.tsx` to RAC `Table`**

Replace the shadcn `Table`/`TableHeader`/`TableBody`/`TableRow`/`TableCell` JSX with Untitled's RAC-based equivalents. The current shadcn version uses generic `<table>` semantics; RAC's `Table` uses `<TableHeader>` + `<Column>` + `<TableBody>` + `<Row>` + `<Cell>` and an `id`-based keying scheme.

Use this template (adapt to current data shape):

```tsx
import { Link } from "react-router-dom";
import { Table, TableHeader, TableBody, Column, Row, Cell } from "@/components/application/table/table";
import { Skeleton } from "@/components/base/skeleton/skeleton";
import { Card, CardContent } from "@/components/base/card/card";
import type { ApplicationResponse } from "@/generated/openapi";

interface Props { rows: ApplicationResponse[] | undefined; isLoading: boolean }

export function ApplicationsTable({ rows, isLoading }: Props) {
  if (isLoading) {
    return (
      <Card>
        <CardContent className="space-y-3">
          <Skeleton className="h-6 w-full" />
          <Skeleton className="h-6 w-full" />
          <Skeleton className="h-6 w-full" />
        </CardContent>
      </Card>
    );
  }
  if (!rows || rows.length === 0) {
    return (
      <Card>
        <CardContent className="text-secondary">No applications yet.</CardContent>
      </Card>
    );
  }
  return (
    <Card>
      <Table aria-label="Applications">
        <TableHeader>
          <Column id="name" isRowHeader>Name</Column>
          <Column id="displayName">Display Name</Column>
          <Column id="description">Description</Column>
        </TableHeader>
        <TableBody items={rows}>
          {(row) => (
            <Row id={row.id}>
              <Cell><Link to={`/catalog/${row.id}`} className="font-medium text-primary hover:underline">{row.name}</Link></Cell>
              <Cell>{row.displayName}</Cell>
              <Cell className="text-secondary">{row.description}</Cell>
            </Row>
          )}
        </TableBody>
      </Table>
    </Card>
  );
}
```

(If Untitled's `Table` exports differ in name/case, adjust the imports — Step 1 surfaced the actual exports.)

- [ ] **Step 4: Update test assertions**

The existing test likely uses `getByRole("table")` and `getByText(...)`. RAC `Table` still emits `role="grid"` (or `role="table"` depending on RAC version); update if needed. Run:

```bash
cd web && npm run test -- ApplicationsTable
```

Expected: PASS. If FAIL on a `role` query, switch the test from `getByRole("table")` to `getByRole("grid")`.

- [ ] **Step 5: Typecheck**

```bash
cd web && npm run typecheck
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add web/src/features/catalog/components/ApplicationsTable.tsx web/src/features/catalog/components/__tests__/ApplicationsTable.test.tsx
git commit -m "refactor(web): port ApplicationsTable to Untitled UI RAC table"
```

---

## Task 10: Migrate `RegisterApplicationDialog.tsx`

This is the most invasive port — RAC `Modal` differs structurally from Radix `Dialog`.

**Files:**
- Modify: `web/src/features/catalog/components/RegisterApplicationDialog.tsx`
- Test: `web/src/features/catalog/components/__tests__/RegisterApplicationDialog.test.tsx` (existing)

- [ ] **Step 1: Read Untitled's modal.tsx + hook-form.tsx**

```bash
cat web/src/components/application/modals/modal.tsx | head -100
cat web/src/components/base/forms/hook-form.tsx | head -100
```

Pin the exposed React components (`Modal`, `Dialog`, `ModalTrigger`, etc.) and the `hook-form` integration shape.

- [ ] **Step 2: Run the existing test (baseline)**

```bash
cd web && npm run test -- RegisterApplicationDialog
```

Expected: PASS.

- [ ] **Step 3: Restructure the file**

Replace the shadcn `<Dialog>`/`<DialogTrigger>`/`<DialogContent>`/`<DialogHeader>`/`<DialogTitle>`/`<DialogDescription>`/`<DialogFooter>` tree with Untitled's `Modal` family. Update imports for `Input`, `Textarea`, `Button`, `Badge`, `Avatar` to the new paths. Replace the `Loader2` lucide icon with the appropriate `@untitledui/icons` spinner (resolve via `mcp__untitledui__search_icons({query: "loading spinner"})`).

The `applyProblemDetailsToForm` glue stays — it talks to `react-hook-form`, not the dialog wrapper. Keep its call site.

- [ ] **Step 4: Update test assertions**

The existing test asserts dialog-open behavior (`getByRole("dialog")`, `findByText("Register application")`, click `Cancel` and verify dialog closes). RAC `Modal` emits `role="dialog"` — these queries should work. If test asserts shadcn `data-state="open"`, update to `aria-modal="true"` or whatever RAC emits.

```bash
cd web && npm run test -- RegisterApplicationDialog
```

Expected: PASS.

- [ ] **Step 5: Run the smoke + auth + form tests as a sanity sweep**

```bash
cd web && npm run test
```

Expected: 72/72 (or current count) PASS.

- [ ] **Step 6: Commit**

```bash
git add web/src/features/catalog/components/RegisterApplicationDialog.tsx web/src/features/catalog/components/__tests__/RegisterApplicationDialog.test.tsx
git commit -m "refactor(web): port RegisterApplicationDialog to Untitled UI Modal + hook-form"
```

---

## Task 11: Migrate `CatalogListPage.tsx`

**Files:**
- Modify: `web/src/features/catalog/pages/CatalogListPage.tsx`
- Test: `web/src/features/catalog/pages/__tests__/CatalogListPage.test.tsx` (existing)

- [ ] **Step 1: Update imports + JSX**

Replace import block:

```tsx
// before
import { Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";

// after
import { Plus } from "@untitledui/icons";
import { Button } from "@/components/base/buttons/button";
import { Card, CardContent } from "@/components/base/card/card";
```

Update Button variant prop names if they differ from Untitled's API (`variant="default"` is shadcn — Untitled uses `variant="primary"|"secondary"|"tertiary"|"link"`).

- [ ] **Step 2: Run test + typecheck**

```bash
cd web && npm run test -- CatalogListPage && npm run typecheck
```

Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add web/src/features/catalog/pages/CatalogListPage.tsx web/src/features/catalog/pages/__tests__/CatalogListPage.test.tsx
git commit -m "refactor(web): port CatalogListPage to Untitled UI primitives"
```

---

## Task 12: Cleanup — delete shadcn primitives + remove deps

**Files:**
- Delete: `web/src/components/ui/*.tsx` except `sonner.tsx`
- Delete: `web/components.json`
- Delete: `web/src/lib/utils.ts`
- Modify: `web/package.json` (remove `radix-ui`, `lucide-react`)

- [ ] **Step 1: Verify no consumer still imports from `@/components/ui/` (except `sonner`)**

```bash
grep -rn "@/components/ui/" web/src --include="*.tsx" --include="*.ts" | grep -v sonner
```

Expected: zero matches. If any are left, return to the consumer port task that missed them.

- [ ] **Step 2: Delete shadcn primitive files (keep sonner.tsx)**

```bash
cd web/src/components/ui
rm avatar.tsx badge.tsx button.tsx card.tsx dialog.tsx dropdown-menu.tsx form.tsx input.tsx label.tsx separator.tsx sheet.tsx sidebar.tsx skeleton.tsx table.tsx textarea.tsx tooltip.tsx
ls
```

Expected: only `sonner.tsx` remains.

- [ ] **Step 3: Delete `web/components.json`**

```bash
rm web/components.json
```

- [ ] **Step 4: Delete `web/src/lib/utils.ts` and update any import sites**

```bash
grep -rn "@/lib/utils" web/src --include="*.tsx" --include="*.ts"
```

For each match, change `import { cn } from "@/lib/utils"` to `import { cx } from "@/utils/cx"` and rename `cn(` calls to `cx(`. Then:

```bash
rm web/src/lib/utils.ts
```

- [ ] **Step 5: Remove `radix-ui` and `lucide-react` from package.json**

```bash
cd web && npm uninstall radix-ui lucide-react
```

- [ ] **Step 6: Verify no remaining import**

```bash
grep -rn "from \"radix-ui\"\|from \"@radix-ui\|from \"lucide-react\"" web/src --include="*.tsx" --include="*.ts"
```

Expected: zero matches.

- [ ] **Step 7: Typecheck + tests + build**

```bash
cd web && npm run typecheck && npm run test:coverage && npm run build
```

Expected:
- Typecheck PASS.
- Tests PASS, coverage gates green (`functions ≥ 80, branches ≥ 75, lines ≥ 80, statements ≥ 80`).
- Build PASS.

- [ ] **Step 8: Commit**

```bash
git add web/src/components/ui/ web/components.json web/src/lib/utils.ts web/package.json web/package-lock.json web/src
git commit -m "chore(web): remove shadcn primitives, components.json, lib/utils.ts; uninstall radix-ui + lucide-react"
```

---

## Task 13: Update ADR-0088 + CLAUDE.md + DESIGN.md + ADR README

**Files:**
- Modify: `docs/architecture/decisions/ADR-0088-react-component-library.md`
- Modify: `docs/architecture/decisions/ADR-0094-untitled-ui-component-library.md` (status flip to Accepted)
- Modify: `docs/architecture/decisions/README.md`
- Modify: `CLAUDE.md`
- Modify: `docs/design/DESIGN.md`

- [ ] **Step 1: Flip ADR-0088 status to Superseded**

Change the `**Status:**` line in `docs/architecture/decisions/ADR-0088-react-component-library.md`:

```markdown
**Status:** Superseded by ADR-0094 (2026-05-01)
```

Add a `**Superseded-By:** ADR-0094` cross-reference line near the related-ADRs block.

- [ ] **Step 2: Flip ADR-0094 status to Accepted**

Change `Status: Proposed` to `Status: Accepted` in `docs/architecture/decisions/ADR-0094-untitled-ui-component-library.md`.

- [ ] **Step 3: Update ADR README keyword index**

In `docs/architecture/decisions/README.md`, locate the row referencing ADR-0088 in the keyword index and change the link to ADR-0094 (or add ADR-0094 alongside it as the current decision). Re-sort if the index is sorted.

- [ ] **Step 4: Update CLAUDE.md frontend stack row**

In `CLAUDE.md` (project-root file), find the row:

```markdown
| Frontend UI stack | shadcn/ui + Tailwind CSS v4 + Radix; ... ; lucide-react; nav canonical in DESIGN.md (not Stitch) | ADR-0088 |
```

Replace with:

```markdown
| Frontend UI stack | Untitled UI free-tier (react-aria-components + Tailwind CSS v4) + @untitledui/icons; nav canonical in DESIGN.md | ADR-0094 |
```

- [ ] **Step 5: Add the DESIGN.md header note**

Edit the top of `docs/design/DESIGN.md`. Insert (immediately under the title):

```markdown
> **Note (2026-05-01):** Color and typography token values are deferred to Untitled UI defaults per ADR-0094. This document retains nav structure, layout density, and information-density rules — those remain canonical.
```

- [ ] **Step 6: Commit**

```bash
git add docs/ CLAUDE.md
git commit -m "docs: ADR-0088 superseded by ADR-0094; CLAUDE.md stack row + DESIGN.md note"
```

---

## Task 14: Cold-start Playwright verification (ADR-0084)

**Files:**
- Create: `docs/superpowers/evidence/2026-05-01-untitled-ui-migration/console.log`
- Create: `docs/superpowers/evidence/2026-05-01-untitled-ui-migration/login.png`
- Create: `docs/superpowers/evidence/2026-05-01-untitled-ui-migration/catalog-list.png`
- Create: `docs/superpowers/evidence/2026-05-01-untitled-ui-migration/register-dialog.png`
- Create: `docs/superpowers/evidence/2026-05-01-untitled-ui-migration/application-detail.png`

- [ ] **Step 1: Cold-start the docker compose stack**

```bash
docker compose down
docker compose up -d --build
```

Wait until `curl -sf http://localhost:8080/health/ready` returns 200.

- [ ] **Step 2: Cold-start the dev server**

In a separate terminal:

```bash
cd web && rm -rf node_modules/.vite .vite && npm run dev
```

Wait for the URL to appear.

- [ ] **Step 3: Walk the four flows via Playwright MCP**

Use `mcp__playwright__browser_navigate` + `_snapshot` + `_take_screenshot` to:

1. Navigate to `/login`, capture screenshot → `login.png`.
2. Sign in as `admin@orga.kartova.local` / `dev_pass`. Land on `/catalog`. Capture → `catalog-list.png`.
3. Click "Register application", fill the form (`name=smoke-cleanup`, `displayName=Smoke Cleanup`, `description=verification`), capture → `register-dialog.png`. Submit; verify the row appears.
4. Click into the new application detail page, capture → `application-detail.png`.

After each step capture console messages via `mcp__playwright__browser_console_messages`. Save all messages into `console.log` (concatenated).

- [ ] **Step 4: Tear down**

```bash
docker compose down
# stop the dev server (Ctrl-C in its terminal)
```

- [ ] **Step 5: Commit evidence**

```bash
git add docs/superpowers/evidence/2026-05-01-untitled-ui-migration/
git commit -m "docs(evidence): cold-start Playwright walk after Untitled UI migration"
```

---

## Task 15: Final DoD verification + slice-boundary code review

- [ ] **Step 1: Backend build with TWAE (sanity — should be untouched)**

```bash
cmd //c "dotnet build Kartova.slnx -c Release -p:TreatWarningsAsErrors=true"
```

Expected: 0 warnings, 0 errors.

- [ ] **Step 2: Backend tests (sanity — should be untouched)**

```bash
cmd //c "dotnet test Kartova.slnx -c Release --no-build --verbosity minimal"
```

Expected: all 199 (or current count) tests PASS.

- [ ] **Step 3: Frontend full verification**

```bash
cd web && npm run typecheck && npm run lint && npm run test:coverage && npm run build
```

Expected: all PASS; coverage gates met.

- [ ] **Step 4: Push branch + open PR**

```bash
git push -u origin feat/untitled-ui-migration
gh pr create --title "feat(web): migrate to Untitled UI free-tier (supersede ADR-0088)" --body "$(cat <<'EOF'
## Summary
- Replaces shadcn/ui + Radix UI + lucide-react with Untitled UI free-tier (react-aria-components + @untitledui/icons).
- Folder layout `web/src/components/<category>/<group>/` per Untitled CLI default.
- Token vocabulary sourced from upstream `untitleduico/react/styles/theme.css`.
- Hand-rolled `card` + `skeleton` primitives (~15 LoC); `sonner` toast kept external.
- All 5 slice-4 consumers (`TopBar`, `ApplicationsTable`, `RegisterApplicationDialog`, `CatalogListPage`, `ApplicationDetailPage`) ported.
- ADR-0088 superseded by ADR-0094; CLAUDE.md frontend row swapped; DESIGN.md note added.

## Test plan
- [x] Backend build TWAE 0/0 (untouched)
- [x] Backend tests 199/199 PASS (untouched)
- [x] Frontend typecheck + lint + test:coverage + build all green
- [x] Cold-start Playwright walkthrough — login → catalog → register-application → detail page (evidence committed under `docs/superpowers/evidence/2026-05-01-untitled-ui-migration/`)
- [x] No `from "radix-ui"`, `from "@radix-ui"`, or `from "lucide-react"` imports remain
- [x] No `@/components/ui/*` imports remain except `sonner`

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

- [ ] **Step 5: Dispatch slice-boundary code review**

Use `superpowers:code-reviewer` (or `superpowers:requesting-code-review` template) against the full diff `master..HEAD` with the spec + plan as context. Address each Critical/Important finding in a follow-up commit on the same branch; record Minor as tracked follow-ups in the PR body.

- [ ] **Step 6: Wait for CI green; merge when reviewer approves**

(User confirms merge.)

---

## Self-review checklist

This section is reviewed by the plan author after writing, not by the engineer:

1. **Spec coverage:** Every spec section maps to a task —
   - §1 (Goal) → Task 7-11 (consumer ports preserve slice-4 behavior).
   - §2 (Constraints) → Task 1 (branch) + Task 2 (theme) + Task 3-5 (CLI install + hand-rolls) + Task 6 (next-themes) + Task 12 (cleanup).
   - §3 (Free-tier coverage) → Task 4 (CLI install) + Task 5 (hand-rolls).
   - §4 (Architecture deps + config) → Task 2 + Task 12.
   - §5 (Component migration procedure) → Task 3-5.
   - §6 (Consumer port plan) → Task 7-11.
   - §7 (Theme + tokens) → Task 2 + Task 6.
   - §8 (Testing) → Task 7-11 (per-file test updates) + Task 12 (full coverage check) + Task 14 (Playwright cold-start).
   - §9 (ADR + docs) → Task 1 (ADR stub) + Task 13 (finalize).
   - §10 (Out of scope) → enforced by the absence of corresponding tasks.
   - §11 (Risks) → Task 3 (CLI smoke verifies before fan-out); Task 6 (next-themes class); Task 9-10 (RAC API restructure).
   - §12 (Acceptance criteria) → Task 12 + Task 14 + Task 15.
   - §13 (Estimated size) → reflected in 15 tasks.
   - §14 (Addendum) → Task 1-5 reflect the CLI-driven workflow.

2. **Placeholder scan:** No "TBD", "TODO", "implement later", "fill in details", "handle edge cases" found. The "icon name resolution via `search_icons` MCP" in Task 7 Step 2 is a deliberate runtime probe, not a placeholder — the engineer runs the tool to pin the exact name.

3. **Type consistency:** Component import paths consistent across tasks (`@/components/base/buttons/button`, `@/components/application/modals/modal`, etc.). `cx` (not `cn`) used uniformly after Task 12. `dark-mode` class used uniformly after Task 6.
