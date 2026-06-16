# Deep PR review — feat/sorting-pagination

**Date:** 2026-05-05
**Target:** branch `feat/sorting-pagination` vs `master` (32 commits ahead, 67 files changed, +2 936 / −181 LOC)
**Status:** OPEN — pre-merge gate
**Specs:** [`docs/superpowers/specs/2026-05-04-sorting-pagination-design.md`](../specs/2026-05-04-sorting-pagination-design.md)
**Plan:** [`docs/superpowers/plans/2026-05-04-sorting-pagination-plan.md`](../plans/2026-05-04-sorting-pagination-plan.md)
**ADRs cited:** ADR-0029, ADR-0083, ADR-0090, ADR-0091, ADR-0095 (new in this branch)
**Mutation evidence:** [`docs/superpowers/evidence/2026-05-04-sorting-pagination/mutation-evidence.md`](../evidence/2026-05-04-sorting-pagination/mutation-evidence.md)
**Curl evidence:** [`docs/superpowers/evidence/2026-05-04-sorting-pagination/curl-output.md`](../evidence/2026-05-04-sorting-pagination/curl-output.md)
**Reviewer:** Claude (in-session, single reviewer)

---

## Overview

This slice ratifies a single cursor-pagination + sort contract across the entire stack: a reusable `IQueryable<T>.ToCursorPagedAsync` keyset extension, an opaque base64url cursor codec, an RFC 7807 paging-error handler, an `ApplicationSortField` enum + per-resource `SortSpec` allowlist, generic `useCursorList` / `useListUrlState` frontend hooks, and a `<DataTable>` shell. It is applied to `GET /api/v1/catalog/applications` as the reference list endpoint, freezes ADR-0095 (Accepted), and adds a `PaginationConventionRules` architecture-fitness test that prevents future `List*Handler` regressions. Mutation score lands at 98.92 % (1 accepted near-equivalent survivor).

---

## Blocking-class issues

**None.**

DoD bullets 1–7 are all citable: solution build clean (per branch CI history), per-task and slice-boundary reviews ran (commit log shows /simplify pass at `652e759`), unit + integration + architecture + frontend tests added in commits `8ebad31` / `ccffbad` / `2e17b0f` / `b325cb9`, live-stack curl evidence captured at [`curl-output.md`](../evidence/2026-05-04-sorting-pagination/curl-output.md), Playwright smoke at [`README.md`](../evidence/2026-05-04-sorting-pagination/README.md), mutation feedback loop run to convergence at 98.92 % per [`mutation-evidence.md`](../evidence/2026-05-04-sorting-pagination/mutation-evidence.md).

---

## Should-fix issues

### S1. `?sortOrder=<numeric>` bypasses validation and is silently treated as `desc`

- **Evidence:** `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs:107-115` — `Enum.TryParse<SortOrder>(sortOrder, ignoreCase: true, out var so)` returns `true` for numeric strings (e.g. `"999"`, `"0"`), producing an undefined `SortOrder` value. There is no `Enum.IsDefined` check. Downstream, `src/Kartova.SharedKernel.Postgres/Pagination/QueryablePagingExtensions.cs:68-70` does `order == SortOrder.Asc ? OrderBy : OrderByDescending`, so any non-`Asc` value is treated as `desc`.
- **Spec/ADR violated:** ADR-0095 §Decision item 7 + spec §4.3 — "`sortOrder` not in {asc, desc} → 400 `invalid-sort-order`". `ApplicationSortField` is protected by `ApplicationSortSpecs.Resolve`'s `_ => throw`, but `SortOrder` has no such guard.
- **Impact:** A request like `?sortOrder=999` silently returns the `desc` page instead of erroring. Asymmetric with sortBy validation, breaks the wire contract guarantee, and is a silent-failure-class issue per CLAUDE.md (DoD bullet 6 / silent-failure-hunter pattern).
- **Fix:** in `CatalogEndpointDelegates.ListApplicationsAsync`, after `Enum.TryParse` succeeds, also assert `Enum.IsDefined(typeof(SortOrder), so)` (and `Enum.IsDefined(typeof(ApplicationSortField), sf)` for symmetry, even though sortBy is currently caught downstream). Add a `[Theory] [InlineData("999")] [InlineData("-1")]` integration case in `ListApplicationsPaginationTests.cs` mirroring the existing `OutOfRange_numeric_sortBy` test.

