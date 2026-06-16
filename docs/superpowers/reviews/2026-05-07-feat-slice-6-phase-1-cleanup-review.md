# PR Review — feat/slice-6-phase-1-cleanup (PR #22)

**Date:** 2026-05-07
**Reviewer:** Claude Sonnet 4.6 (deep-review gate)
**Branch:** `feat/slice-6-phase-1-cleanup` → `master`
**Tip commit:** `c9b10d8`
**Mutation report:** `mutation-report-surviving.md` — 93.38%, PASS (≥80%)

---

## Overview

This slice bundles four carry-forward items from slices 3 and 5: `TimeProvider` injection on `Organization.Create` and `Application.Create`, removal of the last `DateTimeOffset.UtcNow` direct calls in aggregate factories, decommissioned-row filtering on `GET /applications` with cursor-encoded filter state and a SPA toolbar checkbox, `RegisterForMigrator` parity tests for both Catalog and Organization modules, and Central Package Management adoption with FluentAssertions pinned at 6.12.0 (Apache-2.0). No new user-facing stories close; the ADR-0073 "filtered out of default views" rule is concretely realized for the first time.

---

## Blocking-class issues

**None.**

All nine DoD gates are citable from the PR description and commit log:
1. Build: `dotnet build Kartova.slnx -c Debug -p:TreatWarningsAsErrors=true` — 0 warnings, 0 errors (PR description).
2. Per-task subagent reviews — mentioned in PR description.
3. `superpowers:requesting-code-review` — commit `01b9dc1` addresses its findings.
4. Tests green: 268 unit/arch + 6 infra + 102 integration + 141 Vitest (PR description).
5. `docker compose up --build` — verified in PR description (API rebuilt, OpenAPI exposes `includeDecommissioned`, auth filter 401). Curl outputs are marked `<paste…>` in the plan file — the PR description narrative confirms real-HTTP runs but the literal capture is in the description prose rather than pasted into the plan placeholder. This is an execution gap against the plan's own format but not a merge-blocker because the PR description confirms all three checks were run.
6. `/simplify` run — commit `4cd92b3`.
7. Mutation score 93.38% ≥ 80%; 9 survivors documented in `mutation-report-surviving.md`.
8. `/pr-review-toolkit:review-pr` — commit `c3ade7e`.
9. `/deep-review` — this review.

---

## Should-fix issues

**SF-1. `CursorFilterMismatchException`: argument guards fire AFTER `base(…)` uses the arguments in the message string.**

- **Evidence:** `src/Kartova.SharedKernel/Pagination/CursorFilterMismatchException.cs:16-21`. The constructor chains to `base($"Cursor was issued for {filterName}=…")` before the `ArgumentException.ThrowIfNullOrWhiteSpace` guards on lines 19-21. A `null` `filterName` therefore produces a `NullReferenceException` during string interpolation in `base(…)` instead of an `ArgumentException` with `paramName="filterName"`. The three surviving mutants at lines 19-21 (`Statement mutation → ;`) are a direct consequence: the guards can be deleted without changing observable behavior when valid arguments are passed, because the message is already constructed by the time they run.
- **Impact:** Guard contract is broken for invalid inputs; mutation report correctly identifies lines 19-21 as survivors. The tests in `CursorFilterMismatchExceptionTests.cs` that assert `WithParameterName("filterName")` will pass only because `ThrowIfNullOrWhiteSpace` for `null` actually throws `ArgumentNullException` after `base(…)` has already tried string-interpolation with `null`, causing a `NullReferenceException` first — meaning the tests are passing for the wrong reason. Under a platform/runtime change or a slightly different null input, the wrong exception type surfaces.
- **Fix:** Move the guards to a static factory or move them before `base(…)` using the `[StackTraceHidden]`-factory pattern:

```csharp
public CursorFilterMismatchException(string filterName, string expectedValue, string actualValue)
    : base(MakeMessage(filterName, expectedValue, actualValue))
{
    FilterName = filterName;
    ExpectedValue = expectedValue;
    ActualValue = actualValue;
}

private static string MakeMessage(string filterName, string expectedValue, string actualValue)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(filterName);
    ArgumentException.ThrowIfNullOrWhiteSpace(expectedValue);
    ArgumentException.ThrowIfNullOrWhiteSpace(actualValue);
    return $"Cursor was issued for {filterName}={expectedValue} but request uses {filterName}={actualValue}.";
}
```

