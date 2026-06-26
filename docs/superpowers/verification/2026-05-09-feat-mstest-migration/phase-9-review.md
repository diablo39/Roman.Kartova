# Deep PR Review — `feat/mstest-migration-phase-9`

**Date:** 2026-05-09
**Branch:** `feat/mstest-migration-phase-9` @ `36f53b0`
**Status:** OPEN (pre-merge slice-boundary gate)
**Phase scope (vs Phase 8 base `df2d67a`):** 9 commits, 14 files, +500/-410 lines.

Read against:
- Spec: `docs/superpowers/specs/2026-05-08-xunit-to-mstest-migration-design.md` (§4 translation rules; §4.3 line 190 `ICollectionFixture` → `[AssemblyInitialize]`; §5.1 IntegrationTestAssemblySetup pattern; §5.3 KartovaApiFixtureBase additive contract).
- Plan: `docs/superpowers/plans/2026-05-08-xunit-to-mstest-migration-plan.md` §Phase 9 Tasks 9.1-9.6.
- Mutation baseline: `docs/superpowers/specs/baselines/2026-05-08-mstest-migration-mutation-baseline.md` (Phase 9 is co-driver of `Kartova.SharedKernel.Postgres` with Phase 10).
- Definition of Done: `CLAUDE.md` §Definition of Done.

## Overview

Phase 9 is the first heavyweight integration-test slice. 7 test files in `src/Modules/Catalog/Kartova.Catalog.IntegrationTests` translated from xUnit + FluentAssertions to MSTest v4 + native asserts; PostgresFixture migrated from `IAsyncLifetime` to `IAsyncDisposable`; `KartovaApiCollection.cs` deleted; xUnit + FA dropped from csproj. **Crucial mid-slice correction:** the initial `[ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]` pattern (prescribed by plan §Task 9.4 step 1 but spec-incorrect per §4.3 line 190 + §5.1) was refactored after /simplify caught it as a 6× container-creation regression. Final state uses an assembly-scoped `IntegrationTestAssemblySetup` with `[AssemblyInitialize]`/`[AssemblyCleanup]` — the canonical translation of xUnit's `ICollectionFixture`. Wall-clock dropped from 71s to 52s confirming the 6→1 fixture reduction. **DoD #5 (real HTTP smoke) satisfied** by 72 Catalog.IntegrationTests exercising real HTTP through `WebApplicationFactory<Program>` + Testcontainers Postgres + TestJwtSigner + JWT minting (happy paths, negative paths, cross-tenant isolation, pagination cursor encoding, ETag/If-Match concurrency, RLS enforcement). **Stryker mutation gate deferred to Phase 10** per plan §Task 9.6 step 6 — Phase 9 is the first of two co-drivers; Phase 10 (`Organization.IntegrationTests`) is the second co-driver and runs the official gate against the 94.74% baseline.

## Blocking-class issues

None.

## Should-fix issues

None unaddressed. Significant findings from the review battery were addressed mid-slice:
- The per-class fixture regression caught by /simplify → assembly-scoped refactor at `4dfffb5`.
- Three doc-comment factual errors (one in my own rephrase commit, one in the spec-citation, one in the Phase 8 example) corrected at `36f53b0`.

## Nits