### S2. `useCursorList` queryKey-change reset has a render race that fires a stale request

- **Evidence:** `web/src/lib/list/useCursorList.ts:30-43`. Stack reset is performed inside a `useEffect`, which runs *after* render. Sequence on sort flip:
  1. parent updates `queryKey` from `[..., {sortBy:"createdAt",sortOrder:"desc"}]` → `[..., {sortBy:"name",sortOrder:"asc"}]`
  2. render: `currentCursor` still points at the previous sort's cursor (e.g. `"abc..."` issued for `desc`)
  3. `useQuery` sees a brand-new key `[..., {cursor:"abc..."}]`, fires `fetchPage("abc...")` against `?sortBy=name&sortOrder=asc`
  4. server: cursor direction (`desc`) ≠ request order (`asc`) → `InvalidCursorException` → 400 (see `QueryablePagingExtensions.cs:60-64`)
  5. only *then* does the `useEffect` run, set `stack` to `[undefined]`, render, and fire the correct request.
- **Spec violated:** spec §6.1 promises "`reset()` runs automatically when `queryKey` changes (sort change resets pagination)" — implying no stale fetch.
- **Impact:** every sort-flip emits one wasted 400 (visible in `useQuery.isError` for one frame, observable in network panel + server logs as a paging-error spike). Worse if a flicker-sensitive UI surfaces the error card briefly.
- **Fix:** use the React-recommended "store previous prop in state" pattern so the reset is synchronous within the render that detected the change:
  ```ts
  const [stack, setStack] = useState<(string|undefined)[]>([undefined]);
  const [seenKey, setSeenKey] = useState(keyStr);
  if (seenKey !== keyStr) {
    setSeenKey(keyStr);
    setStack([undefined]);
  }
  ```
  This re-renders before commit; `useQuery` is invoked once, with `currentCursor === undefined`. Add a Vitest case in `web/src/lib/list/__tests__/use-cursor-list.test.tsx`: rerender with a different `queryKey` and assert `fetchPage` is called exactly once with `cursor === undefined` (not twice).

### S3. ADR-0095 implementation note diverges from actual keyset filter shape

- **Evidence:** `docs/architecture/decisions/ADR-0095-cursor-pagination-contract.md:37` says "the keyset filter uses PostgreSQL row-constructor comparison: `(sortKey, id) > (?, ?)` … EF Core's PostgreSQL provider translates this directly". `src/Kartova.SharedKernel.Postgres/Pagination/QueryablePagingExtensions.cs:121-150` actually builds the disjunctive form `keyGreater OR (keyEqual AND idGreater)`, with a string-special-case using `string.Compare`. The XML comment at `:87-91` repeats the row-constructor claim while the code below it does the disjunction.
- **Spec/risk acknowledged:** spec §14 row 1 explicitly listed "fall back to disjunctive form" as a mitigation; the implementer apparently took the fallback. ADR not updated.
- **Impact:** ADR drift — a future contributor reading ADR-0095 will not understand why the SQL plan they observe doesn't use a row-constructor predicate. Compromises the ADR's value as the "what we actually did" record.
- **Fix:** edit ADR-0095 §Implementation notes to read "disjunctive form `key > @p OR (key = @p AND id > @p)` (chosen for portability across PostgreSQL + sqlite test path; row-constructor was the original target but was dropped per spec §14 mitigation)". Update the source XML comment at `QueryablePagingExtensions.cs:87-91` to match.

### S4. ADR-0095 still references `useInfiniteQuery`; implementation uses `useQuery` per cursor

- **Evidence:** `docs/architecture/decisions/ADR-0095-cursor-pagination-contract.md:39` — "`gcTime` on frontend `useCursorList` set to 5 min default to bound `useInfiniteQuery` cache growth." `web/src/lib/list/useCursorList.ts:39` uses `useQuery` (one per cursor entry), and the spec was already updated in commit `835de24` to match.
- **Spec violated:** the spec was corrected; the ADR was not.
- **Impact:** future readers will look for `useInfiniteQuery` and not find it. ADRs are cited from CLAUDE.md as load-bearing references; drift erodes the catalog.
- **Fix:** change "useInfiniteQuery cache growth" → "the per-cursor `useQuery` cache growth" in ADR-0095. Single-line edit.

### S5. OpenAPI does not surface `SortByApplications` enum; frontend type-safety promised by ADR is missing

