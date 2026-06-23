# Slice — Finish the list-filter surface: Members onto `<FilterBar>` (single-select control) + close out the reserved controls

**Date:** 2026-06-23
**Stories:** E-03.F-01.S-05 (Members directory — list filtering). Third+ consumer of **ADR-0107** (list-filter consideration mandate + standard `<FilterBar>` UI). Resolves the `Members / Users` row in `docs/design/list-filter-registry.md` (drops the "(pre-standard)" qualifier).
**Phase:** 1 — Core Catalog & Notifications
**Branch (proposed, sub-slice 1):** `feat/filter-surface-members-single-select`

---

## 1. Goal

Bring the **Members** list (`/members`, `GET /users`) — the last list still on bespoke, pre-ADR-0107 filter chrome — onto the standard `<FilterBar>` / `useListFilters` / `useListUrlState` surface, and in doing so build the deferred **`single-select`** control (the role filter is its first consumer).

Per the user decision (2026-06-23) the **multi-select** and **date-range** controls are also in scope overall, but the work is **decomposed into three sequential, independently-shippable sub-slices**:

| Sub-slice | Ships | Real consumer? |
|---|---|---|
| **1 (this spec)** | `single-select` control + Members fully onto `<FilterBar>` (role + name/email search) | ✅ Members `role` |
| **2 (own spec)** | `multi-select` control + `multiFilters` URL axis | ❌ — Tag filtering (E-03.F-04.S-03) is the eventual consumer |
| **3 (own spec)** | `date-range` control + `dateRangeFilters` URL axis | ❌ — no backlog consumer yet |

This spec details **sub-slice 1**. Sub-slices 2 & 3 are outlined in §8; each gets its own brainstorm → spec → plan → PR and follows the same mechanism (Approach A).

The decomposition keeps each PR shippable and under the slice-size ceiling, and front-loads the only sub-slice with a real screen consumer (Members), which de-risks the shared mechanism.

---

## 2. Pre-requisites (already on master)