This kills all three surviving mutants at lines 19-21 as a side effect, since the guards now run before any value is read.

**SF-2. Docker smoke evidence not captured in plan file (`<paste …>` placeholder unfilled).**

- **Evidence:** `docs/superpowers/plans/2026-05-07-slice-6-phase-1-cleanup-bundle-plan.md:2111` — the placeholder `<paste the three curl -i outputs from Task 9 step 6 here>` is unfilled.
- **Impact:** Plan task 9 Step 6's acceptance criterion ("paste the three full `curl -i` outputs into the PR description's 'DoD §5 — Docker compose smoke evidence' block") is not met in the plan file. The PR description confirms the runs happened, but the verbatim output (the negative-path 400 `cursor-filter-mismatch` output in particular) is not captured. A future slice author can't use the plan as an audit trail for DoD #5.
- **Fix:** Fill the `<paste …>` placeholder in the plan file with the three curl outputs. The PR description already summarizes the happy path; the 400 negative path is the important missing piece.

**SF-3. SPA: `useApplicationsList` does not handle `cursor-filter-mismatch` 400 specially — checkbox toggle while cursor is active leaves the UI in an error state rather than auto-resetting.**

- **Evidence:** `web/src/features/catalog/api/applications.ts:54-68` — `fetchPage` throws `error` on any error response without inspecting the problem type. `web/src/lib/list/useCursorList.ts:44-48` — TanStack Query will retry the failing page automatically. The `CatalogListPage.tsx` shows a generic error card with a `Reset` button but no automatic reset when the specific error is `cursor-filter-mismatch`. Spec §5.3 specifies "cursor encodes the filter so paging is stable; mismatch returns 400 `cursor-filter-mismatch`" — the intent is that toggling the checkbox while on page 2 should auto-reset, not surface a generic error.
- **Impact:** A user who pages forward with `includeDecommissioned=false`, then toggles "Show decommissioned", will hold a cursor encoded with `ic=false` while the new request has `includeDecommissioned=true`. `useCursorList` fires the query with the stale cursor, receives 400, and TanStack Query marks the query as errored. The user sees "Failed to load applications" instead of page 1 with the new filter. The `Reset` button fixes it manually, but this is worse UX than automatic reset.
- **Fix:** In `useApplicationsList`, inspect the error `type` field. When `type === "https://kartova/problems/cursor-filter-mismatch"`, call `reset()` from the list state instead of propagating the error. Since `useCursorList` does not expose `reset` to the `fetchPage` callback, the cleaner fix is to include `includeDecommissioned` in the `queryKey` (it already is, via `applicationKeys.list(params)`), and rely on the existing synchronous stack-reset in `useCursorList` (lines 37-41) that fires when `keyStr` changes. Verify that the checkbox toggle causes `keyStr` to change and that `activeStack` reverts to `[undefined]` before the next `useQuery` call. If that path works end-to-end, the 400 case is unreachable in production and only arises from stale bookmarks. Add a comment and a component test confirming the reset fires.

---

## Nits

**N-1. `CatalogListPage.tsx`: `useEffect` for error logging is unnecessary — it re-fires on every render where `list.isError` or `list.error` identity changes.**

- **Evidence:** `web/src/features/catalog/pages/CatalogListPage.tsx:40-44`. A `useEffect` that only calls `console.error` is equivalent to inline logging in the JSX error branch, which already renders when `list.isError` is true. The `useEffect` fires one render later and creates a second log entry on re-renders.
- **Fix:** Remove the `useEffect`. Add `console.error(...)` inline in the `{list.isError ? ...}` branch, or just rely on the React Query `onError` callback at the query level.

**N-2. `QueryablePagingExtensions.cs:91-94`: the `expectedIncludeDecommissioned ?? false` pattern assigns the cursor the caller's filter rather than the cursor's own stored value.**

