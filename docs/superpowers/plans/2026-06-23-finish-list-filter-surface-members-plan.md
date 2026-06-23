# Members onto FilterBar + single-select control (sub-slice 1) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the deferred `single-select` `<FilterBar>` control and move the Members list (`/members`) onto the standard ADR-0107 filter surface — role (single-select) + name/email search (text), submit-driven and URL-backed — deleting the bespoke `<select>` + debounced `<input>`.

**Architecture:** Approach A (typed `useListUrlState` axes). `single-select` rides the **existing `textFilters` axis** (its URL form is a plain string param), so no new URL axis this sub-slice. Controls stay **uncontrolled + committed-on-Search via `FormData`** (PR #39 model): the role Select uses react-aria's `name`/hidden form control, so `FormData.get("role")` returns the selected value. `useListFilters` treats `single-select` exactly like `text`.

**Tech Stack:** React 19 + TypeScript, react-aria-components (ADR-0094), Tailwind v4, TanStack Query, react-router-dom 7, Vitest + Testing Library + `@testing-library/user-event`.

## Global Constraints

- **Branch:** `feat/filter-surface-members-single-select` (already created; spec committed).
- **Frontend-only.** No backend/contract/codegen change — `GET /api/v1/organizations/users` already binds `role` + `q`. **No real-seam tests** (not a wiring slice).
- **Submit-driven.** All `<FilterBar>` controls commit on Search/Enter via one atomic `urlState.setFilters({ text, booleans })`. No live/debounced filtering.
- **Uncontrolled controls**, each keyed by its committed value (`key={`${spec.key}:${committed}`}`) so external changes (Search / back-forward / shared link / Clear all) re-seed them.
- **"All roles" = option `value: ""`** → commit drops empty → `role` param absent.
- **`q` searches name AND email** (unchanged backend semantics); blank ⇒ absent; **no ≥2-char minimum**.
- **Gates per task:** `npm run typecheck` (0 errors), `npm run lint` (0 errors), the task's Vitest file green. **Mutation gate N/A** (TypeScript only). Final: full `npx vitest run` + `npm run build` green; `scripts/ci-local.sh frontend`.
- **Commit after each task.** Run all `npm`/`npx` commands from the `web/` directory.

---

## File Structure

- `web/src/components/base/select/select.tsx` *(create)* — reusable react-aria `Select`; one responsibility: a single-choice dropdown that participates in native form submission via `name`.
- `web/src/components/base/select/__tests__/select.test.tsx` *(create)* — behavior of the base Select.
- `web/src/lib/list/filters/types.ts` *(modify)* — `single-select` variant gains `options`.
- `web/src/lib/list/filters/useListFilters.ts` *(modify)* — derive `single-select` like `text`.
- `web/src/components/application/filter-bar/FilterBar.tsx` *(modify)* — `single-select` render branch + commit.
- `web/src/features/members/pages/MembersListPage.tsx` *(modify — rewrite filter surface)*.
- `docs/design/list-filter-registry.md` *(modify)* — Members row + control-availability note.
- Tests modified: `FilterBar` test (single-select), `useListFilters` test (single-select), `MembersListPage` test (rewrite).

---

## Task 1: Base `Select` component

**Files:**
- Create: `web/src/components/base/select/select.tsx`
- Test: `web/src/components/base/select/__tests__/select.test.tsx`

**Interfaces:**
- Produces: `Select` (default export-style named export) with props `{ name?: string; "aria-label"?: string; label?: string; options: SelectOption[]; defaultSelectedKey?: string; selectedKey?: string | null; onSelectionChange?: (key: Key) => void; placeholder?: string; size?: "sm" | "md"; className?: string; ref?: Ref<HTMLDivElement> }`; and `interface SelectOption { label: string; value: string }`. The option `value` is used as the react-aria key, so it is the value `FormData` reports for `name`. An option with `value: ""` is a valid selectable "none" choice.

- [ ] **Step 1: Write the failing test**

```tsx
// web/src/components/base/select/__tests__/select.test.tsx
import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Select } from "../select";

const OPTIONS = [
  { label: "All roles", value: "" },
  { label: "Viewer", value: "Viewer" },
  { label: "Member", value: "Member" },
];

function formOf(container: HTMLElement) {
  return container.querySelector("form") as HTMLFormElement;
}

describe("Select (base)", () => {
  // NOTE: a react-aria Select trigger's accessible name is "<value> <label>"
  // (e.g. "Viewer Role"), not just the aria-label — so query the single trigger
  // by role and assert the displayed value via toHaveTextContent, never by an
  // exact { name: "Role" }.
  it("seeds the displayed value from defaultSelectedKey", () => {
    render(<Select aria-label="Role" options={OPTIONS} defaultSelectedKey="Viewer" />);
    expect(screen.getByRole("button")).toHaveTextContent("Viewer");
  });

  it("reports the empty default ('All roles') as an empty FormData value", () => {
    const { container } = render(
      <form><Select name="role" aria-label="Role" options={OPTIONS} defaultSelectedKey="" /></form>,
    );
    expect(new FormData(formOf(container)).get("role")).toBe("");
    expect(screen.getByRole("button")).toHaveTextContent("All roles");
  });

  it("selecting an option updates the FormData value", async () => {
    const { container } = render(
      <form><Select name="role" aria-label="Role" options={OPTIONS} defaultSelectedKey="" /></form>,
    );
    await userEvent.click(screen.getByRole("button"));
    await userEvent.click(await screen.findByRole("option", { name: "Member" }));
    expect(new FormData(formOf(container)).get("role")).toBe("Member");
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run (from `web/`): `npx vitest run src/components/base/select`
Expected: FAIL — `Cannot find module '../select'`.

- [ ] **Step 3: Write the component**

```tsx
// web/src/components/base/select/select.tsx
import type { Key, Ref } from "react";
import { ChevronDown } from "@untitledui/icons";
import {
  Select as AriaSelect,
  Button as AriaButton,
  Popover as AriaPopover,
  ListBox as AriaListBox,
  ListBoxItem as AriaListBoxItem,
  SelectValue,
} from "react-aria-components";
import { cx } from "@/lib/utils/cx";

export interface SelectOption {
  label: string;
  value: string;
}

export interface SelectProps {
  /** Form field name. react-aria renders a hidden form control so the selected
   *  option `value` is captured by `FormData` (the uncontrolled commit path). */
  name?: string;
  "aria-label"?: string;
  label?: string;
  options: SelectOption[];
  /** Uncontrolled initial selection (an option `value`). */
  defaultSelectedKey?: string;
  /** Controlled selection (an option `value`). */
  selectedKey?: string | null;
  onSelectionChange?: (key: Key | null) => void;
  placeholder?: string;
  size?: "sm" | "md";
  className?: string;
  ref?: Ref<HTMLDivElement>;
}

export const Select = ({
  name,
  label,
  options,
  defaultSelectedKey,
  selectedKey,
  onSelectionChange,
  placeholder = "Select…",
  size = "sm",
  className,
  ref,
  ...props
}: SelectProps) => {
  return (
    <AriaSelect
      ref={ref}
      name={name}
      aria-label={props["aria-label"]}
      defaultSelectedKey={defaultSelectedKey}
      selectedKey={selectedKey}
      onSelectionChange={onSelectionChange}
      placeholder={placeholder}
      className={cx("flex w-full flex-col gap-1.5", className)}
    >
      {label && <span className="text-sm font-medium text-secondary">{label}</span>}
      <AriaButton
        className={cx(
          "flex w-full cursor-pointer items-center justify-between gap-2 rounded-lg bg-primary text-primary shadow-xs ring-1 ring-primary outline-hidden transition-shadow duration-100 ease-linear ring-inset data-focus-visible:ring-2 data-focus-visible:ring-brand",
          size === "sm" ? "px-3 py-2 text-sm" : "px-3 py-2.5 text-md",
        )}
      >
        <SelectValue className="truncate data-placeholder:text-placeholder" />
        <ChevronDown aria-hidden className="size-4 shrink-0 text-fg-quaternary" />
      </AriaButton>
      <AriaPopover className="max-h-60 w-(--trigger-width) overflow-auto rounded-lg bg-primary py-1 shadow-lg ring-1 ring-secondary">
        <AriaListBox items={options} className="outline-hidden">
          {(item) => (
            <AriaListBoxItem
              id={item.value}
              textValue={item.label}
              className="flex cursor-pointer items-center px-3 py-2 text-sm text-primary outline-hidden select-none data-focused:bg-secondary data-selected:font-medium"
            >
              {item.label}
            </AriaListBoxItem>
          )}
        </AriaListBox>
      </AriaPopover>
    </AriaSelect>
  );
};

Select.displayName = "Select";
```

- [ ] **Step 4: Run test to verify it passes**

Run (from `web/`): `npx vitest run src/components/base/select`
Expected: PASS (3 tests).

> If react-aria emits a console warning about an empty-string key, it is non-fatal and the FormData assertions still hold; do not switch to a sentinel unless a test actually fails.

- [ ] **Step 5: Typecheck, lint, commit**

Run (from `web/`): `npm run typecheck && npm run lint`
Expected: 0 errors.

```bash
git add web/src/components/base/select/select.tsx web/src/components/base/select/__tests__/select.test.tsx
git commit -m "feat(web): base Select control (react-aria, FormData-readable)"
```

---

## Task 2: `FilterSpec` `options` + `useListFilters` single-select

**Files:**
- Modify: `web/src/lib/list/filters/types.ts`
- Modify: `web/src/lib/list/filters/useListFilters.ts:21` (the `textSpecs` filter)
- Test: `web/src/lib/list/filters/__tests__/useListFilters.test.tsx`

**Interfaces:**
- Consumes: `urlState.textFilters` (a `Record<string,string>`), already produced by `useListUrlState`.
- Produces: `FilterSpec` single-select variant now `{ key: string; type: "single-select"; label: string; options: { label: string; value: string }[] }`. `useListFilters` returns the same shape `{ queryFilters, isActive, activeCount }`; a `single-select` key appears in `queryFilters` as `string | undefined` and counts toward `isActive`/`activeCount` when non-empty (identical to `text`).

- [ ] **Step 1: Update `FilterSpec` (types.ts)**

Replace the reserved-union member so `single-select` carries options:

```ts
// web/src/lib/list/filters/types.ts
/** Declarative filter descriptor rendered by <FilterBar> (ADR-0107). */
export type FilterSpec =
  | { key: string; type: "text"; label: string; placeholder?: string }
  | { key: string; type: "boolean"; label: string }
  | { key: string; type: "single-select"; label: string; options: { label: string; value: string }[] }
  // Reserved per ADR-0107 clause 1 — typed now, built when a screen needs them.
  | { key: string; type: "multi-select" | "date-range"; label: string };
```

- [ ] **Step 2: Write the failing test (append to the existing describe)**

Add to `web/src/lib/list/filters/__tests__/useListFilters.test.tsx`:

```tsx
const selectSpecs: FilterSpec[] = [
  {
    key: "role",
    type: "single-select",
    label: "Role",
    options: [
      { label: "All roles", value: "" },
      { label: "Viewer", value: "Viewer" },
    ],
  },
];

describe("useListFilters — single-select", () => {
  it("exposes a committed single-select value as a queryFilter string", () => {
    const { result } = renderHook(() =>
      useListFilters(selectSpecs, { textFilters: { role: "Viewer" }, booleanFilters: {} }),
    );
    expect(result.current.queryFilters.role).toBe("Viewer");
    expect(result.current.isActive).toBe(true);
    expect(result.current.activeCount).toBe(1);
  });

  it("treats a blank single-select as undefined / inactive", () => {
    const { result } = renderHook(() =>
      useListFilters(selectSpecs, { textFilters: { role: "" }, booleanFilters: {} }),
    );
    expect(result.current.queryFilters.role).toBeUndefined();
    expect(result.current.isActive).toBe(false);
  });
});
```

> Match the file's existing import style for `renderHook`, `FilterSpec`, and `useListFilters`. If the file lacks a `renderHook` import, add it from `@testing-library/react`.

- [ ] **Step 3: Run test to verify it fails**

Run (from `web/`): `npx vitest run src/lib/list/filters`
Expected: FAIL — `queryFilters.role` is `undefined` even when committed (single-select not yet derived).

- [ ] **Step 4: Implement — fold single-select into the text-like derivation**

In `web/src/lib/list/filters/useListFilters.ts`, change the `textSpecs` filter (line 21) so single-select is derived from `textFilters` exactly like text:

```ts
  const textSpecs = useMemo(
    () => specs.filter(s => s.type === "text" || s.type === "single-select"),
    [specs],
  );
```

No other change — `queryFilters`, `isActive`, and `activeCount` already iterate `textSpecs` against `committedText`.

- [ ] **Step 5: Run tests to verify they pass**

Run (from `web/`): `npx vitest run src/lib/list/filters`
Expected: PASS (existing + 2 new).

- [ ] **Step 6: Typecheck, lint, commit**

Run (from `web/`): `npm run typecheck && npm run lint`
Expected: 0 errors.

```bash
git add web/src/lib/list/filters/types.ts web/src/lib/list/filters/useListFilters.ts web/src/lib/list/filters/__tests__/useListFilters.test.tsx
git commit -m "feat(web): useListFilters handles single-select (rides textFilters axis)"
```

---

## Task 3: `<FilterBar>` single-select branch + commit

**Files:**
- Modify: `web/src/components/application/filter-bar/FilterBar.tsx`
- Test: `web/src/components/application/filter-bar/__tests__/FilterBar.test.tsx`

**Interfaces:**
- Consumes: `Select` + `SelectOption` (Task 1); `FilterSpec.single-select.options` (Task 2); the existing `commit()` `text` map and `urlState.setFilters`.
- Produces: `<FilterBar>` renders a `Select` for `type: "single-select"` and commits its value through the `text` map (so it flows to the URL via `setFilters({ text })`). `multi-select`/`date-range` still throw.

- [ ] **Step 1: Write the failing tests (append to FilterBar.test.tsx)**

```tsx
const selectSpecs: FilterSpec[] = [
  {
    key: "role",
    type: "single-select",
    label: "Role",
    options: [
      { label: "All roles", value: "" },
      { label: "Viewer", value: "Viewer" },
      { label: "OrgAdmin", value: "OrgAdmin" },
    ],
  },
];

// The react-aria Select trigger is the only button with aria-haspopup="listbox";
// its accessible name is "<value> <label>", so locate it structurally (not by an
// exact { name: "Role" }, which would never match "All roles Role").
const selectTrigger = () =>
  screen.getAllByRole("button").find(b => b.getAttribute("aria-haspopup") === "listbox") as HTMLElement;

describe("FilterBar — single-select", () => {
  it("renders a Select for a single-select spec", () => {
    render(<FilterBar specs={selectSpecs} urlState={fakeUrlState()} />);
    expect(selectTrigger()).toBeInTheDocument();
  });

  it("choosing a value + Search commits it via setFilters (text map)", async () => {
    const setFilters = vi.fn();
    render(<FilterBar specs={selectSpecs} urlState={fakeUrlState({}, {}, setFilters)} />);
    await userEvent.click(selectTrigger());
    await userEvent.click(await screen.findByRole("option", { name: "OrgAdmin" }));
    await userEvent.click(screen.getByRole("button", { name: /^search$/i }));
    expect(setFilters).toHaveBeenCalledWith(
      expect.objectContaining({ text: expect.objectContaining({ role: "OrgAdmin" }) }),
    );
  });

  it("seeds the Select from the committed value", () => {
    render(<FilterBar specs={selectSpecs} urlState={fakeUrlState({ role: "Viewer" })} />);
    expect(selectTrigger()).toHaveTextContent("Viewer");
  });
});
```

Also **replace** the existing `it("throws for single-select type", …)` test (single-select is now built) with a date-range guard if not already covered:

```tsx
  it("throws for multi-select type", () => {
    const bad: FilterSpec[] = [{ key: "x", type: "multi-select", label: "X" }];
    expect(() => render(<FilterBar specs={bad} urlState={fakeUrlState()} />)).toThrow(/not implemented/i);
  });
```

> Ensure `userEvent` is imported in this test file (`import userEvent from "@testing-library/user-event"`). The `fakeUrlState` helper already exists in this file.

- [ ] **Step 2: Run tests to verify they fail**

Run (from `web/`): `npx vitest run src/components/application/filter-bar`
Expected: FAIL — single-select currently throws "not implemented".

- [ ] **Step 3: Implement the branch + commit**

In `web/src/components/application/filter-bar/FilterBar.tsx`:

a) Add the import:
```tsx
import { Select } from "@/components/base/select/select";
```

b) In `commit()`, fold single-select into the text branch:
```tsx
    for (const s of specs) {
      if (s.type === "text" || s.type === "single-select") {
        text[s.key] = String(data.get(s.key) ?? "");
      } else if (s.type === "boolean") {
        booleans[s.key] = data.get(s.key) != null;
      }
    }
```

c) Add the render branch inside the `specs.map(...)`, before the `throw`:
```tsx
            if (spec.type === "single-select") {
              const committed = committedText[spec.key] ?? "";
              return (
                <div key={`${spec.key}:${committed}`} className="w-full sm:w-56">
                  <Select
                    name={spec.key}
                    defaultSelectedKey={committed}
                    aria-label={spec.label}
                    options={spec.options}
                    size="sm"
                  />
                </div>
              );
            }
```

d) Update the throw message to reflect the built set:
```tsx
            throw new Error(
              `FilterBar: "${spec.type}" control not implemented (ADR-0107 clause 1 — text + boolean + single-select only)`,
            );
```

- [ ] **Step 4: Run tests to verify they pass**

Run (from `web/`): `npx vitest run src/components/application/filter-bar`
Expected: PASS (existing + single-select; multi-select still throws).

- [ ] **Step 5: Typecheck, lint, commit**

Run (from `web/`): `npm run typecheck && npm run lint`
Expected: 0 errors.

```bash
git add web/src/components/application/filter-bar/FilterBar.tsx web/src/components/application/filter-bar/__tests__/FilterBar.test.tsx
git commit -m "feat(web): FilterBar single-select control (ADR-0107)"
```

---

## Task 4: Members list onto `<FilterBar>` + registry

**Files:**
- Modify (rewrite filter surface): `web/src/features/members/pages/MembersListPage.tsx`
- Modify: `docs/design/list-filter-registry.md`
- Test: `web/src/features/members/pages/__tests__/MembersListPage.test.tsx`

**Interfaces:**
- Consumes: `<FilterBar>` single-select (Task 3); `useListFilters` (Task 2); `useListUrlState` `textFilters` axis; `useMembersList({ sortBy, sortOrder, role?, q? })` (unchanged).
- Produces: `/members` filters (`role`, `q`) render through `<FilterBar>`, are submit-driven + URL-backed, and thread into `useMembersList`.

- [ ] **Step 1: Rewrite the existing Members test**

The current second test drives a native `<select>` via `userEvent.selectOptions` — that control is gone. Replace it with the react-aria Select + Search flow. Edit `web/src/features/members/pages/__tests__/MembersListPage.test.tsx`:

- `userEvent` is already imported.
- Add a `selectTrigger` helper near the top of the file — the react-aria Select trigger is the only button with `aria-haspopup="listbox"`, and its accessible name is `"<value> <label>"`, so locate it structurally rather than by name:
```ts
const selectTrigger = () =>
  screen.getAllByRole("button").find(b => b.getAttribute("aria-haspopup") === "listbox") as HTMLElement;
```
- Replace the test `it("passes role to apiClient.GET when role filter is changed", …)` with:

```tsx
  it("passes role to apiClient.GET when a role is selected and Search is clicked", async () => {
    mockPermissions([KartovaPermissions.OrgUsersRead]);

    const get = vi.fn().mockResolvedValue({ data: pageOf([]), error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<MembersListPage />, { wrapper: harness(qc) });

    await waitFor(() => expect(get).toHaveBeenCalled());
    get.mockClear();

    await userEvent.click(selectTrigger());
    await userEvent.click(await screen.findByRole("option", { name: "OrgAdmin" }));
    await userEvent.click(screen.getByRole("button", { name: /^search$/i }));

    await waitFor(() =>
      expect(get).toHaveBeenCalledWith(
        "/api/v1/organizations/users",
        expect.objectContaining({
          params: expect.objectContaining({
            query: expect.objectContaining({ role: "OrgAdmin" }),
          }),
        }),
      ),
    );
  });

  it("shows the filtered empty-state when a filter is active and no rows match", async () => {
    mockPermissions([KartovaPermissions.OrgUsersRead]);
    const get = vi.fn().mockResolvedValue({ data: pageOf([]), error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<MembersListPage />, { wrapper: harness(qc) });
    await waitFor(() => expect(get).toHaveBeenCalled());

    await userEvent.click(selectTrigger());
    await userEvent.click(await screen.findByRole("option", { name: "OrgAdmin" }));
    await userEvent.click(screen.getByRole("button", { name: /^search$/i }));

    await waitFor(() =>
      expect(screen.getByText(/no members match your filters/i)).toBeInTheDocument(),
    );
  });
```

The first test (`renders member row …`) and third (`hides … buttons …`) stay as-is — they don't touch the removed `<select>`.

- [ ] **Step 2: Run tests to verify they fail**

Run (from `web/`): `npx vitest run src/features/members/pages`
Expected: FAIL — no `Role` Select / no `Search` button yet (page still has the native `<select>`).

- [ ] **Step 3: Rewrite `MembersListPage.tsx`**

Replace the whole file with:

```tsx
import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { Button } from "@/components/base/buttons/button";
import { Card, CardContent } from "@/components/base/card/card";
import { FilterBar } from "@/components/application/filter-bar/FilterBar";
import { useListFilters } from "@/lib/list/filters/useListFilters";
import type { FilterSpec } from "@/lib/list/filters/types";
import { useMembersList } from "@/features/members/api/members";
import { ChangeMemberRoleDialog } from "@/features/members/components/ChangeMemberRoleDialog";
import { OffboardMemberConfirmDialog } from "@/features/members/components/OffboardMemberConfirmDialog";
import { useListUrlState } from "@/lib/list/useListUrlState";
import { usePermissions } from "@/shared/auth/usePermissions";
import { KartovaPermissions } from "@/shared/auth/permissions";

const ALLOWED_SORT_FIELDS = ["displayName", "role", "createdAt"] as const;
const TEXT_FILTERS = ["role", "q"] as const;
const FILTER_SPECS: FilterSpec[] = [
  {
    key: "role",
    type: "single-select",
    label: "Role",
    options: [
      { label: "All roles", value: "" },
      { label: "Viewer", value: "Viewer" },
      { label: "Member", value: "Member" },
      { label: "OrgAdmin", value: "OrgAdmin" },
    ],
  },
  { key: "q", type: "text", label: "Search members", placeholder: "Search by name or email…" },
];

export function MembersListPage() {
  const urlState = useListUrlState({
    defaultSortBy: "displayName",
    defaultSortOrder: "asc",
    allowedSortFields: ALLOWED_SORT_FIELDS,
    textFilters: TEXT_FILTERS,
  });
  const filters = useListFilters(FILTER_SPECS, urlState);

  const list = useMembersList({
    sortBy: urlState.sortBy,
    sortOrder: urlState.sortOrder,
    role: filters.queryFilters.role as string | undefined,
    q: filters.queryFilters.q as string | undefined,
  });

  const { hasPermission, isLoading: permissionsLoading } = usePermissions();
  const canChangeRole = !permissionsLoading && hasPermission(KartovaPermissions.OrgUsersRoleChange);
  const canRemove = !permissionsLoading && hasPermission(KartovaPermissions.OrgUsersRemove);

  const [roleTarget, setRoleTarget] = useState<{ userId: string; role: string } | null>(null);
  const [offboardTarget, setOffboardTarget] = useState<{ userId: string; displayName: string } | null>(null);

  useEffect(() => {
    if (list.isError) {
      console.error("MembersListPage list error", list.error);
    }
  }, [list.isError, list.error]);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h2 className="text-2xl font-semibold text-primary">Members</h2>
      </div>

      <FilterBar specs={FILTER_SPECS} urlState={urlState} />

      {list.isError ? (
        <Card className="mx-auto max-w-md">
          <CardContent className="space-y-3 p-6 text-center">
            <p className="text-base font-medium text-error-primary">Failed to load members</p>
            <p className="text-sm text-tertiary">Try refreshing or resetting the list.</p>
            <Button size="sm" onClick={() => list.reset()}>Reset</Button>
          </CardContent>
        </Card>
      ) : list.isLoading ? (
        <Card className="mx-auto max-w-md">
          <CardContent className="p-6 text-center text-sm text-tertiary">Loading…</CardContent>
        </Card>
      ) : list.items.length === 0 ? (
        <Card className="mx-auto max-w-md text-center">
          <CardContent className="space-y-2 p-8">
            <p className="text-base font-medium text-primary">
              {filters.isActive ? "No members match your filters" : "No members yet"}
            </p>
            <p className="text-sm text-tertiary">
              {filters.isActive ? "Try a different role or search." : "Invite users to add members."}
            </p>
          </CardContent>
        </Card>
      ) : (
        <div className="overflow-hidden rounded-xl bg-primary shadow-xs ring-1 ring-secondary">
          <table className="w-full text-left text-sm">
            <thead className="bg-secondary text-xs uppercase tracking-wide text-tertiary">
              <tr>
                <th className="px-4 py-3 font-medium">Name</th>
                <th className="px-4 py-3 font-medium">Email</th>
                <th className="px-4 py-3 font-medium">Role</th>
                <th className="px-4 py-3 font-medium">Teams</th>
                <th className="px-4 py-3 font-medium">Last seen</th>
                {(canChangeRole || canRemove) && <th className="px-4 py-3" />}
              </tr>
            </thead>
            <tbody className="divide-y divide-secondary">
              {list.items.map(m => (
                <tr key={m.id} className="hover:bg-primary_hover">
                  <td className="px-4 py-3">
                    <Link to={`/users/${m.id}`} className="font-medium text-primary hover:underline">
                      {m.displayName}
                    </Link>
                  </td>
                  <td className="px-4 py-3 text-tertiary">{m.email}</td>
                  <td className="px-4 py-3 text-primary">{m.role}</td>
                  <td className="px-4 py-3 text-tertiary">{m.teamCount}</td>
                  <td className="px-4 py-3 text-tertiary">
                    {m.lastSeenAt ? new Date(m.lastSeenAt).toLocaleDateString() : "—"}
                  </td>
                  {(canChangeRole || canRemove) && (
                    <td className="px-4 py-3 text-right">
                      <div className="flex justify-end gap-2">
                        {canChangeRole && (
                          <Button size="sm" color="secondary" onClick={() => setRoleTarget({ userId: m.id, role: m.role })}>
                            Change role
                          </Button>
                        )}
                        {canRemove && (
                          <Button size="sm" color="secondary" onClick={() => setOffboardTarget({ userId: m.id, displayName: m.displayName })}>
                            Remove
                          </Button>
                        )}
                      </div>
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <ChangeMemberRoleDialog
        userId={roleTarget?.userId ?? ""}
        currentRole={roleTarget?.role ?? "Member"}
        open={roleTarget !== null}
        onOpenChange={(open) => { if (!open) setRoleTarget(null); }}
      />
      <OffboardMemberConfirmDialog
        userId={offboardTarget?.userId ?? ""}
        displayName={offboardTarget?.displayName ?? ""}
        open={offboardTarget !== null}
        onOpenChange={(open) => { if (!open) setOffboardTarget(null); }}
      />
    </div>
  );
}
```

This deletes: the `roleFilter`/`searchInput`/`debouncedQ` state, the `DEBOUNCE_MS` constant + debounce `useEffect`, the `ROLE_OPTIONS` const, the raw `<select>`/`<input>` block, and the `≥2-char`/`"all"` guards.

- [ ] **Step 4: Run tests to verify they pass**

Run (from `web/`): `npx vitest run src/features/members/pages`
Expected: PASS (all four).

- [ ] **Step 5: Update the registry**

In `docs/design/list-filter-registry.md`:

- Change the Members row (`| Members / Users | … | built (pre-standard) | … |`) to:
```
| Members / Users | `/members` (`GET /users`) | `role` (single-select) + `q` name/email search (FilterBar) | **built** | E-03.F-01.S-05 | Role single-select + name/email text search via `<FilterBar>`/`useListFilters`; submit-driven + URL-backed (`?role=&q=`). |
```
- Update the **Control availability** line (under the status legend) to:
```
**Control availability:** text search + boolean toggle + **single-select** controls are **built** (available for new filter specs). Multi-select and date-range controls are reserved for sub-slices 2 & 3.
```

- [ ] **Step 6: Typecheck, lint, full suite, build, commit**

Run (from `web/`):
```
npm run typecheck && npm run lint && npx vitest run && npm run build
```
Expected: 0 typecheck/lint errors; all Vitest files pass; build succeeds.

```bash
git add web/src/features/members/pages/MembersListPage.tsx web/src/features/members/pages/__tests__/MembersListPage.test.tsx docs/design/list-filter-registry.md
git commit -m "feat(web): Members list onto FilterBar (role single-select + search), drop bespoke chrome"
```

- [ ] **Step 7: Pre-push CI mirror + manual verification**

Run (from repo root): `scripts/ci-local.sh frontend`
Expected: green.

Manual (ADR-0084, if the dev stack is available): cold-start, `/members` → pick a role + type a name/email → Search applies both → URL carries `?role=…&q=…` → Clear all resets → role/q survive a shared-link reload. Flag *pending user verification* if the stack is unavailable.

---

## Self-Review

**1. Spec coverage:**
- §3 #1 (single-select rides `textFilters`) → Task 2 Step 4 + Task 4 `TEXT_FILTERS`. ✓
- §3 #2 (base `Select`, `name`+`defaultSelectedKey`) → Task 1. ✓
- §3 #3 (FilterBar branch, FormData via `text` map, submit-driven) → Task 3. ✓
- §3 #4 (`FilterSpec.options`) → Task 2 Step 1. ✓
- §3 #5 ("All roles" = value `""` → absent) → Task 1 Step 1 (FormData "") + Task 4 options + FilterBar commit drops blank. ✓
- §3 #6 (submit-driven + URL-backed search, drop debounce/≥2-char, `q`=name+email) → Task 4 rewrite. ✓
- §3 #7 (useListFilters treats single-select as text) → Task 2. ✓
- §3 #8 (backend untouched, no real-seam) → no backend task; Global Constraints. ✓
- §3 #9 (filtered empty-state) → Task 4 Step 3 + test. ✓
- §3 #10 (surface proposal; registry drops "(pre-standard)") → Task 4 Step 5. ✓
- §6 test units (Select, FilterBar, useListFilters, MembersListPage) → Tasks 1–4. ✓

**2. Placeholder scan:** No "TBD"/"handle edge cases"/"similar to". Every code step shows full code. The empty-key note in Task 1 is an explicit non-blocking observation, not a deferred decision. ✓

**3. Type consistency:** `SelectOption {label,value}` (Task 1) ≡ `FilterSpec.single-select.options` element shape (Task 2). `Select` props (`name`, `defaultSelectedKey`, `options`, `aria-label`, `size`) used identically in FilterBar (Task 3). `useMembersList({sortBy,sortOrder,role?,q?})` matches the existing signature (verified in `members.ts`). `queryFilters.role`/`.q` cast to `string | undefined` at the call site (the hook returns `string | boolean | undefined`), consistent with the Catalog/Services/Teams pages. ✓