- **Evidence:** `web/openapi-snapshot.json:71-83` — both `sortBy` and `sortOrder` parameters are declared as plain `{ "type": "string" }`, no `enum`, no `$ref`. `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs:86-88` binds them as `[FromQuery] string?` and parses with `Enum.TryParse(ignoreCase: true)`. The frontend `web/src/features/catalog/api/applications.ts:11-13` consequently re-declares the allowlist as a literal union `"createdAt" | "name"`.
- **Spec/ADR violated:** ADR-0095 §Consequences bullet 2 — "OpenAPI generates per-resource sort-field enums (`SortByApplications`, `SortByComponents`, …); the frontend gets compile-time-safe sort values." Spec §4.1 — "Generated as `SortByApplications` in OpenAPI." Spec §6.3 — "(generated) … Picks up `SortByApplications` enum, `SortOrder` enum".
- **Impact:** the contract enforcement is lost on the frontend — a typo like `"createDat"` would fail at runtime instead of at TypeScript compile time. Adding Components / Services later means hand-maintaining each per-resource literal union. The ADR's "compile-time-safe" promise is unfulfilled.
- **Fix:** bind the parameters as the strong-typed `ApplicationSortField? sortBy, SortOrder? sortOrder` — ASP.NET Core minimal-API parameter binding does respect `JsonStringEnumConverter` for query strings via the configured converter, but `[FromQuery]` enum parameters are by default case-sensitive Pascal-case. Two viable fixes: (a) keep the `string?` binding but inject the enum into OpenAPI via `MapOpenApi` operation transformers (declare the param schema as `enum: [createdAt, name]`); (b) bind as enum, register a custom `IQueryStringValueProvider` or rely on OpenAPI's `Microsoft.AspNetCore.OpenApi` enum support and live with PascalCase-only on the wire (and migrate the frontend literal union to use the generated enum). Either way, regenerate `openapi-snapshot.json` and update `applications.ts` to consume `components["schemas"]["SortByApplications"]` instead of the hand-rolled union.

### S6. Problem-type URL: spec/ADR say `kartova.dev`, code says `kartova.io`

- **Evidence:** `docs/superpowers/specs/2026-05-04-sorting-pagination-design.md:81-84` and `docs/architecture/decisions/ADR-0095-cursor-pagination-contract.md:23` both use `https://kartova.dev/problems/<slug>`. `src/Kartova.SharedKernel.AspNetCore/ProblemTypes.cs:9` defines `Base = "https://kartova.io/problems/"`. The curl evidence at `docs/superpowers/evidence/2026-05-04-sorting-pagination/curl-output.md:92` shows the live response uses `kartova.io`. ADR-0091 (the canonical problem-type ADR) governs the base URI — the code is consistent with ADR-0091.
- **Spec/ADR violated:** ADR-0095 contradicts ADR-0091. Spec §4.3 contradicts the code's published wire shape.
- **Impact:** doc bug only (impl correct), but a future reader / customer following ADR-0095 will look for problem documentation under `kartova.dev` that doesn't exist; the wire contract drift between two ADRs is exactly the kind of thing the deep-review template is designed to catch.
- **Fix:** edit ADR-0095 §Decision item 7 and spec §4.3 table to use `kartova.io`. Add a forward reference to ADR-0091 ("base URI defined in ADR-0091; ADR-0095 only adds the slugs `invalid-sort-field`, `invalid-sort-order`, `invalid-cursor`, `invalid-limit`").

### S7. Pagination types live in `Kartova.SharedKernel`, not the `*.Contracts` assembly the spec specified

