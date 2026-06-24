# Applications List — Team & Lifecycle Multi-Select Filters Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Applications list's `displayNameContains` + `includeDecommissioned` filters with `displayNameContains` (unchanged) + **lifecycle** (multi-select) + **team** (multi-select), end-to-end (backend + frontend), building the reusable multi-select FilterBar control + multi-value URL axis.

**Architecture:** A new react-aria `MultiSelect` control mirrors its selection into hidden `<input>`s so the existing FormData-based `FilterBar` commit stays uniform. `useListUrlState` gains a `multiFilters` axis (repeated URL params, read via `getAll`). The backend `ListApplications` query accepts repeated `lifecycle`/`teamId` params, applies `IN` predicates, and records them in the cursor `f`-map only when non-empty; `includeDecommissioned` is removed and its default-hide behavior moves to "no lifecycle selected ⇒ exclude Decommissioned" (ADR-0073 preserved).

**Tech Stack:** React 19 + TypeScript, react-aria-components (ADR-0094), TanStack Query, react-router-dom 7, Vitest + Testing Library; .NET 10 / ASP.NET Core minimal APIs, EF Core (Npgsql), Wolverine handler, MSTest v4 + Testcontainers (real Postgres/RLS + real JWT via `KartovaApiFixtureBase`).

## Global Constraints

- **Spec:** `docs/superpowers/specs/2026-06-23-applications-list-team-lifecycle-filters-design.md`. ADRs: ADR-0107, ADR-0095, ADR-0094, ADR-0073.
- **Lifecycle wire tokens** (lowercase, case-insensitive on input): `active` · `deprecated` · `decommissioned`. C# enum `Lifecycle { Active=1, Deprecated=2, Decommissioned=3 }`.
- **Lifecycle filter semantics:** none selected ⇒ exclude `Decommissioned` (ADR-0073 default view); some selected ⇒ `lifecycle IN (selected)` exactly. The default (no filters) cursor `f`-map is **empty** (byte-identical to a filterless cursor) — `includeDecommissioned` is gone.
- **Filter-key consistency (ADR-0095):** URL param == API query param == cursor `f`-map key, identical strings: `lifecycle`, `teamId`.
- **Cursor `f`-map values:** multi-value filters serialize as **sorted, comma-joined** strings; key present **only** when the selection is non-empty.
- **`includeDecommissioned` is removed from the Applications endpoint + page only.** The generic boolean infra (`useListUrlState.booleanFilters`, `FilterBar` boolean branch, `useListFilters`) is **retained, not deleted** — but it has **no current production consumer** after this slice (`includeDecommissioned` was its only one). Keep it as typed+tested reserved infra (ADR-0107 standard vocabulary); do not delete it, and do not claim Services uses it.
- **Services list is out of scope** (it has only a `displayNameContains` text filter today). Touch only `CatalogListPage` (Applications, `/catalog`), `useApplicationsList`, and the `ListApplications` backend.
- **Gate 6 (mutation) is BLOCKING** for this slice (diff touches `ListApplicationsHandler` Application-layer logic).
- **Real seam (gate 3):** all backend filter integration tests run against real Postgres/RLS + real JWT via the existing `CatalogIntegrationTestBase` / `KartovaApiFixtureBase`. Never the fake auth handler or a mocked DbContext.
- **DoD:** CLAUDE.md's eight always-blocking gates (gate 6 blocking here) + terminal re-verify. This plan does not restate them.
- Windows shell: PowerShell / `cmd //c` wrappers for `dotnet`; stop the Vite dev server before `npm ci`/`ci-local` (lightningcss lock).

---

### Task 1: Base `MultiSelect` control

**Files:**
- Create: `web/src/components/base/multi-select/multi-select.tsx`
- Test: `web/src/components/base/multi-select/__tests__/multi-select.test.tsx`

**Interfaces:**
- Consumes: `SelectOption` shape (`{ label: string; value: string }`) — re-declared here (do not import from `select.tsx` to avoid coupling; identical shape).
- Produces: `MultiSelect` React component. Props:
  ```ts
  interface MultiSelectProps {
    name: string;                       // form field name; hidden inputs use it → FormData.getAll(name)
    "aria-label"?: string;
    label?: string;
    options: { label: string; value: string }[];
    defaultSelectedKeys?: string[];     // uncontrolled initial selection (option values)
    placeholder?: string;               // shown when nothing selected (e.g. "All teams")
    size?: "sm" | "md";
    className?: string;
    ref?: Ref<HTMLDivElement>;
  }
  ```
- Behavior contract (the tests are the source of truth):
  1. Seeds its displayed selection + hidden inputs from `defaultSelectedKeys`.
  2. The trigger button text summarizes the selection: `placeholder` when empty, the single option's `label` when exactly one, `"N selected"` when more than one.
  3. For each selected value it renders a hidden `<input type="hidden" name={name} value={value}>` **outside** the portaled popover (so it stays inside the enclosing `<form>` and `FormData.getAll(name)` returns the selected values).
  4. Selecting/deselecting options updates the hidden inputs (and thus `FormData`).

- [ ] **Step 1: Write the failing tests**

```tsx
// web/src/components/base/multi-select/__tests__/multi-select.test.tsx
import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MultiSelect } from "../multi-select";

const OPTIONS = [
  { label: "Active", value: "active" },
  { label: "Deprecated", value: "deprecated" },
  { label: "Decommissioned", value: "decommissioned" },
];

function formOf(container: HTMLElement) {
  return container.querySelector("form") as HTMLFormElement;
}
// The trigger is the only button rendered by the control (the listbox is portaled).
const trigger = () => screen.getByRole("button");

describe("MultiSelect (base)", () => {
  it("shows the placeholder and emits no FormData value when nothing is selected", () => {
    const { container } = render(
      <form><MultiSelect name="lifecycle" aria-label="Lifecycle" options={OPTIONS} placeholder="Any status" /></form>,
    );
    expect(trigger()).toHaveTextContent("Any status");
    expect(new FormData(formOf(container)).getAll("lifecycle")).toEqual([]);
  });

  it("seeds the selection + hidden inputs from defaultSelectedKeys", () => {
    const { container } = render(
      <form>
        <MultiSelect name="lifecycle" aria-label="Lifecycle" options={OPTIONS} defaultSelectedKeys={["active", "deprecated"]} />
      </form>,
    );
    expect(new FormData(formOf(container)).getAll("lifecycle")).toEqual(["active", "deprecated"]);
    expect(trigger()).toHaveTextContent("2 selected");
  });

  it("shows the single option's label when exactly one is selected", () => {
    render(<form><MultiSelect name="lifecycle" aria-label="Lifecycle" options={OPTIONS} defaultSelectedKeys={["deprecated"]} /></form>);
    expect(trigger()).toHaveTextContent("Deprecated");
  });

  it("selecting options updates the FormData values", async () => {
    const { container } = render(
      <form><MultiSelect name="lifecycle" aria-label="Lifecycle" options={OPTIONS} placeholder="Any status" /></form>,
    );
    await userEvent.click(trigger());
    await userEvent.click(await screen.findByRole("option", { name: "Active" }));
    await userEvent.click(await screen.findByRole("option", { name: "Decommissioned" }));
    expect(new FormData(formOf(container)).getAll("lifecycle")).toEqual(["active", "decommissioned"]);
  });
});
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd web && npx vitest run src/components/base/multi-select`
Expected: FAIL — `Cannot find module '../multi-select'`.

- [ ] **Step 3: Implement `MultiSelect`**

