# Deep Review — `feat/filter-surface-members-single-select` (PR #40)

**Status:** OPEN (pre-merge gate)
**Range:** `2104178..e807c01`
**Spec:** `docs/superpowers/specs/2026-06-23-finish-list-filter-surface-members-design.md`
**Plan:** `docs/superpowers/plans/2026-06-23-finish-list-filter-surface-members-plan.md`
**ADRs:** ADR-0107 (filter mandate + `<FilterBar>` UI), ADR-0095 (cursor/`f`-map), ADR-0094 (Untitled UI / react-aria), ADR-0097 (test taxonomy)
**DoD:** `CLAUDE.md §Definition of Done`
**Prior gates:** 4 per-task reviews, opus whole-branch review (merge-yes), `/simplify`, `review-pr` (added Clear-all + q-thread tests).

### Overview

Sub-slice 1 of finishing the list-filter surface: adds the deferred `single-select` `<FilterBar>` control (on a new reusable react-aria `Select`) and moves the Members list (`/members`) onto the standard ADR-0107 surface — role single-select + name/email (`q`) text search, both submit-driven and URL-backed — deleting the bespoke `<select>` + debounced `<input>`. Frontend-only; the backend `GET /api/v1/organizations/users` already binds `role`+`q`. Multi-select and date-range remain reserved for sub-slices 2 & 3.

### Blocking-class issues

None.

Every §3 spec decision is implemented and matches the design: single-select rides the `textFilters` axis (`useListFilters.ts:22`, `MembersListPage.tsx` `textFilters:["role","q"]`); base `Select` is `FormData`-readable via `name` (`select.tsx:52`); FilterBar commits single-select through the `text` map (`FilterBar.tsx` `commit()`); "All roles"=`""`→param dropped (live-verified). All four plan tasks' acceptance criteria are honored. No ADR deviation: ADR-0107 control vocabulary (single-select is listed; registry updated per clause 1); ADR-0107 clause 3 permits non-text controls to apply immediately, and choosing submit-driven uniformity instead is conformant, not a violation; ADR-0094 react-aria primitives used. No real-seam gate applies (frontend-only; no HTTP/auth/DB change).

### Should-fix issues

**1. Base `Select` accepts neither `label` nor `aria-label` — silent a11y failure for a future caller.**
- **Evidence:** `web/src/components/base/select/select.tsx:21-23` — both `"aria-label"?` and `label?` are optional; nothing requires at least one accessible name.
- **Impact:** `<FilterBar>` always passes `aria-label={spec.label}` (`FilterBar.tsx` single-select branch), so **no issue in this slice**. But `Select` is now a shared base component; a future caller omitting both ships an unnamed combobox (WCAG 4.1.2 failure) with no compile-time or test signal.
- **Fix:** when sub-slice 2/3 or another screen first reuses `Select`, tighten the prop type to require one accessible name (e.g. a union `{ label: string } | { "aria-label": string }`), or add a dev-time `console.warn` when both are absent. Defer to the first additional consumer rather than speculating on the shape now.

### Nits

**1. `id=""` on the "All roles" `ListBoxItem` → `aria-activedescendant=""` edge.**
- **Evidence:** `web/src/components/base/select/select.tsx:73` (`id={item.value}`, value `""` for All roles).
- **Impact:** functionally verified working — the Task-1 test asserts `FormData.get("role") === ""` and the live stack showed All-roles selectable. The empty-string key is **required** for the `""`→param-drop contract, so the obvious "remap id to a sentinel" fix is wrong (it would put the sentinel on the wire). The residual is a theoretical SR `aria-activedescendant=""` nuance.
- **Fix:** none safe without breaking the FormData contract; leave as-is. Revisit only if a real SR defect is observed (would need a hidden-input mapping, not an id remap).

**2. `isActive` / `activeCount` computed by two separate `useMemo`s with the same predicate.**
- **Evidence:** `web/src/lib/list/filters/useListFilters.ts:33-44` (pre-existing; this PR only widened `textSpecs`).
- **Impact:** duplicated traversal of small spec arrays; can't drift in practice (same inputs). Cosmetic.
- **Fix:** derive `isActive = activeCount > 0` from a single count memo. Pre-existing — fold into the tracked `useListFilters` cleanup, not this slice.

**3. `useListFilters` test pins the exact return-key set (no draft/bind/submit).**
- **Evidence:** `web/src/lib/list/filters/__tests__/useListFilters.test.tsx` (the "returns only {…}" assertion).
- **Impact:** guards API surface but breaks on any additive (non-breaking) return-shape change. Minor fragility.
- **Fix:** assert the three keys are present rather than that no others exist. Optional.

### Missing tests

Coverage of the load-bearing behaviors is adequate (Select render/seed/FormData; useListFilters single-select queryFilters+isActive; FilterBar render/commit/seed/Clear-all; Members role+q thread; filtered empty-state; multi-select throw guard). Two low-priority gaps remain — branches that exist but are unexercised:

1. **Unfiltered empty-state ("No members yet").** `MembersListPage.tsx` renders `filters.isActive ? "No members match…" : "No members yet"`; only the active side is tested (filtered-empty test). *Test:* `MembersListPage.test.tsx` — render `pageOf([])` with no filter interaction, assert "No members yet". A conditional swap would otherwise go undetected.
2. **"All roles" → param dropped, end-to-end.** Each layer is unit-tested (FormData `""`; `useListFilters` `""`→`undefined`); no single test walks select-All → Search → `setFilters({text:{role:""}})` → `queryFilters.role === undefined`. *Test:* `FilterBar.test.tsx` single-select describe — low risk, composition only.

Both are Suggestions, not blockers — no acceptance criterion is unverified.

### What looks good

1. **Single-select rides the `textFilters` axis — zero new wire concept.** `useListFilters.ts:22` folds `single-select` into `textSpecs`, so `queryFilters`/`isActive`/`activeCount` treat it identically to text. A single-select value genuinely *is* a string URL param; the design landed the abstraction at the right boundary (multi-select, which needs repeated params, is correctly carved to its own axis in sub-slice 2).
2. **`FormData` contract is real, not asserted.** `select.tsx:73` (`id={item.value}`) + `name` on `AriaSelect` make the selected value flow through react-aria's hidden form control; the Task-1 test proves `FormData.get("role")` returns `""` for All-roles and the chosen value otherwise — verifying the empty-string-key risk instead of hand-waving it.
3. **Bespoke chrome fully removed.** `MembersListPage.tsx` deletes `ROLE_OPTIONS`, `DEBOUNCE_MS`, the `roleFilter`/`searchInput`/`debouncedQ` state, the debounce `useEffect`, the ≥2-char guard, and the raw `<select>`/`<input>` — no orphaned imports/vars. The last pre-standard list is now standard.
4. **Registry updated per ADR-0107 clause 1.** `docs/design/list-filter-registry.md` — Members row → **built** (drops "(pre-standard)"), control-availability note adds single-select; the canonical per-list record stays accurate.
5. **Lean sub-slice decomposition.** Spec/plan split the user's "all three controls" into three shippable sub-slices; this PR is ~180 production LOC, well under the slice ceiling, and de-risks the shared mechanism on the one sub-slice with a real screen consumer.