- **Filter surface (PR #39, as-built):** `<FilterBar specs urlState />` with **uncontrolled** controls committed on Search/Enter via `FormData` → one atomic `urlState.setFilters({ text, booleans })`; `useListFilters(specs, urlState)` is **pure-derived** (`{ queryFilters, isActive, activeCount }`) from the committed URL; controls keyed by committed value; `text` + `boolean` branches built, `single-select`/`multi-select`/`date-range` **throw**. (See `docs/superpowers/specs/2026-06-22-list-filter-surface-catalog-design.md` §11 for the as-built mechanism.)
- **URL state:** `useListUrlState` with `allowedSortFields` + `booleanFilters` + `textFilters` axes and the atomic `setFilters`. `textFilters` accepts arbitrary string keys.
- **`FilterSpec`** discriminated union in `web/src/lib/list/filters/types.ts` — `single-select`/`multi-select`/`date-range` typed but not built (no payloads).
- **Members list (current, pre-standard):** `MembersListPage.tsx` holds `roleFilter` (raw `<select>`: All/Viewer/Member/OrgAdmin) and `searchInput` → 250 ms debounce → `debouncedQ` (`q`, applied only at ≥2 chars) in **component state, not URL-backed**; `useListUrlState` drives sort only (`displayName`/`role`/`createdAt`). `useMembersList({ sortBy, sortOrder, role, q })`. Backend `GET /users` already accepts `role` + `q` (name **and** email substring).
- **Base UI:** no reusable `Select`/date/combobox base component exists (only a menu `dropdown` and the feature-level `UserSearchCombobox`). react-aria-components + Tailwind v4 per ADR-0094.

---

## 3. Decisions (sub-slice 1)

| # | Decision | Rationale |
|---|---|---|
| 1 | **`single-select` rides the existing `textFilters` URL axis** — its URL form is a plain string param (`?role=Viewer`). **No new `useListUrlState` axis** in this sub-slice (the `multiFilters`/`dateRangeFilters` axes land in sub-slices 2/3). | A single-select value is indistinguishable from a text param at the URL/transport level; reuse the proven `text` set/commit path (trim, drop-when-blank) end-to-end. Zero new wire concepts. |
| 2 | **Build a base `Select`** at `web/src/components/base/select/select.tsx` on react-aria-components `Select`, supporting `name` + `defaultSelectedKey` + `options` + Untitled UI tokens. | First reusable form Select in the repo (ADR-0094). `name` + `defaultSelectedKey` make it **uncontrolled + `FormData`-readable**, so it fits the shipped commit model without a controlled-state exception. |
| 3 | **`<FilterBar>` `single-select` branch** renders the base `Select` keyed by the committed value; **commit reads `FormData.get(key)` into the existing `text` map**. All controls stay **submit-driven** (Search/Enter), uniform with the shipped text/boolean. | Keeps one commit path. `single-select` is just another string written on submit. |
| 4 | **`FilterSpec` `single-select` variant gains `options: { label: string; value: string }[]`.** `multi-select` will later carry the same `options`; `date-range` stays bare. | Declarative control config (ADR-0107 clause 4) — the screen passes options, `<FilterBar>` renders. |
| 5 | **"All roles" sentinel = option `value: ""`** → commit drops the empty value → `role` param **absent** = no filter. | Mirrors the text "blank ⇒ absent" rule; no special-case "all" string on the wire. |
| 6 | **Members search becomes submit-driven + URL-backed**; **delete the 250 ms debounce and the ≥2-char guard**. `q` still searches name **and** email (unchanged backend contract); blank ⇒ absent. | ADR-0107 clause 3 standard; removes the last bespoke live-filter behavior; makes filters shareable (`?role=&q=`). User decision 2026-06-23. |
| 7 | **`useListFilters` treats `single-select` like `text`** for `queryFilters` (`string \| undefined`) and `isActive`/`activeCount` (active when non-empty) — both read `urlState.textFilters[key]`. | The value lives in the `textFilters` map (decision #1); the only difference from `text` is the rendered control, which is `<FilterBar>`'s concern, not the hook's. |
| 8 | **Backend untouched.** `GET /users` already binds `role` + `q`. **Frontend-only slice — not a wiring slice**, so no real-seam (HTTP/auth/DB) test mandate. | No endpoint/handler/contract change; the existing `ListUsers` real-seam coverage stands. |
| 9 | **Add the filtered empty-state** ("No members match your filters" when `filters.isActive`, else "No members yet"). | Mirrors the other lists; distinguishes "no data" from "no matches" (ADR-0107 clause 5). |
| 10 | **Surface proposal (refactor, not expansion):** `role` → single-select **built**; `q` (name+email) → text **built**. Columns + sort allowlist (`displayName`/`role`/`createdAt`) **unchanged**; no new filter fields. Registry row drops "(pre-standard)". | Members already exists; this is a chrome standardization, so the ADR-0107 surface mandate is satisfied by the existing registry row plus this confirmation. |

---

## 4. Architecture

### 4.1 Data flow (Members)

```
MembersListPage
 ├ FILTER_SPECS = [
 │    { key:"role", type:"single-select", label:"Role",
 │      options:[ {label:"All roles",value:""}, {label:"Viewer",value:"Viewer"},
 │                {label:"Member",value:"Member"}, {label:"OrgAdmin",value:"OrgAdmin"} ] },
 │    { key:"q", type:"text", label:"Search members", placeholder:"Search by name or email…" } ]
 ├ urlState = useListUrlState({ defaultSortBy:"displayName", defaultSortOrder:"asc",
 │              allowedSortFields:["displayName","role","createdAt"], textFilters:["role","q"] })
 ├ filters  = useListFilters(FILTER_SPECS, urlState)
 ├ list     = useMembersList({ sortBy, sortOrder,
 │              role: filters.queryFilters.role,   // string | undefined
 │              q:    filters.queryFilters.q })     // string | undefined
 ├ <FilterBar specs={FILTER_SPECS} urlState={urlState} />   // role Select + q input + Search + Clear all
 └ table   empty → filters.isActive ? "No members match your filters" : "No members yet"

GET /api/v1/users?sortBy=displayName&sortOrder=asc&limit=50&role=Viewer&q=ann   (role/q absent when blank)
```

### 4.2 File map (sub-slice 1)

**Frontend — created:**

| File | Purpose | ~LOC |
|---|---|---|
| `web/src/components/base/select/select.tsx` | Styled react-aria-components `Select` — `name`, `defaultSelectedKey`, `options`, `aria-label`/label, Untitled UI tokens; uncontrolled + `FormData`-readable. | ~90 |
| `web/src/components/base/select/__tests__/select.test.tsx` | renders options; selection; `name` in `FormData`; `defaultSelectedKey` seeds. | — |

**Frontend — modified:**

| File | Change |
|---|---|
| `web/src/lib/list/filters/types.ts` | Split `single-select` out of the reserved union into `{ key; type:"single-select"; label; options: {label;value}[] }`. `multi-select`/`date-range` stay reserved (built in sub-slices 2/3). |
| `web/src/components/application/filter-bar/FilterBar.tsx` | Add the `single-select` branch (render base `Select`, keyed by committed value, `defaultSelectedKey`); in `commit()`, `single-select` → `text[key] = String(data.get(key) ?? "")`. Keep throwing for `multi-select`/`date-range`. |
| `web/src/lib/list/filters/useListFilters.ts` | Handle `single-select` identically to `text` (read `textFilters[key]` → `queryFilters` string\|undefined; count toward `isActive`/`activeCount`). |
| `web/src/features/members/pages/MembersListPage.tsx` | Delete `roleFilter`/`searchInput`/`debouncedQ` state + debounce `useEffect` + raw `<select>`/`<input>` + ≥2-char guard. Add `textFilters:["role","q"]`, module-level `FILTER_SPECS`, `useListFilters`, `<FilterBar>`, filtered empty-state. Thread `queryFilters.role`/`.q` into `useMembersList`. Sort + table unchanged. |
| `docs/design/list-filter-registry.md` | Members/Users row → drop "(pre-standard)"; control-availability note → **single-select built** (multi-select/date-range still reserved → sub-slices 2/3). |

**Frontend — tests modified:** `FilterBar.test.tsx` (+single-select render/commit/seed/Clear-all), `useListFilters.test.tsx` (+single-select queryFilters/isActive), `MembersListPage.test.tsx` (rewrite for FilterBar; bespoke chrome removed; role+q thread; filtered empty-state).

**Backend:** none. **Generated/codegen:** none (no contract change).

**Estimate ≈ 180–250 production LOC** (Select ~90, FilterBar branch + commit ~15, useListFilters ~10, MembersListPage net small, types ~3; excludes tests). Under the ~400 target → single sub-slice, no further decomposition.

---

## 5. Components

### 5.1 Base `Select`
react-aria-components `Select` + `Label`/`Button`/`Popover`/`ListBox`/`ListBoxItem`, styled to Untitled UI tokens (mirror the visual language of `Input`/`Checkbox`). Props: `name`, `aria-label`/`label`, `defaultSelectedKey`, `options: {label,value}[]`, `size`, `placeholder`. Renders react-aria's hidden form input so `name` is captured by `FormData` (the uncontrolled commit path). No controlled `selectedKey` needed for `<FilterBar>` (uncontrolled + keyed-by-committed re-seeds on external change).

### 5.2 `<FilterBar>` single-select branch
```tsx
if (spec.type === "single-select") {
  const committed = committedText[spec.key] ?? "";
  return (
    <div key={`${spec.key}:${committed}`} className="w-full sm:w-56">
      <Select name={spec.key} defaultSelectedKey={committed} aria-label={spec.label} options={spec.options} size="sm" />
    </div>
  );
}
```
`commit()` gains: `else if (s.type === "single-select") text[s.key] = String(data.get(s.key) ?? "");`. Submit-driven (Search/Enter); seeded from committed value; "All roles" (`""`) → dropped → param absent. Lives in the controls body; Search/Clear-all in the footer (unchanged from PR #39).

### 5.3 `useListFilters` single-select
In the per-spec derivation, `single-select` joins the `text` case: `queryFilters[key] = (textFilters[key] ?? "") || undefined`; contributes to `isActive`/`activeCount` when non-empty. No change to the boolean path or the return shape.

---

## 6. Testing strategy (gate-3; no gate-5 real-seam — frontend-only)

Per [docs/TESTING-STRATEGY.md](../../TESTING-STRATEGY.md). No HTTP/auth/DB/middleware change → **no real-seam mandate**; the existing `ListUsers` integration coverage (role/q over RLS + real JWT) already exercises the backend contract and is untouched.

**Frontend (Vitest)** — ≥1 happy + ≥1 negative per unit:
- **`Select` base:** renders all options; selecting updates the value; `name` value appears in a wrapping form's `FormData`; `defaultSelectedKey` seeds the initial selection; "All roles" selectable.
- **`<FilterBar>`:** a `single-select` spec renders a Select with its options; choosing a value + Search calls `urlState.setFilters` with the value in `text`; selecting "All roles" commits empty (param drop); the control seeds from committed `textFilters`; `Clear all` resets it; `multi-select`/`date-range` still throw.
- **`useListFilters`:** `single-select` → `queryFilters[key]` string when set / `undefined` when blank; counts toward `isActive`/`activeCount`.
- **`MembersListPage`:** role Select + search render inside `<FilterBar>` (no raw `<select>`/debounced `<input>` remain); selecting a role + typing a query + Search threads `role`/`q` into `useMembersList` (mocked); filters are URL-backed; filtered empty-state vs "No members yet"; default sort `displayName asc` unchanged.

**Manual verification (ADR-0084):** Playwright MCP cold-start → `/members` → pick a role + type a name/email → Search applies both → URL carries `?role=…&q=…` → "Clear all" restores → role/q survive a shared-link reload (and the deep-link OIDC restore from PR #39's fix). *Pending user verification* if the dev stack is unavailable in-session.

---

## 7. Definition of Done

CLAUDE.md → Working agreements → **Definition of Done** (eight always-blocking gates + conditional mutation) applies verbatim; not restated here.

**Mutation gate (6): N/A** — the diff is TypeScript/React only (no C# Domain/Application logic), outside Stryker scope.

Run `scripts/ci-local.sh frontend` green before push. Steps needing the running stack (Playwright MCP) → flagged *pending user verification* if unavailable.

**On completion — registry (`docs/design/list-filter-registry.md`):**
- Members/Users row → **built**; drop "(pre-standard)"; filters `role` (single-select) + `q` (text, name+email).
- Control-availability note → text + boolean + **single-select** built; multi-select/date-range still reserved (→ sub-slices 2/3).

---

## 8. Sub-slices 2 & 3 (outline — own spec each)

Both follow Approach A (typed `useListUrlState` axes) and the uncontrolled/submit-driven `<FilterBar>` model.

**Sub-slice 2 — `multi-select`:**
- `FilterSpec` `multi-select` gains `options`. New `useListUrlState` axis `multiFilters` (repeated URL param `?key=a&key=b`, read via `params.getAll`, write replaces all); `setFilters` extended. `queryFilters[key]` → `string[]`.
- **`<FilterBar>` deviation:** no native multi-value form field exists, so the multi-select branch uses **component-local committed-on-Search state** (seeded from the committed URL, keyed), read on commit instead of `FormData`. Documented, isolated to this branch.
- No production consumer this sub-slice → covered by `Select`/multi-select component + `<FilterBar>` + `useListFilters` unit tests; the eventual consumer is Tag filtering (E-03.F-04.S-03), which owns AND/OR combination semantics (a backend concern; the control stays semantics-agnostic, emitting a value set).

**Sub-slice 3 — `date-range`:**
- New `useListUrlState` axis `dateRangeFilters` (`?keyFrom=…&keyTo=…`); `queryFilters[key]` → `{ from?: string; to?: string }` (ISO date strings).
- `<FilterBar>` branch = two native `<input type="date" name=keyFrom/keyTo>` → uncontrolled + `FormData` fits; commit writes both into the range axis.
- No backlog consumer → control + axis unit tests only; ships closing out the reserved `FilterSpec` types.

---

## 9. Out of scope (explicit deferrals)

- **Multi-select & date-range controls** — built in sub-slices 2 & 3 (own specs), not this PR.
- **Tag filtering** (E-03.F-04.S-03) — the eventual multi-select consumer; its AND/OR semantics + live-vs-submit choice belong to that slice.
- **Members table → `<DataTable>`** — the raw table stays; only the filter surface is standardized.
- **`UserSearchCombobox` convergence onto the new base `Select`** — possible later cleanup, not required here.
- **Backend `GET /users` changes** — none; contract already supports `role` + `q`.
- **New sort fields / allowlist changes** — none.

---

## 10. Self-review

**Spec coverage:** every §3 decision traces to §4–§6; each gate-3 artifact in §6 is a named file/area in §4.2 (base `Select` + test, `FilterBar` branch, `useListFilters`, `MembersListPage`, registry) for writing-plans to turn into a task.

**Internal consistency:** `role`/`q` are one identifier across URL param ↔ `textFilters` ↔ `queryFilters` ↔ API param (§3 #1/#6, §4.1). single-select-as-text-axis consistent across §3 #1/#7, §5.2/§5.3. Submit-driven + uncontrolled consistent with PR #39 (§2, §3 #3/#6). "text + boolean + single-select built; multi-select/date-range reserved" consistent across §1, §3 #4, §4.2, §7, §8.

**Scope check:** ~180–250 production LOC, single PR, well under 400; multi-select/date-range explicitly carved into sub-slices 2/3 to stay shippable and respect the slice-size ceiling.

**Ambiguity check:** "All roles" ⇒ empty value ⇒ param absent (§3 #5); search blank ⇒ absent, no min-length (§3 #6); `q` scope = name+email (unchanged, §3 #6); single-select commit timing ⇒ submit-driven (§3 #3); backend ⇒ untouched, no real-seam (§3 #8); empty-state ⇒ filtered vs none (§3 #9).

**No blocking issues found.**