```tsx
// web/src/components/base/multi-select/multi-select.tsx
import { useState, type Ref } from "react";
import { Check, ChevronDown } from "@untitledui/icons";
import {
  DialogTrigger,
  Dialog,
  Button as AriaButton,
  Popover as AriaPopover,
  ListBox as AriaListBox,
  ListBoxItem as AriaListBoxItem,
  type Selection,
} from "react-aria-components";
import { cx } from "@/lib/utils/cx";

export interface MultiSelectOption {
  label: string;
  value: string;
}

export interface MultiSelectProps {
  /** Form field name. Selected values are mirrored into hidden inputs so they are
   *  captured by `FormData.getAll(name)` (the uncontrolled FilterBar commit path). */
  name: string;
  "aria-label"?: string;
  label?: string;
  options: MultiSelectOption[];
  /** Uncontrolled initial selection (option `value`s). */
  defaultSelectedKeys?: string[];
  placeholder?: string;
  size?: "sm" | "md";
  className?: string;
  ref?: Ref<HTMLDivElement>;
}

export const MultiSelect = ({
  name,
  label,
  options,
  defaultSelectedKeys,
  placeholder = "Select…",
  size = "sm",
  className,
  ref,
  ...props
}: MultiSelectProps) => {
  const [selected, setSelected] = useState<Set<string>>(() => new Set(defaultSelectedKeys ?? []));

  const onChange = (keys: Selection) => {
    // We never use the "all" sentinel (no select-all affordance) — treat it as empty.
    setSelected(keys === "all" ? new Set() : new Set([...keys].map(k => String(k))));
  };

  // Summary text: placeholder when empty, the single label when one, "N selected" otherwise.
  const summary =
    selected.size === 0
      ? placeholder
      : selected.size === 1
        ? (options.find(o => o.value === [...selected][0])?.label ?? `${selected.size} selected`)
        : `${selected.size} selected`;

  return (
    <div ref={ref} className={cx("flex w-full flex-col gap-1.5", className)}>
      {label && <span className="text-sm font-medium text-secondary">{label}</span>}
      <DialogTrigger>
        <AriaButton
          aria-label={props["aria-label"]}
          className={cx(
            "flex w-full cursor-pointer items-center justify-between gap-2 rounded-lg bg-primary text-primary shadow-xs ring-1 ring-primary outline-hidden transition-shadow duration-100 ease-linear ring-inset data-focus-visible:ring-2 data-focus-visible:ring-brand",
            size === "sm" ? "px-3 py-2 text-sm" : "px-3 py-2.5 text-md",
          )}
        >
          <span className={cx("truncate", selected.size === 0 && "text-placeholder")}>{summary}</span>
          <ChevronDown aria-hidden className="size-4 shrink-0 text-fg-quaternary" />
        </AriaButton>
        <AriaPopover className="max-h-60 w-(--trigger-width) overflow-auto rounded-lg bg-primary py-1 shadow-lg ring-1 ring-secondary">
          <Dialog className="outline-hidden">
            <AriaListBox
              aria-label={props["aria-label"]}
              selectionMode="multiple"
              selectedKeys={selected}
              onSelectionChange={onChange}
              items={options}
              className="outline-hidden"
            >
              {(item: MultiSelectOption) => (
                <AriaListBoxItem
                  id={item.value}
                  textValue={item.label}
                  className="flex cursor-pointer items-center justify-between gap-2 px-3 py-2 text-sm text-primary outline-hidden select-none data-focused:bg-secondary data-selected:font-medium"
                >
                  {({ isSelected }: { isSelected: boolean }) => (
                    <>
                      <span className="truncate">{item.label}</span>
                      {isSelected && <Check aria-hidden className="size-4 shrink-0 text-fg-brand-primary" />}
                    </>
                  )}
                </AriaListBoxItem>
              )}
            </AriaListBox>
          </Dialog>
        </AriaPopover>
      </DialogTrigger>
      {/* Hidden inputs live OUTSIDE the portaled popover so they stay inside the
          enclosing <form>; FormData.getAll(name) then returns the selected values.
          Sorted only for deterministic test output — order is not semantically meaningful. */}
      {[...selected].sort().map(value => (
        <input key={value} type="hidden" name={name} value={value} />
      ))}
    </div>
  );
};

MultiSelect.displayName = "MultiSelect";
```

> Note for implementer: if a react-aria detail differs (e.g. the `Check` icon import name in `@untitledui/icons`, or the render-prop typing), adjust to make the **four behavioral tests** pass — they are the contract. Do not add a select-all affordance or controlled `selectedKeys` prop (YAGNI; the FilterBar re-seeds via `key` + `defaultSelectedKeys`).

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd web && npx vitest run src/components/base/multi-select`
Expected: PASS (4 tests).

- [ ] **Step 5: Typecheck + lint**

Run: `cd web && npx tsc -p tsconfig.app.json --noEmit && npx eslint src/components/base/multi-select`
Expected: 0 errors.

- [ ] **Step 6: Commit**

```bash
git add web/src/components/base/multi-select/
git commit -m "feat(web): reusable react-aria MultiSelect control (hidden-input FormData bridge)"
```

---

### Task 2: `useListUrlState` multi-value axis + `setFilters` widening

**Files:**
- Modify: `web/src/lib/list/useListUrlState.ts`
- Test: `web/src/lib/list/__tests__/use-list-url-state.test.tsx`

**Interfaces:**
- Consumes: nothing new.
- Produces: a 4th generic `TMultiFilter extends string = never`; new config field `multiFilters?: readonly TMultiFilter[]`; new derived `multiFilters: Readonly<Record<TMultiFilter, string[]>>`; widened `setFilters` accepting `multi?: Record<string, string[]>`. Backward compatible — existing 3-arg callers (Teams, Members) keep working (`TMultiFilter` defaults to `never`).

- [ ] **Step 1: Write the failing tests** (append to the existing describe block)

```tsx
// web/src/lib/list/__tests__/use-list-url-state.test.tsx — add these tests.
// (Follows the file's existing harness for rendering the hook under a MemoryRouter
//  and reading window.location.search; mirror the existing text-filter tests' setup.)
import { renderHook, act } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { useListUrlState } from "../useListUrlState";

function wrapperFor(initialEntries: string[]) {
  return ({ children }: { children: React.ReactNode }) => (
    <MemoryRouter initialEntries={initialEntries}>{children}</MemoryRouter>
  );
}

describe("useListUrlState — multi-value filters", () => {
  it("reads repeated params into an array (getAll)", () => {
    const { result } = renderHook(
      () => useListUrlState({
        defaultSortBy: "displayName", defaultSortOrder: "asc",
        allowedSortFields: ["displayName"] as const,
        multiFilters: ["lifecycle"] as const,
      }),
      { wrapper: wrapperFor(["/?lifecycle=active&lifecycle=deprecated"]) },
    );
    expect(result.current.multiFilters.lifecycle).toEqual(["active", "deprecated"]);
  });

  it("defaults to an empty array when the param is absent", () => {
    const { result } = renderHook(
      () => useListUrlState({
        defaultSortBy: "displayName", defaultSortOrder: "asc",
        allowedSortFields: ["displayName"] as const,
        multiFilters: ["lifecycle"] as const,
      }),
      { wrapper: wrapperFor(["/"]) },
    );
    expect(result.current.multiFilters.lifecycle).toEqual([]);
  });

  it("setFilters serializes a multi array as repeated params and drops empties", () => {
    const { result } = renderHook(
      () => useListUrlState({
        defaultSortBy: "displayName", defaultSortOrder: "asc",
        allowedSortFields: ["displayName"] as const,
        multiFilters: ["lifecycle", "teamId"] as const,
      }),
      { wrapper: wrapperFor(["/?lifecycle=active"]) },
    );
    act(() => result.current.setFilters({ multi: { lifecycle: ["active", "deprecated"], teamId: [] } }));
    expect(result.current.multiFilters.lifecycle).toEqual(["active", "deprecated"]);
    expect(result.current.multiFilters.teamId).toEqual([]); // empty ⇒ param absent
  });
});
```

- [ ] **Step 2: Run to verify they fail**

Run: `cd web && npx vitest run src/lib/list/__tests__/use-list-url-state.test.tsx`
Expected: FAIL — `multiFilters` undefined / not a function arg.

- [ ] **Step 3: Implement the multi-value axis**

Make these edits to `web/src/lib/list/useListUrlState.ts`:

(a) Widen `Config` (add the 4th generic + field):
```ts
interface Config<
  TField extends string,
  TBoolFilter extends string = never,
  TTextFilter extends string = never,
  TMultiFilter extends string = never,
> {
  defaultSortBy: TField;
  defaultSortOrder: SortDirection;
  allowedSortFields: readonly TField[];
  booleanFilters?: readonly TBoolFilter[];
  textFilters?: readonly TTextFilter[];
  /**
   * Optional multi-value URL params (e.g. ["lifecycle","teamId"]). Each is read as
   * an array via `getAll` ([] when absent). Setter writes one repeated param per
   * value; an empty array removes the key entirely (blank ⇒ absent, ADR-0095).
   */
  multiFilters?: readonly TMultiFilter[];
}
```

(b) Widen `ListUrlState` (add the 4th generic, the `multiFilters` read map, and the `multi` updater on `setFilters`):
```ts
export interface ListUrlState<
  TField extends string,
  TBoolFilter extends string = never,
  TTextFilter extends string = never,
  TMultiFilter extends string = never,
> {
  sortBy: TField;
  sortOrder: SortDirection;
  setSort: (field: TField, order: SortDirection) => void;
  booleanFilters: Readonly<Record<TBoolFilter, boolean>>;
  setBooleanFilter: (name: string, value: boolean) => void;
  textFilters: Readonly<Record<TTextFilter, string>>;
  setTextFilter: (name: string, value: string) => void;
  /** Map of filter name to current array value (default []). */
  multiFilters: Readonly<Record<TMultiFilter, string[]>>;
  setFilters: (updates: {
    text?: Record<string, string>;
    booleans?: Record<string, boolean>;
    multi?: Record<string, string[]>;
  }) => void;
}
```

(c) Widen the function signature + add the `multiFilterNames` memo + the `multiFilters` derivation (place next to the `textFilters` derivation):
```ts
export function useListUrlState<
  TField extends string,
  TBoolFilter extends string = never,
  TTextFilter extends string = never,
  TMultiFilter extends string = never,