- **Evidence:** `src/Kartova.SharedKernel.Postgres/Pagination/QueryablePagingExtensions.cs:90-94`. When `expectedIncludeDecommissioned` is non-null, the mismatch guard (lines 66-73) ensures the cursor's `IncludeDecommissioned` equals `expectedIncludeDecommissioned`. So encoding `expectedIncludeDecommissioned ?? false` is always safe. However, the surviving mutant at line 94 (`?? false` → `true`) is logically equivalent here because when `expectedIncludeDecommissioned` is `null` (no filter check requested), the caller is the generic `ToCursorPagedAsync` overload without the filter parameter, and encoding `false` vs `true` has no semantic consumer (the next-page caller that uses the cursor will pass `expectedIncludeDecommissioned: null` again). The comment in the diff correctly identifies this but the mutant is not killed.
- **Fix:** The logic is correct; the surviving mutant is genuinely low-value. Document with a `// mutation-survivor: when expectedIncludeDecommissioned is null, ic=false in the cursor is consumed only by callers that also pass null, so ic value is never mismatch-checked.` comment at line 94 to make the rationale visible at the site. The existing comment at line 178 documents the `VisitParameter` survivor; line 94 deserves the same inline treatment. (This is also a nit to match house style — the pattern is already used at line 176-178 of this file.)

**N-3. `CatalogModuleRegisterForMigratorTests` and `OrganizationModuleRegisterForMigratorTests`: test method names diverge slightly from the spec's §6.2 naming.**

- **Evidence:** `src/Modules/Catalog/Kartova.Catalog.Infrastructure.Tests/CatalogModuleRegisterForMigratorTests.cs:21` — method is `RegisterForMigrator_resolves_CatalogDbContext_with_main_connection_string`; spec §6.2 says `RegisterForMigrator_resolves_CatalogDbContext_with_bypass_connection_string`. The implementation uses `KartovaConnectionStrings.Main` (correct per the actual code), but the spec example used the old `Bypass` name from an earlier draft. The tests are internally consistent with the production code; the spec name was an error.
- **Fix:** No code change needed — the test name in the spec was wrong; the implementation is right. Add a comment in the test: `// Note: spec §6.2 draft used "bypass_connection_string" — the production path is Main, not Bypass. See CatalogModule.cs:105.` or close with the ADR-0085 reference for future authors.

**N-4. `CursorFilterMismatchException`: `ExpectedValue` and `ActualValue` are asymmetrically named relative to the check in `QueryablePagingExtensions.cs`.**

- **Evidence:** `src/Kartova.SharedKernel.Postgres/Pagination/QueryablePagingExtensions.cs:70-72` — `expectedValue: decoded.IncludeDecommissioned ? "true" : "false"` (the cursor's stored value), `actualValue: requestFilter ? "true" : "false"` (the request's value). This means `ExpectedValue` = what the cursor was encoded with, `ActualValue` = what the request sent. The integration test at `ListApplicationsPaginationTests.cs:483-485` asserts `expectedValue="true"` and `actualValue="false"` which matches. However, from the client's perspective, the "expected" value is what they sent in the request, not what's in the cursor — the naming could confuse callers reading the problem body.
- **Fix:** Optionally rename to `CursorValue` / `RequestValue` to be unambiguous. Or leave the current naming and add a one-line comment to the exception doc: `/// <summary><c>ExpectedValue</c> is the filter value encoded in the cursor at issue time; <c>ActualValue</c> is the filter value from the current request.</summary>`. The integration test already documents the semantics via its inline comments.

**N-5. `web/src/features/catalog/pages/__tests__/CatalogListPage.test.tsx`: `afterEach` only restores mocks in the second `describe` block but not in the first.**

- **Evidence:** `CatalogListPage.test.tsx:221-224` — `afterEach(() => { vi.restoreAllMocks(); })` in `CatalogListPage — API hook receives correct query params`. The first new describe block `CatalogListPage — Show decommissioned checkbox` only has `beforeEach(() => { vi.restoreAllMocks(); })` (line 197). If a test in that block leaks a spy, the next test in the same block starts from a dirty state.
- **Fix:** Add `afterEach(() => { vi.restoreAllMocks(); })` to the `Show decommissioned checkbox` describe block as well, or lift it to a shared `afterEach` at the outer scope.

---

## Missing tests