- **Evidence:** spec §5.1 prescribed `src/SharedKernel/Kartova.SharedKernel.Contracts/Pagination/CursorPage.cs` and `.../SortOrder.cs`. Actual paths: `src/Kartova.SharedKernel/Pagination/CursorPage.cs:14`, `src/Kartova.SharedKernel/Pagination/SortOrder.cs:6`, `src/Kartova.SharedKernel/Pagination/BoundedListResultAttribute.cs:13`. There is no `Kartova.SharedKernel.Contracts` assembly in the solution.
- **Spec violated:** §5.1 file table.
- **Impact:** silent — the `[ExcludeFromCodeCoverage]` attribute is correctly applied so the coverage rule stays honored, and `ContractsCoverageRules.All_types_in_Contracts_assemblies_have_ExcludeFromCodeCoverage` only enumerates `*.Contracts` assemblies (Catalog + Organization), so the new types aren't covered by that fitness rule. If a future contributor adds a new public DTO under `Kartova.SharedKernel/Pagination/` without `[ExcludeFromCodeCoverage]`, nothing will catch it. Also: spec divergence is undocumented in either the spec or the ADR.
- **Fix:** either (a) introduce a `Kartova.SharedKernel.Contracts` assembly and move the three pure-carrier types there + register it in `AssemblyRegistry.AllContracts()`, or (b) record the deviation as a one-line amendment at the bottom of the spec ("§5.1 corrigendum: pagination contract carriers placed in `Kartova.SharedKernel/Pagination/` rather than a new `*.Contracts` assembly to avoid creating an assembly for three types; ContractsCoverageRules checked manually") and accept it. Option (a) is the cleaner long-term answer because the SortByApplications generated enum (S5 above) belongs alongside `CursorPage<T>` in OpenAPI generation.

---

## Nits

### N1. Redundant `CursorPage` re-construction in `ListApplicationsHandler`

`src/Modules/Catalog/Kartova.Catalog.Infrastructure/ListApplicationsHandler.cs:32-38` re-wraps the result with an explicit `PrevCursor: null` even though `page.PrevCursor` is already null by `ToCursorPagedAsync` contract. Could be `return new CursorPage<ApplicationResponse>(items, page.NextCursor, page.PrevCursor)` — or `page with { Items = items }` if record `with`-semantics are preferred. Makes the intent obvious.

### N2. `fromSort(field: string | null, …)` — null branch never reached

`web/src/components/application/data-table/data-table.tsx:48-54` accepts `field: string | null` but every caller (`ApplicationsTable.tsx:66`, `sortable-head.test.tsx`) passes a non-null value. Tighten the type to `string`.

### N3. Double-cast in `useApplicationsList` defeats the codegen contract

`web/src/features/catalog/api/applications.ts:41` — `return data as unknown as CursorPageEnvelope<ApplicationResponse>`. The generated openapi types should already be assignable; the double cast both hides any future shape drift and undermines S5's "compile-time-safe" goal. Once S5 is fixed, drop the cast.

### N4. XML comment on `ApplyKeysetFilter` describes shape that isn't built

`src/Kartova.SharedKernel.Postgres/Pagination/QueryablePagingExtensions.cs:87-91` claims the expression "translates to a row-constructor comparison in PostgreSQL". The expression tree built below it (line 149) is disjunctive. Update the comment to match S3's resolution.

### N5. Empty-state hides the pager and hasPrev affordance

`web/src/features/catalog/components/ApplicationsTable.tsx:42-53` returns the empty card whenever `list.items.length === 0`, including after navigating Prev to a page that just emptied due to a delete. The Prev/Next bar disappears with it; user has to reload to recover. Low impact at MVP scale (120 seeded rows, no concurrent deletes), but worth a follow-up. Render the empty state *inside* the table body or alongside the pager when `hasPrev` is true.

---

## Missing tests

### M1. No unit test for `ListApplicationsHandler`

Spec §9.2 row 3 listed "Dispatch-to-extension wiring; `ApplicationSortField` → `SortSpec` resolution; default-params path. Uses sqlite or in-memory `CatalogDbContext`." `src/Modules/Catalog/Kartova.Catalog.Tests/` contains `ApplicationTests.cs` and `EfApplicationConfigurationTests.cs` but no `ListApplicationsHandlerTests.cs`. The integration tests cover the end-to-end behavior, but the targeted unit tier promised by the spec is missing.

**Test that should exist:** `src/Modules/Catalog/Kartova.Catalog.Tests/ListApplicationsHandlerTests.cs` — sqlite-backed `CatalogDbContext`, scenarios: (a) default `ApplicationSortField.CreatedAt` resolves to `ApplicationSortSpecs.CreatedAt`; (b) `ApplicationSortField.Name` resolves to `ApplicationSortSpecs.Name`; (c) undefined `ApplicationSortField` value (cast `(ApplicationSortField)999`) throws `InvalidSortFieldException` with `AllowedFields = ["createdAt", "name"]`. This was explicit in the spec; without it, S1 (sort-order numeric bypass) would also have shown up as a sibling pattern at the unit tier.

### M2. No integration test for numeric `?sortOrder` (ties to S1)