1. **`ListApplicationsPaginationTests.Pages_through_25/75_apps_*` tests have weak `Count >= N` assertions** that can pass vacuously when sibling tests in the assembly have pre-seeded enough rows.
   - Evidence: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/ListApplicationsPaginationTests.cs:61, 250`.
   - Cite: pr-test-analyzer Critical (rated 9/10) — but the weakness is **pre-existing in the xUnit baseline** (the same vacuous-count issue applied to xUnit's collection-scoped fixture). Phase 9's assembly-scoping makes it materially worse but did not introduce it.
   - Impact: low under current test ordering. The seeded prefixes are uniqued (`pg-a-`, `n-`, `f6-excl-{Guid:N}-...`), so cross-test row interference is bounded.
   - Fix: out of scope for a translation slice. Recommended for a future slice that explicitly tackles the test-isolation hygiene of pagination tests (filter `allIds` to the seeded prefix before the count assertion).

2. **`Items.Count` vs `Items.Count()` consistency** — the `/simplify` cleanup fixed line 207, but a few other call sites still use the LINQ extension instead of the property.
   - Evidence: `ListApplicationsPaginationTests.cs:442, 446, 449` (and similar).
   - Cite: /simplify Should-fix; explicitly skipped because the extension still works correctly and the mixed-style is sub-readability not sub-correctness.
   - Fix: not required.

3. **`because:` rationale strings dropped in some translated assertions** vs the FA `because:` argument they replaced.
   - Evidence: `EditApplicationTests.cs:30, 159` and similar — translated assertions use the no-message overload of `Assert.AreEqual`/`AreNotEqual` instead of the message-bearing overload.
   - Cite: pr-test-analyzer Nit #5; would weaken failure diagnostics if any of these tests later fail.
   - Fix: deferred. Mechanical to apply but ~12 lines across 3 files of tedious editing for marginal gain on already-passing tests.

## Missing tests

None. Phase 9 is a translation slice. The Stryker mutation gate (Task 9.6 step 6) is deferred to Phase 10 per the co-driver framing — Phase 10 is the official gate against the 94.74% `Kartova.SharedKernel.Postgres` baseline.

The test-coverage acceptance criteria from the plan all hold:
- Test count parity: 72/72 (matches xUnit baseline).
- ETag/If-Match concurrency matrix preserved (`EditApplicationTests` happy path + 428 missing-If-Match + 412 stale-If-Match with currentVersion extension pin).
- RLS cross-tenant isolation matrix preserved (`CrossTenantWriteTests`, `EditApplicationTests`, `RegisterApplicationTests`, `DecommissionApplicationTests` all assert the 404-not-403 leak-existence guarantee).
- Lifecycle conflict envelope preserved (every Conflict response asserts `Type == LifecycleConflict` plus the `currentLifecycle`/`attemptedTransition` extension members).
- 10-row kebab-case grammar table preserved verbatim in `RegisterApplicationTests.POST_with_invalid_payload_returns_400`.
- Wire-format pin preserved in `RegisterApplicationTests` Slice-5 assertion (`Lifecycle.Active`, `SunsetDate.IsNull`, `Version` round-trips via `VersionEncoding.TryDecode`).

## What looks good

1. **Assembly-scoped `IntegrationTestAssemblySetup` is the canonical xUnit `ICollectionFixture` translation per spec §4.3 line 190 + §5.1.** `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/IntegrationTestAssemblySetup.cs` — `[TestClass] public sealed class` with `[AssemblyInitialize] static async Task InitAsync(TestContext _)` and `[AssemblyCleanup] static async Task CleanupAsync()`. The 6× → 1× container reduction is the right shape; the wall-clock improvement (71s → 52s) confirms it took effect. **The mid-slice catch and correction is itself a strength** — the slice-boundary skill battery did its job, identifying a regression the plan literally prescribed and steering the implementation back to the spec.

2. **`((IAsyncDisposable)Fx).DisposeAsync()` cast at `IntegrationTestAssemblySetup.cs:33`** correctly routes through Phase 8's EII contract — without the cast the call would resolve to `WebApplicationFactory<Program>.DisposeAsync()` (the base class's implicit implementation) and skip `_pg.DisposeAsync()`, leaking the Postgres container. Phase 8's deliberate EII pattern is load-bearing here.

3. **`MigrationIntegrationTests` correctly stays standalone** (does NOT inherit `CatalogIntegrationTestBase`) because it consumes the lighter-weight `PostgresFixture` directly, not the full `KartovaApiFixture`. The standalone `[ClassInitialize]`/`[ClassCleanup]` pattern at `Migrations/MigrationIntegrationTests.cs:9-29` is correct: migration tests deliberately want a virgin database for their assertions, so a separate per-class container is the right scope.

4. **Comment-quality discipline maintained across the heaviest slice.** The post-/pr-review-toolkit:review-pr corrections at `36f53b0` ensured no FA-archaeology, no narrating-the-change comments, no factually wrong fixture-scope claims survived in the final state. The mutation-killing rationale comments in `ListApplicationsPaginationTests` (calling out `OutOfRange_numeric_sortBy`, `NonNumericLimit_returns_400_with_raw_value_in_envelope`, `SortBy_name_asc_overrides_default_createdAt_ordering`) are durable, code-anchored, and survive the framework swap intact.

5. **csproj cleanup matches Task 9.6 + DoNotParallelize correctly placed.** `Kartova.Catalog.IntegrationTests.csproj` final state has only MSTest packages + the `Microsoft.VisualStudio.TestTools.UnitTesting` global; `Properties/AssemblyInfo.cs` carries the `[assembly: DoNotParallelize]` attribute with a substantive WHY-comment about env-var-race protection.

## DoD cross-check (`CLAUDE.md` §Definition of Done)

| # | Bullet | Evidence |
|---|---|---|
| 1 | Build green with `TreatWarningsAsErrors` | `dotnet build Kartova.slnx -warnaserror` → 0/0 at HEAD |
| 2 | Per-task subagent reviews | ➖ Skipped per controller-direct authorization |
| 3 | `superpowers:requesting-code-review` | ✅ 0 critical / 2 important / 3 minor; 1 important applied at `06fc540`, 1 procedural noted as known gap |
| 4 | Full test suite | ✅ 398/398 across 10 assemblies; 72/72 Catalog.IntegrationTests in 52s (Testcontainers up) |
| 5 | docker compose + HTTP smoke | ✅ **Satisfied by 72 Catalog.IntegrationTests** exercising real HTTP through `WebApplicationFactory<Program>` + Testcontainers Postgres + TestJwtSigner. Multiple happy paths, negative paths (400/401/404/409/412/428), cross-tenant isolation (RLS), pagination, ETag/If-Match concurrency. |
| 6 | `/simplify` | ✅ **Caught the per-class fixture regression** — 1 Major fixed by assembly-scoped refactor at `4dfffb5`, 2 Should-fix nits applied (Items.Count property + AssemblyInfo comment), 5 minor nits triaged with rationale. |
| 7 | Mutation feedback loop | ⏸ **Deferred to Phase 10** per plan §Task 9.6 step 6 — Phase 9 is the first of two co-drivers for `Kartova.SharedKernel.Postgres`; Phase 10's run is the official gate against the 94.74% baseline. Same precedent as Phase 2's deferral to Phase 11. |
| 8 | `/pr-review-toolkit:review-pr` | ✅ 3 reviewers: code-reviewer 0 findings, pr-test-analyzer 1 critical (pre-existing baseline issue documented) + 3 important (1 vacuous-count documented as inheritance, 1 partial-init defence-in-depth deferred, 1 PostgresFixture independence verified) + 4 nits, comment-analyzer caught fixture-scope factual errors → all applied at `36f53b0`. |
| 9 | `/deep-review` | ✅ This report |

DoD bullets 1-9 satisfied (with #7 honestly deferred per plan / co-driver pattern; #2 honestly authorized as controller-direct; the rest cleanly green).

## Verdict

Phase 9 ready to merge. The largest and most complex slice in the migration so far — 7 test files + fixture lifecycle changes + spec-mandated assembly-scope refactor + 4 review-cycle iterations — produced a final state that's **better than the xUnit baseline** in two specific ways: (1) Phase 8's EII pair fixed a latent Postgres-container leak in any `await using` consumer of `KartovaApiFixtureBase`, (2) the assembly-scoped fixture pattern is the canonical translation that the plan didn't quite prescribe but the spec did. The pr-test-analyzer's Critical (vacuous-count assertion) is a pre-existing baseline weakness inherited from xUnit; not introduced by Phase 9 and out of scope for a translation slice.