>(
  config: Config<TField, TBoolFilter, TTextFilter, TMultiFilter>,
): ListUrlState<TField, TBoolFilter, TTextFilter, TMultiFilter> {
  // ... existing body ...

  const multiFiltersKey = (config.multiFilters ?? []).join(",");
  const multiFilterNames = useMemo(
    () => (config.multiFilters ?? []) as readonly TMultiFilter[],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [multiFiltersKey],
  );

  const multiFilters = useMemo(() => {
    const out = {} as Record<TMultiFilter, string[]>;
    for (const name of multiFilterNames) out[name] = params.getAll(name);
    return out;
  }, [params, multiFilterNames]);
```

(d) Extend `setFilters` to apply the `multi` map (inside the existing `setParams(prev => …)` body, after the booleans loop):
```ts
      for (const [name, values] of Object.entries(updates.multi ?? {})) {
        next.delete(name);
        for (const raw of values) {
          const trimmed = raw.trim();
          if (trimmed) next.append(name, trimmed);
        }
      }
```

(e) Add `multiFilters` to the returned object:
```ts
  return { sortBy, sortOrder, setSort, booleanFilters, setBooleanFilter, textFilters, setTextFilter, multiFilters, setFilters };
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd web && npx vitest run src/lib/list/__tests__/use-list-url-state.test.tsx`
Expected: PASS (existing + 3 new).

- [ ] **Step 5: Typecheck**

Run: `cd web && npx tsc -p tsconfig.app.json --noEmit`
Expected: 0 errors (existing 3-arg callers still compile via the `never` default).

- [ ] **Step 6: Commit**

```bash
git add web/src/lib/list/useListUrlState.ts web/src/lib/list/__tests__/use-list-url-state.test.tsx
git commit -m "feat(web): multi-value filter axis in useListUrlState (repeated params)"
```

---

### Task 3: `FilterSpec` multi-select variant + `FilterBar` branch + `useListFilters` derivation

**Files:**
- Modify: `web/src/lib/list/filters/types.ts`
- Modify: `web/src/components/application/filter-bar/FilterBar.tsx`
- Modify: `web/src/lib/list/filters/useListFilters.ts`
- Test: `web/src/components/application/filter-bar/__tests__/FilterBar.test.tsx`
- Test: `web/src/lib/list/filters/__tests__/useListFilters.test.tsx`

**Interfaces:**
- Consumes: `MultiSelect` (Task 1), `useListUrlState.multiFilters` + `setFilters({multi})` (Task 2).
- Produces: `multi-select` becomes a first-class `FilterSpec` variant with `options`; `FilterBar` renders/commits/clears it; `useListFilters` derives `queryFilters[key]: string[] | undefined` and counts it active when non-empty.

- [ ] **Step 1: Promote the `multi-select` variant in `types.ts`**

```ts
/** Declarative filter descriptor rendered by <FilterBar> (ADR-0107). */
export type FilterSpec =
  | { key: string; type: "text"; label: string; placeholder?: string }
  | { key: string; type: "boolean"; label: string }
  | { key: string; type: "single-select"; label: string; options: { label: string; value: string }[] }
  | { key: string; type: "multi-select"; label: string; options: { label: string; value: string }[]; placeholder?: string }
  // Reserved per ADR-0107 clause 1 — typed now, built when a screen needs it.
  | { key: string; type: "date-range"; label: string };
```

- [ ] **Step 2: Write the failing FilterBar tests** (replace the existing "throws for multi-select" test; add a multi-select describe block)

In `FilterBar.test.tsx`:
- Update `fakeUrlState` to also carry a multi map and pass it through:
```ts
function fakeUrlState(
  textFilters: Record<string, string> = {},
  booleanFilters: Record<string, boolean> = {},
  setFilters = vi.fn(),
  multiFilters: Record<string, string[]> = {},
) {
  return { textFilters, booleanFilters, multiFilters, setFilters };
}
```
- Replace the `"throws for multi-select type"` test (it no longer throws) — keep a `date-range` throw test for the still-reserved type:
```ts
  it("throws for the still-reserved date-range type", () => {
    const bad: FilterSpec[] = [{ key: "x", type: "date-range", label: "X" }];
    expect(() => render(<FilterBar specs={bad} urlState={fakeUrlState()} />)).toThrow(/not implemented/i);
  });
```
- Add a multi-select describe block:
```ts
const multiSpecs: FilterSpec[] = [
  {
    key: "lifecycle",
    type: "multi-select",
    label: "Lifecycle",
    placeholder: "Any status",
    options: [
      { label: "Active", value: "active" },
      { label: "Deprecated", value: "deprecated" },
      { label: "Decommissioned", value: "decommissioned" },
    ],
  },
];
// The multi-select trigger is the only non-toggle, non-Search button; query it via its label text.
const multiTrigger = () => screen.getByRole("button", { name: /lifecycle/i });

describe("FilterBar — multi-select", () => {
  it("renders a MultiSelect for a multi-select spec", () => {
    render(<FilterBar specs={multiSpecs} urlState={fakeUrlState()} />);
    expect(multiTrigger()).toBeInTheDocument();
  });

  it("seeds the MultiSelect from committed multiFilters", () => {
    render(<FilterBar specs={multiSpecs} urlState={fakeUrlState({}, {}, vi.fn(), { lifecycle: ["active", "deprecated"] })} />);
    expect(multiTrigger()).toHaveTextContent("2 selected");
  });

  it("choosing values + Search commits them via setFilters (multi map)", async () => {
    const setFilters = vi.fn();
    render(<FilterBar specs={multiSpecs} urlState={fakeUrlState({}, {}, setFilters)} />);
    await userEvent.click(multiTrigger());
    await userEvent.click(await screen.findByRole("option", { name: "Deprecated" }));
    await userEvent.click(screen.getByRole("button", { name: /^search$/i }));
    expect(setFilters).toHaveBeenCalledWith(
      expect.objectContaining({ multi: expect.objectContaining({ lifecycle: ["deprecated"] }) }),
    );
  });

  it("Clear all resets a committed multi-select to empty", () => {
    const setFilters = vi.fn();
    render(<FilterBar specs={multiSpecs} urlState={fakeUrlState({}, {}, setFilters, { lifecycle: ["active"] })} />);
    fireEvent.click(screen.getByRole("button", { name: /clear all/i }));
    expect(setFilters).toHaveBeenCalledWith({ text: {}, booleans: {}, multi: { lifecycle: [] } });
  });

  it("counts a non-empty multi-select toward the active-filter header", () => {
    render(<FilterBar specs={multiSpecs} urlState={fakeUrlState({}, {}, vi.fn(), { lifecycle: ["active"] })} />);
    expect(screen.getByRole("button", { name: /filters \(1 active\)/i })).toBeInTheDocument();
  });
});
```

- [ ] **Step 3: Write the failing `useListFilters` tests** (append)

```tsx
// useListFilters.test.tsx — add. The hook now also reads urlState.multiFilters.
it("derives a non-empty multi-select into queryFilters as an array and marks active", () => {
  const specs: FilterSpec[] = [
    { key: "lifecycle", type: "multi-select", label: "Lifecycle", options: [{ label: "Active", value: "active" }] },
  ];
  const urlState = { textFilters: {}, booleanFilters: {}, multiFilters: { lifecycle: ["active", "deprecated"] } };
  const { result } = renderHook(() => useListFilters(specs, urlState));
  expect(result.current.queryFilters.lifecycle).toEqual(["active", "deprecated"]);
  expect(result.current.isActive).toBe(true);
  expect(result.current.activeCount).toBe(1);
});

it("treats an empty multi-select as inactive and undefined in queryFilters", () => {
  const specs: FilterSpec[] = [
    { key: "lifecycle", type: "multi-select", label: "Lifecycle", options: [{ label: "Active", value: "active" }] },
  ];
  const urlState = { textFilters: {}, booleanFilters: {}, multiFilters: { lifecycle: [] } };
  const { result } = renderHook(() => useListFilters(specs, urlState));
  expect(result.current.queryFilters.lifecycle).toBeUndefined();
  expect(result.current.isActive).toBe(false);
});
```

> If existing `useListFilters` tests construct `urlState` without `multiFilters`, add `multiFilters: {}` to those literals (the hook reads it; `?.` guards keep it safe, but be explicit).

- [ ] **Step 4: Run all three test files to verify the new cases fail**

Run: `cd web && npx vitest run src/components/application/filter-bar src/lib/list/filters`
Expected: FAIL on the new multi-select cases.

- [ ] **Step 5: Implement the `FilterBar` multi-select branch**

In `FilterBar.tsx`:

(a) Import the control + widen the prop type:
```ts
import { MultiSelect } from "@/components/base/multi-select/multi-select";
```
```ts
  urlState: Pick<
    ListUrlState<string, string, string, string>,
    "textFilters" | "booleanFilters" | "multiFilters" | "setFilters"
  >;
```

(b) Read the committed multi map:
```ts
  const committedMulti = urlState.multiFilters;
```

(c) In `commit()`, fold multi-select keys (read via `getAll`) and pass `multi` to `setFilters`:
```ts
  const commit = () => {
    const form = formRef.current;
    if (!form) return;
    const data = new FormData(form);
    const text: Record<string, string> = {};
    const booleans: Record<string, boolean> = {};
    const multi: Record<string, string[]> = {};
    for (const s of specs) {
      if (s.type === "text" || s.type === "single-select") {
        text[s.key] = String(data.get(s.key) ?? "");
      } else if (s.type === "boolean") {
        booleans[s.key] = data.get(s.key) != null;
      } else if (s.type === "multi-select") {
        multi[s.key] = data.getAll(s.key).map(String);
      }
    }
    urlState.setFilters({ text, booleans, multi });
  };
```

(d) In `clearAll()`, empty multi keys too:
```ts
  const clearAll = () => {
    const text: Record<string, string> = {};
    const booleans: Record<string, boolean> = {};
    const multi: Record<string, string[]> = {};
    for (const s of specs) {
      if (s.type === "text" || s.type === "single-select") text[s.key] = "";
      else if (s.type === "boolean") booleans[s.key] = false;
      else if (s.type === "multi-select") multi[s.key] = [];
    }
    urlState.setFilters({ text, booleans, multi });
  };
```

(e) Add the render branch (place before the `throw`):
```tsx
              if (spec.type === "multi-select") {
                const committed = committedMulti?.[spec.key] ?? [];
                return (
                  // Keyed by the committed values so Clear all / back-forward re-seeds
                  // the uncontrolled control via defaultSelectedKeys.
                  <div key={`${spec.key}:${committed.join(",")}`} className="w-full sm:w-56">
                    <MultiSelect
                      name={spec.key}
                      defaultSelectedKeys={committed}
                      aria-label={spec.label}
                      options={spec.options}
                      placeholder={spec.placeholder}
                      size="sm"
                    />
                  </div>
                );
              }
```
And update the throw message:
```ts
              throw new Error(
                `FilterBar: "${spec.type}" control not implemented (ADR-0107 clause 1 — text + boolean + single-select + multi-select only)`,
              );
```

- [ ] **Step 6: Implement the `useListFilters` multi-select derivation**

In `useListFilters.ts`:

(a) Widen the `urlState` Pick + read the multi map:
```ts
export function useListFilters(
  specs: FilterSpec[],
  urlState: Pick<ListUrlState<string, string, string, string>, "textFilters" | "booleanFilters" | "multiFilters">,
) {
  const textSpecs = useMemo(
    () => specs.filter(s => s.type === "text" || s.type === "single-select"),
    [specs],
  );
  const boolSpecs = useMemo(() => specs.filter(s => s.type === "boolean"), [specs]);
  const multiSpecs = useMemo(() => specs.filter(s => s.type === "multi-select"), [specs]);
  const committedText = urlState.textFilters;
  const committedBool = urlState.booleanFilters;
  const committedMulti = urlState.multiFilters;
```

(b) Extend `queryFilters` (widen the value union to include `string[]`):
```ts
  const queryFilters = useMemo(() => {
    const out: Record<string, string | boolean | string[] | undefined> = {};
    for (const s of textSpecs) out[s.key] = (committedText[s.key] ?? "") || undefined;
    for (const s of boolSpecs) out[s.key] = committedBool?.[s.key] ?? false;
    for (const s of multiSpecs) out[s.key] = committedMulti?.[s.key]?.length ? committedMulti[s.key] : undefined;
    return out;
  }, [textSpecs, boolSpecs, multiSpecs, committedText, committedBool, committedMulti]);
```

(c) Extend `isActive` + `activeCount`:
```ts
  const isActive = useMemo(
    () => textSpecs.some(s => (committedText[s.key] ?? "") !== "")
       || boolSpecs.some(s => (committedBool?.[s.key] ?? false) === true)
       || multiSpecs.some(s => (committedMulti?.[s.key]?.length ?? 0) > 0),
    [textSpecs, boolSpecs, multiSpecs, committedText, committedBool, committedMulti]);

  const activeCount = useMemo(
    () => textSpecs.filter(s => (committedText[s.key] ?? "") !== "").length
        + boolSpecs.filter(s => (committedBool?.[s.key] ?? false) === true).length
        + multiSpecs.filter(s => (committedMulti?.[s.key]?.length ?? 0) > 0).length,
    [textSpecs, boolSpecs, multiSpecs, committedText, committedBool, committedMulti]);
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `cd web && npx vitest run src/components/application/filter-bar src/lib/list/filters`
Expected: PASS.

- [ ] **Step 8: Typecheck + lint**

Run: `cd web && npx tsc -p tsconfig.app.json --noEmit && npx eslint src/components/application/filter-bar src/lib/list/filters`
Expected: 0 errors.

- [ ] **Step 9: Commit**

```bash
git add web/src/lib/list/filters/ web/src/components/application/filter-bar/
git commit -m "feat(web): FilterBar multi-select control + useListFilters derivation (ADR-0107)"
```

---

### Task 4: Backend `ListApplications` — lifecycle[]/teamId[] params, predicates, f-map; remove `includeDecommissioned`

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Application/ListApplicationsQuery.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs:111-162`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/ListApplicationsHandler.cs:38-120`
- Modify: `src/Kartova.SharedKernel.AspNetCore/ProblemTypes.cs` (add one constant)
- Test (rewrite): `src/Modules/Catalog/Kartova.Catalog.Tests/ListApplicationsHandlerFilterTests.cs`

**Interfaces:**
- Produces: `ListApplicationsQuery(ApplicationSortField SortBy, SortOrder SortOrder, string? Cursor, int Limit, Lifecycle[] Lifecycle, Guid[] TeamId, string? DisplayNameContains = null, Guid? CreatedByUserId = null)`. Endpoint binds `?lifecycle=…` (repeated, parsed names) + `?teamId=…` (repeated Guids); cursor `f`-map keys `lifecycle` / `teamId` (sorted comma-joined, present only when non-empty).

- [ ] **Step 1: Rewrite the handler unit tests (failing)**

Replace the body of `ListApplicationsHandlerFilterTests.cs` test methods (keep the class scaffolding + `BuildDbWithBothLifecyclesAsync` + `NoOpDirectory`). Add a second-team builder and new tests:

```csharp
    // Add alongside BuildDbWithBothLifecyclesAsync:
    private static readonly Guid TeamB = Guid.Parse("cccccccc-0000-0000-0000-000000000002");

    private static async Task<CatalogDbContext> BuildDbWithTwoTeamsAsync()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var seed = new CatalogDbContext(options);
        var inA = DomainApplication.Create("In Team A", "d", Creator, Team, Tenant, Clock(BaseTime));
        var inB = DomainApplication.Create("In Team B", "d", Creator, TeamB, Tenant, Clock(BaseTime.AddMinutes(1)));
        seed.Applications.Add(inA);
        seed.Applications.Add(inB);
        await seed.SaveChangesAsync();
        return new CatalogDbContext(options);
    }

    private static ListApplicationsQuery Query(Lifecycle[]? lifecycle = null, Guid[]? teamId = null) =>
        new(ApplicationSortField.CreatedAt, SortOrder.Desc, Cursor: null, Limit: 50,
            Lifecycle: lifecycle ?? Array.Empty<Lifecycle>(),
            TeamId: teamId ?? Array.Empty<Guid>());

    [TestMethod]
    public async Task Handle_with_no_lifecycle_filter_excludes_Decommissioned_rows()
    {
        await using var db = await BuildDbWithBothLifecyclesAsync();
        var page = await new ListApplicationsHandler(NoOpDirectory()).Handle(Query(), db, CancellationToken.None);
        Assert.AreEqual(1, page.Items.Count, "empty lifecycle filter applies the ADR-0073 default view");
        Assert.AreEqual("Active App", page.Items.Single().DisplayName);
    }

    [TestMethod]
    public async Task Handle_with_lifecycle_decommissioned_returns_only_decommissioned()
    {
        await using var db = await BuildDbWithBothLifecyclesAsync();
        var page = await new ListApplicationsHandler(NoOpDirectory())
            .Handle(Query(lifecycle: new[] { Lifecycle.Decommissioned }), db, CancellationToken.None);
        Assert.AreEqual(1, page.Items.Count);
        Assert.AreEqual("Decomm App", page.Items.Single().DisplayName);
    }

    [TestMethod]
    public async Task Handle_with_all_lifecycles_returns_both()
    {
        await using var db = await BuildDbWithBothLifecyclesAsync();
        var page = await new ListApplicationsHandler(NoOpDirectory())
            .Handle(Query(lifecycle: new[] { Lifecycle.Active, Lifecycle.Deprecated, Lifecycle.Decommissioned }), db, CancellationToken.None);
        Assert.AreEqual(2, page.Items.Count);
        CollectionAssert.AreEquivalent(new[] { "Active App", "Decomm App" }, page.Items.Select(i => i.DisplayName).ToArray());
    }

    [TestMethod]
    public async Task Handle_with_teamId_filters_to_that_team()
    {
        await using var db = await BuildDbWithTwoTeamsAsync();
        var page = await new ListApplicationsHandler(NoOpDirectory())
            .Handle(Query(teamId: new[] { TeamB }), db, CancellationToken.None);
        Assert.AreEqual(1, page.Items.Count);
        Assert.AreEqual("In Team B", page.Items.Single().DisplayName);
    }
```

Delete the two old `IncludeDecommissioned_*` tests. Update the class-level `<summary>` to describe lifecycle/team predicates.

- [ ] **Step 2: Run the unit tests to verify they fail to compile**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Tests --filter ListApplicationsHandlerFilterTests"`
Expected: build error — `ListApplicationsQuery` has no `Lifecycle`/`TeamId` parameters yet.

- [ ] **Step 3: Update `ListApplicationsQuery`**

```csharp
public sealed record ListApplicationsQuery(
    ApplicationSortField SortBy,
    SortOrder SortOrder,
    string? Cursor,
    int Limit,
    Lifecycle[] Lifecycle,
    Guid[] TeamId,
    string? DisplayNameContains = null,
    Guid? CreatedByUserId = null);
```
Add `using Kartova.Catalog.Domain;` for `Lifecycle`. Replace the `<para>` doc about `IncludeDecommissioned` with one describing the lifecycle (empty ⇒ ADR-0073 default-hide; non-empty ⇒ `IN`) and team (`IN`) filters, both encoded into the cursor `f`-map only when non-empty.

- [ ] **Step 4: Add the `ProblemTypes` constant**

In `src/Kartova.SharedKernel.AspNetCore/ProblemTypes.cs`, after `InvalidCreatedBy`:
```csharp
    // Catalog ?lifecycle= multi-select filter — applications list (ADR-0107).
    public const string InvalidLifecycleFilter = Base + "invalid-lifecycle-filter";
```

- [ ] **Step 5: Update the endpoint delegate** (`CatalogEndpointDelegates.ListApplicationsAsync`)

Replace the `includeDecommissioned` parameter with `lifecycle` + add `teamId`, parse/validate, and build the query:
```csharp
    internal static async Task<IResult> ListApplicationsAsync(
        [FromQuery] string? sortBy,
        [FromQuery] string? sortOrder,
        [FromQuery] string? cursor,
        [FromQuery] string? limit,
        [FromQuery] string? displayNameContains,
        [FromQuery] string[]? lifecycle,
        [FromQuery] Guid[]? teamId,
        [FromQuery] Guid? createdByUserId,
        ListApplicationsHandler handler,
        CatalogDbContext db,
        IUserDirectory directory,
        CancellationToken ct)
    {
        var (parsedSortBy, parsedSortOrder, effectiveLimit) = CursorListBinding.Bind<ApplicationSortField>(
            sortBy, sortOrder, limit, ApplicationSortSpecs.AllowedFieldNames);

        // Parse the repeated ?lifecycle= tokens (wire form: lowercase enum names). Reject
        // numeric tokens ("1") and unknown strings with a 400 invalid-lifecycle-filter so
        // the contract stays names-only (mirrors the sortBy IsDefined reject in CursorListBinding).
        var lifecycles = new List<Lifecycle>();
        foreach (var raw in lifecycle ?? Array.Empty<string>())
        {
            if (int.TryParse(raw, out _)
                || !Enum.TryParse<Lifecycle>(raw, ignoreCase: true, out var parsed)
                || !Enum.IsDefined(parsed))
            {
                return Results.Problem(
                    type: ProblemTypes.InvalidLifecycleFilter,
                    title: "Invalid lifecycle filter",
                    detail: $"'{raw}' is not a valid lifecycle. Expected one of: active, deprecated, decommissioned.",
                    statusCode: StatusCodes.Status400BadRequest);
            }
            lifecycles.Add(parsed);
        }

        if (createdByUserId is { } createdByToValidate)
        {
            var user = await directory.GetAsync(createdByToValidate, ct);
            if (user is null)
            {
                return Results.Problem(
                    type: ProblemTypes.InvalidCreatedBy,
                    title: "Invalid created-by",
                    detail: "The supplied createdByUserId does not resolve to a user in the current tenant.",
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }
        }

        var name = string.IsNullOrWhiteSpace(displayNameContains) ? null : displayNameContains.Trim();

        var query = new ListApplicationsQuery(
            SortBy: parsedSortBy ?? ApplicationSortField.DisplayName,
            SortOrder: parsedSortOrder ?? SortOrder.Asc,
            Cursor: cursor,
            Limit: effectiveLimit,
            Lifecycle: lifecycles.Distinct().ToArray(),
            TeamId: (teamId ?? Array.Empty<Guid>()).Distinct().ToArray(),
            DisplayNameContains: name,
            CreatedByUserId: createdByUserId);

        var page = await handler.Handle(query, db, ct);
        return Results.Ok(page);
    }
```
Add `using Kartova.Catalog.Domain;` if `Lifecycle` is not already in scope (the file aliases `ApplicationId`; add the namespace import). Update the XML `<para>` on the method to describe `lifecycle`/`teamId` (replace the `includeDecommissioned` paragraph). Keep `createdByUserId` docs.

- [ ] **Step 6: Update the handler predicates + f-map** (`ListApplicationsHandler.Handle`)

Replace the `IncludeDecommissioned` block + the `filters` dictionary:
```csharp
        IQueryable<DomainApplication> source = db.Applications;

        // Lifecycle filter (ADR-0107) replaces the old includeDecommissioned boolean.
        // None selected ⇒ ADR-0073 default view (hide Decommissioned); some selected ⇒
        // exactly those states. Applied before paging so a hidden row never becomes a
        // cursor boundary. Array.Contains → SQL `= ANY(@p)` via Npgsql.
        if (q.Lifecycle.Length > 0)
        {
            source = source.Where(a => q.Lifecycle.Contains(a.Lifecycle));
        }
        else
        {
            source = source.Where(a => a.Lifecycle != Lifecycle.Decommissioned);
        }

        if (q.CreatedByUserId is { } createdByUserId)
        {
            source = source.Where(a => a.CreatedByUserId == createdByUserId);
        }

        if (q.DisplayNameContains is { } name)
        {
            var pattern = $"%{LikeEscaping.EscapeLike(name)}%";
            source = source.Where(a => EF.Functions.ILike(a.DisplayName, pattern, "\\"));
        }

        // Team filter (ADR-0107). Applied before paging. Array.Contains → SQL IN.
        if (q.TeamId.Length > 0)
        {
            source = source.Where(a => q.TeamId.Contains(a.TeamId));
        }

        // Filter state the cursor is issued under (ADR-0095). Every applied filter is
        // recorded; absent filters add no key — so the default (unfiltered) cursor map
        // is EMPTY (byte-identical to a filterless cursor). Multi-value filters serialize
        // as sorted comma-joined strings so identity is order-independent. A change
        // mid-pagination trips CursorFilterMismatchException inside ToCursorPagedAsync.
        var filters = new Dictionary<string, string>(StringComparer.Ordinal);
        if (q.Lifecycle.Length > 0)
        {
            filters["lifecycle"] = string.Join(",",
                q.Lifecycle.Select(l => l.ToString()).OrderBy(s => s, StringComparer.Ordinal));
        }
        if (q.TeamId.Length > 0)
        {
            filters["teamId"] = string.Join(",",
                q.TeamId.Select(t => t.ToString("D")).OrderBy(s => s, StringComparer.Ordinal));
        }
        if (q.CreatedByUserId is { } createdBy)
        {
            filters["createdByUserId"] = createdBy.ToString("D");
        }
        if (q.DisplayNameContains is { } displayName)
        {
            filters["displayNameContains"] = displayName;
        }
```
(The `ToCursorPagedAsync` call + owner-enrichment tail below it are unchanged.)

- [ ] **Step 7: Run the unit tests to verify they pass**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Tests --filter ListApplicationsHandlerFilterTests"`
Expected: PASS (4 tests). If `ListApplicationsHandlerOwnerFilterTests` / `...OwnerEnrichmentTests` construct `ListApplicationsQuery`, update those call sites to the new positional shape (`Lifecycle: Array.Empty<Lifecycle>(), TeamId: Array.Empty<Guid>()`).

- [ ] **Step 8: Build the touched projects (warnings-as-errors)**

Run: `cmd //c "dotnet build src/Modules/Catalog/Kartova.Catalog.Infrastructure"`
Expected: 0 warnings, 0 errors.

- [ ] **Step 9: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Application/ListApplicationsQuery.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/ListApplicationsHandler.cs src/Kartova.SharedKernel.AspNetCore/ProblemTypes.cs src/Modules/Catalog/Kartova.Catalog.Tests/ListApplicationsHandlerFilterTests.cs
git commit -m "feat(catalog): lifecycle[] + teamId[] filters on ListApplications; drop includeDecommissioned"
```

---

### Task 5: Backend integration-test migration (real seam)

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/ListApplicationsPaginationTests.cs`

**Interfaces:**
- Consumes: the Task-4 contract (`?lifecycle=`, `?teamId=`, f-map keys `lifecycle`/`teamId`). Fixture helpers already exist: `Fx.SeedApplicationsAsync`, `Fx.SeedApplicationsWithLifecycleAsync(tenant,count,namePrefix,Lifecycle)`, `Fx.SeedSingleApplicationAsync(tenant,creator,teamId,namePrefix)`, `Fx.SeedTeamInOrganizationAsync(tenant,name) → Guid`, `Fx.SeedUserInOrganizationAsync`, `Fx.DeleteApplicationsByPrefixAsync`, `KartovaApiFixtureBase.WireJson`.

This task replaces the `includeDecommissioned` wire tests with `lifecycle`/`teamId` equivalents. **Behavioral change to honor:** with `includeDecommissioned` removed and lifecycle optional, a default (no-filter) request now produces an **empty** cursor `f`-map — so a filterless cursor is no longer rejected on a default request. The two tests that asserted the always-on dimension must be removed; the mismatch coverage moves onto `lifecycle`.

- [ ] **Step 1: Keep** (unchanged) all pagination/sort/limit/cursor tests and `GET_applications_default_excludes_Decommissioned` (no param ⇒ default still hides Decommissioned — now via the empty-lifecycle branch), and `GET_with_displayNameContains_returns_only_matching_applications`, and `GET_default_sort_orders_matching_rows_by_displayName_ascending`.

- [ ] **Step 2: Delete** these now-invalid tests:
- `GET_applications_with_includeDecommissioned_true_returns_all_lifecycles`
- `GET_applications_with_explicit_includeDecommissioned_false_matches_default`
- `GET_displayNameContains_combines_with_includeDecommissioned`
- `GET_applications_with_cursor_from_includeDecommissioned_true_then_request_false_returns_400_cursor_filter_mismatch`
- `GET_applications_with_filterless_cursor_returns_400_cursor_filter_mismatch` (premise gone — default request now has an empty filter map, so a filterless cursor MATCHES)
- `GET_applications_legacy_cursor_replayed_with_includeDecommissioned_true_returns_400` (replaced by the lifecycle mismatch test below)

- [ ] **Step 3: Add the lifecycle wire tests** (replace the slice-6 section)

```csharp
    // -----------------------------------------------------------------------
    // Lifecycle multi-select filter (ADR-0107) — real seam: real Postgres/RLS + JWT.
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task GET_with_lifecycle_decommissioned_reveals_decommissioned_rows()
    {
        var unique = $"lc-dec-{Guid.NewGuid():N}";
        var activePrefix = $"{unique}-a-";
        var decommPrefix = $"{unique}-d-";
        var tenantId = Fx.TenantIdForEmail("admin@orga.kartova.local");
        await Fx.SeedApplicationsAsync(tenantId, count: 3, namePrefix: activePrefix);
        await Fx.SeedApplicationsWithLifecycleAsync(tenantId, count: 2, namePrefix: decommPrefix, Lifecycle.Decommissioned);
        try
        {
            var client = Fx.CreateClientForOrgA();
            var resp = await client.GetAsync("/api/v1/catalog/applications?limit=200&lifecycle=decommissioned");
            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
            var page = await resp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            var ours = page!.Items.Where(i => i.DisplayName.StartsWith(unique, StringComparison.Ordinal)).ToList();
            Assert.AreEqual(2, ours.Count(i => i.DisplayName.StartsWith(decommPrefix)), "decommissioned rows are revealed by ?lifecycle=decommissioned");
            Assert.AreEqual(0, ours.Count(i => i.DisplayName.StartsWith(activePrefix)), "active rows are excluded when only decommissioned is selected");
        }
        finally
        {
            await Fx.DeleteApplicationsByPrefixAsync(tenantId, activePrefix);
            await Fx.DeleteApplicationsByPrefixAsync(tenantId, decommPrefix);
        }
    }

    [TestMethod]
    public async Task GET_with_all_lifecycles_returns_active_and_decommissioned()
    {
        var unique = $"lc-all-{Guid.NewGuid():N}";
        var activePrefix = $"{unique}-a-";
        var decommPrefix = $"{unique}-d-";
        var tenantId = Fx.TenantIdForEmail("admin@orga.kartova.local");
        await Fx.SeedApplicationsAsync(tenantId, count: 3, namePrefix: activePrefix);
        await Fx.SeedApplicationsWithLifecycleAsync(tenantId, count: 2, namePrefix: decommPrefix, Lifecycle.Decommissioned);
        try
        {
            var client = Fx.CreateClientForOrgA();
            var resp = await client.GetAsync("/api/v1/catalog/applications?limit=200&lifecycle=active&lifecycle=deprecated&lifecycle=decommissioned");
            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
            var page = await resp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            var ours = page!.Items.Where(i => i.DisplayName.StartsWith(unique, StringComparison.Ordinal)).ToList();
            Assert.AreEqual(5, ours.Count, "selecting all lifecycles returns every seeded row");
        }
        finally
        {
            await Fx.DeleteApplicationsByPrefixAsync(tenantId, activePrefix);
            await Fx.DeleteApplicationsByPrefixAsync(tenantId, decommPrefix);
        }
    }

    [TestMethod]
    public async Task GET_with_invalid_lifecycle_token_returns_400_invalid_lifecycle_filter()
    {
        var client = Fx.CreateClientForOrgA();
        var resp = await client.GetAsync("/api/v1/catalog/applications?lifecycle=garbage");
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
        StringAssert.Contains(await resp.Content.ReadAsStringAsync(), "invalid-lifecycle-filter");
    }
```

- [ ] **Step 4: Add the team wire tests**

```csharp
    // -----------------------------------------------------------------------
    // Team multi-select filter (ADR-0107).
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task GET_with_teamId_filters_to_that_team()
    {
        var unique = $"tm-{Guid.NewGuid():N}";
        var tenantId = Fx.TenantIdForEmail("admin@orga.kartova.local");
        var teamA = await Fx.SeedTeamInOrganizationAsync(tenantId, $"{unique}-A");
        var teamB = await Fx.SeedTeamInOrganizationAsync(tenantId, $"{unique}-B");
        var creator = await Fx.SeedUserInOrganizationAsync(tenantId, displayName: "Team Filter Creator", email: $"{unique}@orga.kartova.local");
        var inA = await Fx.SeedSingleApplicationAsync(tenantId, creator, teamId: teamA, namePrefix: $"{unique}-a");
        var inB = await Fx.SeedSingleApplicationAsync(tenantId, creator, teamId: teamB, namePrefix: $"{unique}-b");
        try
        {
            var client = Fx.CreateClientForOrgA();
            var resp = await client.GetAsync($"/api/v1/catalog/applications?limit=200&teamId={teamA}");
            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
            var page = await resp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            var ids = page!.Items.Select(i => i.Id).ToHashSet();
            Assert.IsTrue(ids.Contains(inA), "app in team A is returned");
            Assert.IsFalse(ids.Contains(inB), "app in team B is excluded");
        }
        finally
        {
            await Fx.DeleteUserInOrganizationAsync(creator);
            await Fx.DeleteApplicationsByPrefixAsync(tenantId, unique);
        }
    }

    [TestMethod]
    public async Task GET_with_multiple_teamId_returns_union()
    {
        var unique = $"tm2-{Guid.NewGuid():N}";
        var tenantId = Fx.TenantIdForEmail("admin@orga.kartova.local");
        var teamA = await Fx.SeedTeamInOrganizationAsync(tenantId, $"{unique}-A");
        var teamB = await Fx.SeedTeamInOrganizationAsync(tenantId, $"{unique}-B");
        var creator = await Fx.SeedUserInOrganizationAsync(tenantId, displayName: "Union Creator", email: $"{unique}@orga.kartova.local");
        var inA = await Fx.SeedSingleApplicationAsync(tenantId, creator, teamId: teamA, namePrefix: $"{unique}-a");
        var inB = await Fx.SeedSingleApplicationAsync(tenantId, creator, teamId: teamB, namePrefix: $"{unique}-b");
        try
        {
            var client = Fx.CreateClientForOrgA();
            var resp = await client.GetAsync($"/api/v1/catalog/applications?limit=200&teamId={teamA}&teamId={teamB}");
            var page = await resp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            var ids = page!.Items.Select(i => i.Id).ToHashSet();
            Assert.IsTrue(ids.Contains(inA) && ids.Contains(inB), "both teams' apps are returned");
        }
        finally
        {
            await Fx.DeleteUserInOrganizationAsync(creator);
            await Fx.DeleteApplicationsByPrefixAsync(tenantId, unique);
        }
    }
```

- [ ] **Step 5: Add the cursor `f`-map mismatch test (on lifecycle)**

```csharp
    [TestMethod]
    public async Task GET_lifecycle_cursor_then_changed_lifecycle_returns_400_cursor_filter_mismatch()
    {
        var unique = $"lc-mism-{Guid.NewGuid():N}";
        var activePrefix = $"{unique}-a-";
        var decommPrefix = $"{unique}-d-";
        var tenantId = Fx.TenantIdForEmail("admin@orga.kartova.local");
        await Fx.SeedApplicationsAsync(tenantId, count: 3, namePrefix: activePrefix);
        await Fx.SeedApplicationsWithLifecycleAsync(tenantId, count: 2, namePrefix: decommPrefix, Lifecycle.Decommissioned);
        try
        {
            var client = Fx.CreateClientForOrgA();
            // Page 1 selects all three lifecycles → f-map "Active,Decommissioned,Deprecated" (sorted).
            var page1 = await client.GetAsync("/api/v1/catalog/applications?limit=2&lifecycle=active&lifecycle=deprecated&lifecycle=decommissioned");
            Assert.AreEqual(HttpStatusCode.OK, page1.StatusCode);
            var p1 = await page1.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(p1!.NextCursor);

            // Page 2 narrows to just active → mismatch on the "lifecycle" filter key.
            var page2 = await client.GetAsync(
                $"/api/v1/catalog/applications?limit=2&lifecycle=active&cursor={Uri.EscapeDataString(p1.NextCursor!)}");
            Assert.AreEqual(HttpStatusCode.BadRequest, page2.StatusCode);
            var problem = await page2.Content.ReadFromJsonAsync<ProblemDetails>(KartovaApiFixtureBase.WireJson);
            Assert.AreEqual(ProblemTypes.CursorFilterMismatch, problem!.Type);
            Assert.AreEqual("lifecycle", problem.Extensions["filterName"]!.ToString());
            Assert.AreEqual("Active,Decommissioned,Deprecated", problem.Extensions["expectedValue"]!.ToString());
            Assert.AreEqual("Active", problem.Extensions["actualValue"]!.ToString());
        }
        finally
        {
            await Fx.DeleteApplicationsByPrefixAsync(tenantId, activePrefix);
            await Fx.DeleteApplicationsByPrefixAsync(tenantId, decommPrefix);
        }
    }
```

- [ ] **Step 6: Run the integration tests**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter ListApplicationsPaginationTests"`
Expected: PASS. (Requires Docker for Testcontainers. If Docker is unavailable in the execution environment, flag *pending user verification* and proceed.)

> If a transient Docker named-pipe TimeoutException appears under saturation, re-run the assembly in isolation before treating it as red (known flake).

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.IntegrationTests/ListApplicationsPaginationTests.cs
git commit -m "test(catalog): migrate ListApplications integration tests to lifecycle/team filters"
```

---

### Task 6: Regenerate the OpenAPI snapshot + generated TS types

**Files:**
- Modify (regenerated): `web/openapi-snapshot.json` (committed)
- Regenerated (gitignored, not committed): `web/src/generated/openapi.ts`

**Interfaces:**
- Produces: `operations["ListApplications"]["parameters"]["query"]` now includes `lifecycle?: string[]` + `teamId?: string[]` and no longer includes `includeDecommissioned`. Task 7 depends on this.

> This step needs the API running with the Task-4 changes so codegen fetches the new contract. `codegen.mjs` fetches `http://localhost:8080/openapi/v1.json`, writes `openapi-snapshot.json`, then runs `openapi-typescript` → `src/generated/openapi.ts`. If the live API is unreachable it falls back to the **stale** snapshot — which would NOT contain the new params — so the API must be up for this task. This is the one task that may require the controller/human to start the dev stack.

- [ ] **Step 1: Build + run the API** (catalog changes included)

Run (PowerShell, from repo root): start the API/dev stack per the project's run path so `:8080/openapi/v1.json` serves the new contract. (Cold-start; no stale process.)

- [ ] **Step 2: Regenerate**

Run: `cd web && node scripts/codegen.mjs`
Expected: `codegen: fetched live OpenAPI from http://localhost:8080/openapi/v1.json` then `codegen: wrote …/src/generated/openapi.ts`.

- [ ] **Step 3: Verify the contract changed**

Run: `cd web && npx tsc -p tsconfig.app.json --noEmit`
Expected: 0 errors. Confirm `web/openapi-snapshot.json` now contains `"lifecycle"` + `"teamId"` query params on `/api/v1/catalog/applications` and no `"includeDecommissioned"` (search the file).

- [ ] **Step 4: Commit the snapshot**

```bash
git add web/openapi-snapshot.json
git commit -m "chore(web): regenerate openapi snapshot for ListApplications lifecycle/team params"
```

---

### Task 7: `CatalogListPage` + `useApplicationsList` wiring

**Files:**
- Modify: `web/src/features/catalog/api/applications.ts:22-65`
- Modify: `web/src/features/catalog/pages/CatalogListPage.tsx`
- Test: `web/src/features/catalog/pages/__tests__/CatalogListPage.test.tsx`

**Interfaces:**
- Consumes: regenerated `ListApplicationsQuery` type (Task 6); `FilterBar`/`useListFilters`/`useListUrlState` multi-select support (Tasks 1-3); `useTeamsList` (existing) for team options.

- [ ] **Step 1: Update the failing page tests**

In `CatalogListPage.test.tsx`: remove any `includeDecommissioned`-based assertion; add lifecycle + team threading and keep/adjust the filtered-empty-state test.

```tsx
// Helpers (mirror MembersListPage.test.tsx): the multi-select trigger is the button
// whose accessible name matches the spec label.
const lifecycleTrigger = () => screen.getByRole("button", { name: /^lifecycle/i });

it("threads selected lifecycle to apiClient.GET as repeated params", async () => {
  const get = vi.fn().mockResolvedValue({ data: pageOf([]), error: undefined });
  vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET: get, POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() } as never);
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(<CatalogListPage />, { wrapper: harness(qc) });
  await waitFor(() => expect(get).toHaveBeenCalled());
  get.mockClear();

  await userEvent.click(lifecycleTrigger());
  await userEvent.click(await screen.findByRole("option", { name: "Deprecated" }));
  await userEvent.click(screen.getByRole("button", { name: /^search$/i }));

  await waitFor(() =>
    expect(get).toHaveBeenCalledWith(
      "/api/v1/catalog/applications",
      expect.objectContaining({
        params: expect.objectContaining({ query: expect.objectContaining({ lifecycle: ["deprecated"] }) }),
      }),
    ),
  );
});

it("omits lifecycle/teamId from the query when nothing is selected", async () => {
  const get = vi.fn().mockResolvedValue({ data: pageOf([]), error: undefined });
  vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET: get, POST: vi.fn(), PUT: vi.fn(), DELETE: vi.fn() } as never);
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(<CatalogListPage />, { wrapper: harness(qc) });
  await waitFor(() => expect(get).toHaveBeenCalled());
  const query = get.mock.calls[0][1].params.query;
  expect(query.lifecycle).toBeUndefined();
  expect(query.teamId).toBeUndefined();
});
```
> Reuse the file's existing `harness`, `pageOf`, and the `useTeamsList` mock (the page already calls it). If the existing test mocks `useTeamsList`, ensure it returns at least one team so the team multi-select renders options; the lifecycle assertions don't depend on teams.

- [ ] **Step 2: Run to verify the new cases fail**

Run: `cd web && npx vitest run src/features/catalog/pages/__tests__/CatalogListPage.test.tsx`
Expected: FAIL (page still sends `includeDecommissioned`, no lifecycle/teamId).

- [ ] **Step 3: Update `useApplicationsList`** (`applications.ts`)

```ts
type ApplicationsListParams = {
  sortBy: NonNullable<ListApplicationsQuery["sortBy"]>;
  sortOrder: NonNullable<ListApplicationsQuery["sortOrder"]>;
  limit?: number;
  /** ADR-0107 lifecycle multi-select (wire values active|deprecated|decommissioned).
   *  Empty/undefined ⇒ omitted ⇒ ADR-0073 default view (hide Decommissioned). */
  lifecycle?: string[];
  /** ADR-0107 team multi-select (team ids). Empty/undefined ⇒ omitted. */
  teamId?: string[];
  createdByUserId?: string;
  displayNameContains?: string;
};
```
And in `fetchPage`'s query object, replace the `includeDecommissioned` line with conditional spreads:
```ts
          query: {
            sortBy: params.sortBy,
            sortOrder: params.sortOrder,
            limit: params.limit ?? 50,
            cursor,
            ...(params.lifecycle?.length ? { lifecycle: params.lifecycle } : {}),
            ...(params.teamId?.length ? { teamId: params.teamId } : {}),
            ...(params.createdByUserId ? { createdByUserId: params.createdByUserId } : {}),
            ...(params.displayNameContains ? { displayNameContains: params.displayNameContains } : {}),
          },
```

- [ ] **Step 4: Update `CatalogListPage`**

```tsx
const ALLOWED_SORT_FIELDS = ["createdAt", "displayName"] as const;
const TEXT_FILTERS = ["displayNameContains"] as const;
const MULTI_FILTERS = ["lifecycle", "teamId"] as const;
const LIFECYCLE_OPTIONS = [
  { label: "Active", value: "active" },
  { label: "Deprecated", value: "deprecated" },
  { label: "Decommissioned", value: "decommissioned" },
];

export function CatalogListPage() {
  const urlState = useListUrlState({
    defaultSortBy: "displayName",
    defaultSortOrder: "asc",
    allowedSortFields: ALLOWED_SORT_FIELDS,
    textFilters: TEXT_FILTERS,
    multiFilters: MULTI_FILTERS,
  });

  const teamsList = useTeamsList({ sortBy: "displayName", sortOrder: "asc", limit: 200 });
  const teamNameById = useMemo(
    () => new Map<string, string>((teamsList.items ?? []).map(t => [t.id, t.displayName])),
    [teamsList.items],
  );

  // FILTER_SPECS is dynamic: team options come from the teams fetch. Lifecycle +
  // search are static. (Known limit: the team dropdown shows only the first 200
  // teams — same cap as the existing teamNameById lookup; see spec §2.)
  const filterSpecs: FilterSpec[] = useMemo(
    () => [
      { key: "displayNameContains", type: "text", label: "Search applications", placeholder: "Search by name…" },
      { key: "lifecycle", type: "multi-select", label: "Lifecycle", placeholder: "Any status", options: LIFECYCLE_OPTIONS },
      {
        key: "teamId",
        type: "multi-select",
        label: "Team",
        placeholder: "All teams",
        options: (teamsList.items ?? []).map(t => ({ label: t.displayName, value: t.id })),
      },
    ],
    [teamsList.items],
  );
  const filters = useListFilters(filterSpecs, urlState);

  const list = useApplicationsList({
    sortBy: urlState.sortBy,
    sortOrder: urlState.sortOrder,
    displayNameContains: filters.queryFilters.displayNameContains as string | undefined,
    lifecycle: filters.queryFilters.lifecycle as string[] | undefined,
    teamId: filters.queryFilters.teamId as string[] | undefined,
  });

  const [dialogOpen, setDialogOpen] = useState(false);
  const { hasPermission, isLoading: permissionsLoading } = usePermissions();
  const canRegister = !permissionsLoading && hasPermission(KartovaPermissions.CatalogApplicationsRegister);

  useEffect(() => {
    if (list.isError) console.error("CatalogListPage list error", list.error);
  }, [list.isError, list.error]);

  // ... JSX unchanged except <FilterBar specs={filterSpecs} urlState={urlState} /> ...
}
```
Update the two references that used the module-level `FILTER_SPECS` (the `useListFilters` call and the `<FilterBar specs=…>` prop) to `filterSpecs`. Remove the `BOOLEAN_FILTERS` const and the old module-level `FILTER_SPECS`.

- [ ] **Step 5: Run the page tests + typecheck + lint**

Run: `cd web && npx vitest run src/features/catalog/pages/__tests__/CatalogListPage.test.tsx && npx tsc -p tsconfig.app.json --noEmit && npx eslint src/features/catalog`
Expected: PASS, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add web/src/features/catalog/api/applications.ts web/src/features/catalog/pages/CatalogListPage.tsx web/src/features/catalog/pages/__tests__/CatalogListPage.test.tsx
git commit -m "feat(web): Applications list onto lifecycle + team multi-select filters"
```

---

### Task 8: Docs — list-filter registry + ADR-0107 as-built note

**Files:**
- Modify: `docs/design/list-filter-registry.md`
- Modify: `docs/architecture/decisions/ADR-0107-list-filtering-consideration-and-filterbar-ui.md`

**Interfaces:** none (documentation).

- [ ] **Step 1: Update the registry Applications row**

Change the Applications row so the Filters cell reads (keeping the table's column structure):
`displayNameContains` + `lifecycle` (multi-select) + `teamId` (multi-select) via `<FilterBar>`; status **built**; note: "Lifecycle multi-select replaces the includeDecommissioned boolean (none-selected ⇒ ADR-0073 default-hide). Team multi-select (first 200 teams). Sort opt-out for both (explicit). Pulled forward from E-05."

- [ ] **Step 2: Update the control-availability line** in the registry: move `multi-select` from "reserved/deferred" to "built"; leave `date-range` reserved.

- [ ] **Step 3: Add an ADR-0107 as-built note** (alongside the existing clause-3 note) recording that the `multi-select` control is built as a react-aria `ListBox(selectionMode=multiple)` in a `DialogTrigger` popover, mirrors selection into hidden inputs for the FormData commit path, and rides the new `useListUrlState.multiFilters` repeated-param axis; cursor `f`-map value is the sorted comma-joined selection (present only when non-empty).

- [ ] **Step 4: Commit**

```bash
git add docs/design/list-filter-registry.md docs/architecture/decisions/ADR-0107-list-filtering-consideration-and-filterbar-ui.md
git commit -m "docs: registry + ADR-0107 as-built note for multi-select (Applications lifecycle/team)"
```

---

## Self-Review

**1. Spec coverage:**
- §5.1 multi-value URL axis → Task 2 ✅
- §5.2 MultiSelect control → Task 1 ✅
- §5.3 FilterSpec/FilterBar/useListFilters → Task 3 ✅
- §5.4 backend params/predicates/f-map + remove includeDecommissioned → Task 4 ✅
- §5.5 CatalogListPage + useApplicationsList wiring → Task 7 ✅
- §4 lifecycle semantics (empty ⇒ default-hide) → Task 4 handler + Task 5 default-excludes test ✅
- §8 real-seam integration tests (lifecycle happy/edge/reveal, team subset+union, combined-implied, cursor mismatch) → Task 5 ✅ (combined lifecycle+team end-to-end not separately asserted at the HTTP seam — covered at the unit seam in Task 4 + each filter independently at the HTTP seam; acceptable, but Task 5 may add one combined HTTP test if cheap)
- §8 frontend units (MultiSelect, FilterBar, useListFilters, useListUrlState, CatalogListPage) → Tasks 1,2,3,7 ✅
- §8 OpenAPI regen → Task 6 ✅
- §3/§10 registry + ADR → Task 8 ✅
- Gate 6 mutation (blocking) → run during DoD after Task 7 on `ListApplicationsHandler` + endpoint delegate; not a code task (process gate).

**2. Placeholder scan:** No TBD/TODO; every code step shows code; commands have expected output. The two "if the implementer finds…" notes are conditional call-site fixes (Owner tests, useListFilters literals), not placeholders.

**3. Type consistency:** `multiFilters` (read map) / `multi` (setFilters key) used consistently; `ListApplicationsQuery` positional order `(…, Lifecycle, TeamId, DisplayNameContains?, CreatedByUserId?)` used identically in Task 4 record, endpoint, and unit tests; f-map keys `lifecycle`/`teamId` match URL + API param names; lifecycle wire tokens lowercase everywhere, f-map values use C# enum names (`Active` etc.) sorted — the mismatch test asserts `"Active,Decommissioned,Deprecated"` which matches `OrderBy(Ordinal)` on the enum names.

## Execution Handoff

**Plan complete.** Recommended: Subagent-Driven (fresh subagent per task + spec/quality review between tasks). Note the cross-task dependency: **Task 6 (OpenAPI regen) needs the API running with Task-4 changes**, and Tasks 6→7 are sequential. Gate 6 (mutation) is blocking for this slice.