`src/Modules/Catalog/Kartova.Catalog.IntegrationTests/ListApplicationsPaginationTests.cs:106-115` covers `?sortOrder=upward` (string parse failure). It does not cover numeric strings like `?sortOrder=999` or `?sortOrder=-1` that bypass `Enum.TryParse` validation. Add `[Theory] [InlineData("999")] [InlineData("-1")] OutOfRange_numeric_sortOrder_returns_400` mirroring the existing `OutOfRange_numeric_sortBy_returns_400_invalid_sort_field`.

### M3. No frontend test that `useCursorList` resets the stack on `queryKey` change (ties to S2)

Spec §9.4 row 2 calls out "`reset()` triggered on `queryKey` change". `web/src/lib/list/__tests__/use-cursor-list.test.tsx` covers explicit `reset()`, `goNext`/`goPrev`, but never re-renders the hook with a different `queryKey`. Add a test that:
1. mounts with `queryKey: ["t","a"]`, advances to a non-undefined cursor;
2. rerenders with `queryKey: ["t","b"]`;
3. asserts `fetchPage` is called *exactly once* on the rerender, with `cursor === undefined`, AND `hasPrev === false`.

This is the regression test for S2 — it should fail today against the `useEffect`-based reset.

### M4. Mutation report — accepted near-equivalent survivor

The single survivor in `mutation-evidence.md` (ParameterReplaceVisitor `_to` short-circuit) is documented as functionally unreachable in the single-parameter visitor. Acceptance is correctly cited inline at `src/Kartova.SharedKernel.Postgres/Pagination/QueryablePagingExtensions.cs:163-167`. No additional test is required — agreed near-equivalent. Listed here for traceability.

---

## What looks good

1. **Architecture-fitness rule pre-empts regression** — `tests/Kartova.ArchitectureTests/PaginationConventionRules.cs:14-57` is exactly the right primitive: it forces every future `List*Handler` to either return `Task<CursorPage<T>>` or carry `[BoundedListResult]` with a non-empty `Reason`. This is the kind of rule that pays back on slice 5, not slice 1.
2. **Single canonical reference for the `_id` shadow property** — `ApplicationSortSpecs.cs:25-42` co-locates the keyset id selector and the `IdEquals` predicate so `EfApplicationConfiguration.IdFieldName` (the magic string `"_id"`) is referenced exactly once outside its own EF configuration. `GetApplicationByIdHandler.cs:23-25` uses `ApplicationSortSpecs.IdEquals(q.Id)` instead of repeating the magic string. Good DRY enforcement under a real EF-translation constraint.
3. **Cursor direction-mismatch detection** — `src/Kartova.SharedKernel.Postgres/Pagination/QueryablePagingExtensions.cs:60-64` refuses a cursor whose `d` does not match the request's `sortOrder`. Subtle, easy to miss, but it prevents the "user navigates next, then flips sort, then re-navigates with the stale cursor" silent-bad-page bug. Integration-tested at `ListApplicationsPaginationTests.cs:213-232`.
4. **BCL-native base64url** — `CursorCodec.cs:82-86` uses `System.Buffers.Text.Base64Url` (.NET 9+) rather than hand-rolling the URL-safe replacement of `+/=`. Smaller surface for tampering bugs; `/simplify` finding correctly applied (commit `652e759`).
5. **Mutation-driven test strengthening** — the loop converged from 89.25 % → 97.85 % → 98.92 % across three passes, and the surviving mutant is documented inline in source with a reference to the evidence file (`QueryablePagingExtensions.cs:163-167`). This is exactly the DoD bullet 7 workflow done well.

---

## Summary

| Tier | Count |
|---|---|
| Blocking | 0 |
| Should-fix | 7 (S1–S7) |
| Nits | 5 (N1–N5) |
| Missing tests | 4 (M1–M4) |
| What looks good | 5 |

**Top should-fix** by impact:
1. **S1** — silent failure on `?sortOrder=<numeric>` (real bug, treated as `desc`)
2. **S2** — `useCursorList` render race fires a wasted 400 on every sort flip
3. **S5** — OpenAPI `SortByApplications` enum missing; frontend type-safety promised by ADR not delivered

S3, S4, S6, S7 are doc/ADR drift — fix as a single follow-up commit. S1 and S2 are runtime bugs — fix before merge or as P0 follow-up. M1–M3 are the regression tests that map onto S1 and S2.
