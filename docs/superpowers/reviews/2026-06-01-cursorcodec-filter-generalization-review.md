# Deep PR Review — CursorCodec filter-state generalization

**Target:** `feat/slice-9-organization-people-management`, range `9ba4f66..HEAD` (cursor-refactor subset). **Status:** OPEN (pre-merge gate).
**Read against:** spec `2026-06-01-cursorcodec-filter-generalization-design.md`, plan `2026-06-01-cursorcodec-filter-generalization-plan.md`, ADR-0095 (+ amendment), `CLAUDE.md §Definition of Done`, test taxonomy ADR-0097.
**Reviewer date:** 2026-06-01.

### Overview

The slice removes Catalog-specific filter fields (`ic`/includeDecommissioned, `ou`/ownerUserId) from the shared `CursorCodec` and `QueryablePagingExtensions`, replacing them with an opaque `string→string` filter map (`f`) plus a new pure `CursorFilterComparer`. `ListApplicationsHandler` now composes the filter map; the shared layer is domain-agnostic. Includes a frontend `gcTime` 5→15 min default bump and an ADR-0095 amendment documenting the generalized wire shape.

### Blocking-class issues

**None** (code). No spec deviation, no architectural drift, no failed acceptance criterion.

DoD status (process gates, not code defects):
- **DoD #5 (docker compose) — ✅ DONE.** Production-image stack built + migrated + started (`docker compose up --build`, exit 0; all services healthy, migrator Exited(0)). Verified against the real composed API (production Dockerfile + real Keycloak JWT + migrated Postgres): unauth → 401; happy page-2 cursor round-trip → 200 with `overlap_with_page1=0` (no dup/skip); negative replay of an `includeDecommissioned=false` cursor with `includeDecommissioned=true` → **400** `cursor-filter-mismatch` (`filterName=includeDecommissioned`, `expectedValue=false`, `actualValue=true`). The production codec emits the new wire shape — observed `nextCursor` decodes to `…"f":{"includeDecommissioned":"false"}}`.
- **DoD #6 (`/simplify`) — ✅ DONE.** 4-lens pass; fixes in `431814e` (FrozenDictionary.Empty, dropped defensive copy); behavior-changing suggestions explicitly skipped.
- **DoD #8 (`/pr-review`) — ✅ DONE.** silent-failure + type-design lenses (other lenses covered by per-task two-stage reviews); fixes in `4c42e6e` (immutable `Filters`, caller-contract doc).
- **DoD #9 (`/deep-review`) — ✅ DONE** (this report).
- **DoD #7 (mutation ≥80%) — DEFERRED by user decision.** Not run this session. Per `CLAUDE.md §DoD`, the slice is not "fully complete" until this is green. Evidence: no `mutation-report-surviving.md` produced. Resolution: run `/misc:mutation-sentinel` on the 4 changed files before merge, or record an explicit waiver. This is the sole outstanding gate.

Follow-up recorded (pre-existing, out of scope): `QueryablePagingExtensions.ConvertCursorValue` throws raw `FormatException`/`InvalidCastException`/`OverflowException` on a tampered *sort-value type* mismatch → escapes as 500 instead of 400 `invalid-cursor` (silent-failure Finding 6). Not introduced by this diff; fix is a localized try/catch → `InvalidCursorException`. Worth a small follow-up PR with a dedicated tampered-sort-value test.

### Should-fix issues

**None.** The review surfaced no follow-up-before-next-slice items. (Earlier per-task code-quality findings — oracle gaps, stale handler comment, `EmptyFilters` duplication, defensive-copy waste — were already addressed in `acadf2c` and `431814e`.)

### Nits

1. **`f` key now always present on applications cursors.** `ListApplicationsHandler.cs:78` always writes `includeDecommissioned`, so every applications cursor carries `f` (the old code omitted `ic` when false). Evidence: `ListApplicationsHandler.cs:76-79`. Impact: a few extra cursor bytes. This is the spec's deliberate choice (§3.4 "always-applied dimensions always present") for correctness over compactness — noted, not a defect.
2. **`CursorFilterComparer` allocates a `SortedSet` per call.** `CursorFilterComparer.cs:28`. For ≤3-key maps once-per-request this is negligible (confirmed by the efficiency lens); deterministic ordering is the explicit design goal. No action.

### Missing tests

1. **gcTime default (15 min) has no asserting test.** `web/src/lib/list/useCursorList.ts:27`. The plan (Task 3) acknowledged this. Suggested test (low value): in `web/src/lib/list/__tests__/use-cursor-list.test.tsx`, render `useCursorList` without `gcTime` and assert the `useQuery` option resolves to `900000` — only worth adding if the default is considered a contract. Otherwise accept as untested constant.
2. **No mutation cross-reference** — DoD #7 deferred, so no surviving-mutant list to convert into tests. If run, expect candidate survivors on the `direction == SortOrder.Asc ? "asc" : "desc"` arm (`CursorCodec.cs`) and the `string.Equals(..., Ordinal)` comparison in `CursorFilterComparer` — both already have killing tests (`Encode_then_Decode_roundtrips_desc_direction`, `Value_comparison_is_ordinal_case_sensitive`).

All spec §8 test-matrix rows are covered: codec `f` round-trip/omit/empty/malformed (`CursorCodecTests.cs`), comparer 8 cases (`CursorFilterComparerTests.cs`), extension mismatch/round-trip/no-filter (`QueryablePagingExtensionsTests.cs`), integration happy + 400 (`ListApplicationsPaginationTests.cs`).

### What looks good

1. **Altitude goal achieved and verified.** Zero domain filter knowledge remains in the shared layer — grep for `includeDecommissioned`/`ownerUserId` in `src/Kartova.SharedKernel*` returns nothing, and `Kartova.ArchitectureTests` (70/70) confirms no SharedKernel→module dependency. The whole point of the refactor landed.
2. **The 3-branch comparer correctly distinguishes "absent key" from a value equal to the `"(none)"` sentinel.** `CursorFilterComparer.cs:37-51`. The tempting 2-branch sentinel-substitution form (raised by the simplify lens) would have introduced a real bug here; the current form is right.
3. **Latent `ic`/`ou` asymmetry fixed.** The old code skipped the `ic` check on opt-out but always ran the `ou` check (`Nullable.Equals`); the unified map comparison (`QueryablePagingExtensions.cs:~80`) applies one consistent either-direction rule. Guarded by `Cursor_carrying_filters_replayed_against_no_filter_request_throws_mismatch`.
4. **Stringification owned by the handler, not the shared layer.** `ListApplicationsHandler.cs:78,82` (`bool→"true"/"false"`, `Guid.ToString("D")`) keeps the codec opinion-free — a future filtered list picks its own conventions without touching shared code.
5. **`FrozenDictionary<string,string>.Empty` for the empty-map sentinel** (`CursorCodec.cs:92`, `QueryablePagingExtensions.cs:~80`) — true zero-alloc BCL singleton, consistent with existing `FrozenSet<T>.Empty` usage; removed two duplicated private fields.

### Verdict

No blocking code findings. DoD gates #1–#6, #8, #9 are green with cited evidence; **#7 (mutation) is the sole outstanding gate, deferred by user decision.** Honest status: **implementation complete and correctness-verified end-to-end (incl. production docker-compose happy + negative paths); merge-ready pending the mutation gate (or an explicit waiver).**