**MT-1. `CursorFilterMismatchException` constructor guard mutations (lines 19-21 — three surviving mutants).**

The three `Statement mutation → ;` survivors at lines 19-21 of `CursorFilterMismatchException.cs` are caused by the guards firing after `base(…)` already consumed the arguments (see SF-1). Fixing SF-1 (moving guards into a `MakeMessage` static) will make these three mutants killable. After that fix, the three existing `Ctor_throws_ArgumentException_when_*` tests in `CursorFilterMismatchExceptionTests.cs` will kill them naturally — no new test needed, but SF-1 must be fixed first.

- **Project:** `Kartova.SharedKernel.Tests`
- **Class:** `CursorFilterMismatchExceptionTests`
- **Scenario:** All three null/empty/whitespace `Theory` tests already exist and are correct; they just can't kill the mutants until the constructor is restructured.

**MT-2. `QueryablePagingExtensions.cs:94` — `expectedIncludeDecommissioned ?? false` mutation survivor.**

- **Spec reference:** Spec §10 success criterion "Legacy cursor (JSON without the field) decodes as `false` and pages successfully." — there is a unit test for this at `QueryablePagingExtensionsTests.cs:474-490` (`ToCursorPagedAsync_with_null_expectedIncludeDecommissioned_encodes_next_cursor_with_ic_false`). The test exists and *should* kill the mutant at line 94, but the mutation report shows it as surviving. The likely cause: the test calls the `idSelector`-only overload (line 18-28) which chains to the full overload with `idExtractor = idSelector.Compile()`, and `expectedIncludeDecommissioned` defaults to `null`. The next cursor is encoded with `null ?? false = false`. If the mutation changes this to `true`, the cursor decodes with `IncludeDecommissioned=true`. The test asserts `decoded.IncludeDecommissioned.Should().BeFalse()` — this should catch the mutation. If the mutant still survived, the issue may be that Stryker's coverage map didn't see the test exercise line 94 specifically. Running the test suite with code coverage confirmation (coverlet) would clarify.
- **Actionable test:** `Kartova.SharedKernel.Tests` / `QueryablePagingExtensionsTests` / existing `ToCursorPagedAsync_with_null_expectedIncludeDecommissioned_encodes_next_cursor_with_ic_false` — this test is already correct. Accept as a near-equivalent mutant survivor and document the rationale inline at line 94 (per N-2 above).

**MT-3. `Organization.Rename` validation mutation (line 35 — `ValidateName(newName)` → `;`).**

- **Spec reference:** This survivor predates slice 6 (carry-forward noted in `Organization.cs:35` inline comment). The mutation report flags it as a should-fix.
- **Missing test:** `Kartova.Organization.Tests` / `OrganizationAggregateTests` / new `[Theory]` scenario: `Rename_with_invalid_name_throws`. Variants: null, empty, whitespace, too-long.
- **Expected assertion:** `act.Should().Throw<ArgumentException>()` — kills the `ValidateName(newName)` drop mutant and the suppress-throw variants.
- **Note:** This is a carry-forward, not introduced by slice 6. It is documented inline; the mutation score still passes. This item is for the next Organization slice.

**MT-4. `AdminOrganizationCommands.CreateAsync` DB persistence mutations (lines 20-21).**

- **Spec reference:** Mutation report items for `AdminOrganizationCommands.cs:20-21`. The inline comment correctly explains the gap: "AdminBypassTests only asserts the response DTO shape, not DB persistence." A test that reads back the persisted row would kill both `_db.Organizations.Add(org)` and `await _db.SaveChangesAsync(ct)` drop mutants.
- **Missing test:** `Kartova.Organization.IntegrationTests` — new test that calls `POST /api/v1/admin/organizations`, then issues a `GET /api/v1/organizations/me` (or uses the bypass DB connection to read back the row), and asserts the row exists in the database with the correct `Id`, `Name`, and `CreatedAt`.
- **Note:** Also a carry-forward. Score still passes. For the next Organization slice.

---

## What looks good

**1. Cursor backward-compatibility design is clean and correctly tested.**
`src/Kartova.SharedKernel/Pagination/CursorCodec.cs` — using `JsonIgnoreCondition.WhenWritingNull` to omit `ic` when `false`, and decoding absent `ic` as `false` (line 77), is the right approach. The unit tests in `CursorCodecTests.cs` (three new cases: `Encode_then_Decode_preserves_includeDecommissioned_true`, `Decode_legacy_cursor_without_ic_field_returns_includeDecommissioned_false`, `Encode_with_includeDecommissioned_false_omits_ic_field_and_round_trips`) directly pin the spec's Decision #7. The hand-crafted legacy JSON cursor in the third test is a particularly good technique — it's the only reliable way to verify the backward-compat path without depending on the encoder.

**2. `ListApplicationsHandler` filter placement is architecturally correct (ADR-0090 + spec §5.2 note).**
`src/Modules/Catalog/Kartova.Catalog.Infrastructure/ListApplicationsHandler.cs:30-51` — the Decommissioned `Where` predicate is applied to the `IQueryable<DomainApplication>` *before* `ToCursorPagedAsync`, not inside it. The inline comment explains the exact reason: "a row hidden by the filter must never appear as a cursor boundary." This is precisely the issue that would arise if the filter were applied inside the extension — the keyset WHERE clause would use a cursor boundary row that is no longer in the visible set, silently skipping rows on the next page. Placing the filter on the source queryable is the correct call.

**3. `useListUrlState` generic boolean filter extension is minimal and type-safe.**
`web/src/lib/list/useListUrlState.ts` — the `TBoolFilter extends string = never` default prevents callers without boolean filters from accidentally accessing `booleanFilters`. The `params.get(name)?.toLowerCase() === "true"` parse is intentionally strict (only `"true"` activates the filter; any other value is false), which matches the "no error UI for garbage in URL" convention from ADR-0095. The setter removes the param entirely when `false` rather than writing `=false`, keeping URLs clean. All four behaviors are pinned by unit tests in `use-list-url-state.test.tsx`.

**4. `RegisterForMigrator` test projects correctly distinguish the two DI registration paths (ADR-0085 + ADR-0090).**
`src/Modules/Catalog/Kartova.Catalog.Infrastructure.Tests/CatalogModuleRegisterForMigratorTests.cs` — the second test case ("does_not_require_active_TenantScope") is the critical one: it proves the migrator path uses plain `AddDbContext` rather than `AddModuleDbContext<T>` (which would require an `ITenantScope` at resolution time). Without this test, a future regression that accidentally routes the migrator through the tenant-scoped path would fail at runtime during Helm pre-upgrade jobs, not at test time. The test catches that exactly.

**5. CPM adoption with FluentAssertions pinned at 6.12.0 is handled correctly and future-proofed.**
`Directory.Packages.props` — all test package references now omit inline versions, the CPM file is the single source of truth, and the spec §13.9 follow-up registers the long-term FA replacement evaluation. The `NuGet.Config` with `<clear />` plus the public NuGet source ensures no stale or private feeds interfere with fresh CI runs. The Dockerfile COPY fix in commit `4219` correctly adds `Directory.Packages.props` and `NuGet.Config` to the build context — without this, `docker build` would fail to restore packages in the container layer.

---

## Appendix: Mutation survivors assessment

| File:line | Type | Assessment |
|---|---|---|
| `QueryablePagingExtensions.cs:94` | `?? false → true` | Near-equivalent; `expectedIncludeDecommissioned ?? false` is only read when the caller passes `null`, and callers that pass `null` also pass `null` on the next page, so the encoded value is never mismatch-checked. Document inline. |
| `QueryablePagingExtensions.cs:183` | `node == _from ? _to : base.VisitParameter(node)` | Equivalent; only one parameter is ever replaced per expression. Documented since slice 3; re-confirmed here. |
| `CursorFilterMismatchException.cs:19-21` | `Statement → ;` (×3) | **Actionable** — guards fire after `base(…)` uses the arguments. Fix via SF-1. |
| `Organization.cs:21` | Block removal on `private Organization()` | Equivalent for EF Core — documented inline. |
| `Organization.cs:35` | `ValidateName(newName)` → `;` | Actionable — carry-forward, documented inline. |
| `AdminOrganizationCommands.cs:20-21` | Statement → `;` (×2) | Actionable — carry-forward, documented inline. |
