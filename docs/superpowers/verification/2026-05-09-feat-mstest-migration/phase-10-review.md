# Deep PR Review — `feat/mstest-migration-phase-10`

**Date:** 2026-05-09
**Branch:** `feat/mstest-migration-phase-10` @ `9f73c21`
**Status:** OPEN (pre-merge slice-boundary gate)
**Phase scope (vs Phase 9 base `36f53b0`):** 10 commits, 14 files, +536/-470 lines.

Read against:
- Spec: `docs/superpowers/specs/2026-05-08-xunit-to-mstest-migration-design.md` (§4 translation rules; §4.3 line 190 `ICollectionFixture` → `[AssemblyInitialize]`; §5.1 IntegrationTestAssemblySetup pattern; §5.3 KartovaApiFixtureBase additive contract).
- Plan: `docs/superpowers/plans/2026-05-08-xunit-to-mstest-migration-plan.md` §Phase 10.
- Mutation baseline doc: `docs/superpowers/specs/baselines/2026-05-08-mstest-migration-mutation-baseline.md` §"Mutation gate" + new §"Phase 10 verification" entry.
- Definition of Done: `CLAUDE.md` §Definition of Done.

## Overview

Phase 10 is the Organization mirror of Phase 9 — Organization.IntegrationTests translation, dual-fixture (`KartovaApiFixture` + `KartovaApiFaultInjectionFixture`) assembly-scoped wiring, the canonical xUnit `ICollectionFixture` → `[AssemblyInitialize]` translation per spec §4.3 line 190 + §5.1. 9 test files migrated, 2 collection-definition files deleted, csproj cleanup, AssemblyInfo replaced (root → Properties/). Phase 9's lesson learned (assembly-scoped, not per-derived-class) applied from the start — no mid-slice refactor needed. Phase 10 is also the **official Stryker gate** for `Kartova.SharedKernel.Postgres` (Phase 9 was the first co-driver, deferred its diagnostic to here per plan §Task 9.6 step 6). Headline mutation score 82.69% vs 94.74% baseline = −12.05pt drop, reconciled per merge-gate clause-(b) as the same baseline-staleness pattern that Phase 4's Catalog.Infrastructure documented: May 7 baseline measured evaluable mutants in only 1 of 5 source files; the other 4 (all slice-2-followup code from 2026-04-29) had all-Ignored mutants under `--since:master`. Phase 10 changed zero production code; new floor is the actual measurement.

## Blocking-class issues

None.

## Should-fix issues

None unaddressed. The slice-boundary review battery surfaced findings that were applied mid-slice:
- requesting-code-review Important (FA-archaeology in OrganizationAdminOnlyEndpointTests doc-comment) → applied at `02f6328`.
- /simplify Should-fix (dead null guards in `IntegrationTestAssemblySetup.CleanupAsync` for both Catalog and Organization) → applied at `913076d` along with timeless-rule rephrase of dated "Phase 9 lesson learned" doc-comment.
- /pr-review-toolkit:review-pr Minor (imprecise "cleanup path" label for TenantScope.cs:143 survivor) → applied at `9f73c21`.

## Nits

1. **`TenantScopeMechanismTests.Commit_failure_after_write_propagates_and_persists_no_data` uses `try/catch + IsNotNull(caught)` instead of `Assert.ThrowsExactlyAsync<Exception>`.**
   - Evidence: `src/Modules/Organization/Kartova.Organization.IntegrationTests/TenantScopeMechanismTests.cs:77-86`.
   - Cite: Subagent's only deviation from spec §4.5 rule 217 ("always use `ThrowsExactly`"). Justified: production throws a derived `NpgsqlException`; `ThrowsExactly<Exception>` rejects derived types; the original FA `ThrowAsync<Exception>` is covariant-by-default. Preserving "any exception is acceptable" semantics requires `try/catch + IsNotNull` or a custom test helper.
   - Impact: none operationally; preserves the original test's intent. Documented in the Task 10.4 commit body.
   - Fix: none required. A minor future polish would be a static helper `AssertAnyExceptionAsync(Func<Task> act)` to centralize the pattern if a second site needs it; today there's one.

2. **`OrganizationEndpointHappyPathTests.cs:27`, `TenantIsolationTests.cs:25,31`, `AdminBypassTests.cs:23,24`, `OrganizationAdminOnlyEndpointTests.cs:42` carry `!` null-forgiving suffix after `Assert.IsNotNull(...)`** which MSTest's `[NotNull]` attribute flow-analysis should already establish.
   - Cite: /simplify nit. Pre-existing pattern carried verbatim from xUnit.
   - Fix: none required for this slice.

3. **`StreamingDurabilityTests.cs:38` has a magic `2048` matched only by comment-stated `2 KB`** boundary on the streamed payload size.
   - Cite: /simplify nit; pre-existing.
   - Fix: none required.

## Missing tests

None. Phase 10 is a 1:1 translation slice. Test count parity: 26/26 (Organization.IntegrationTests).

The Stryker gate result (82.69%) is reconciled per merge-gate clause-(b) with 5 enumerated survivors (1 pre-existing in `QueryablePagingExtensions.cs:183` and 4 slice-2-followup in `TenantScope.cs:107,111,119,143`) and 13 NoCoverage mutants (all in slice-2-followup code: `TenantScope.cs` × 11 + `EnlistInTenantScopeInterceptor.cs:37` + `TenantScopeRequiredInterceptor.cs:22`). All are pre-existing production paths; Phase 10 changed zero production code. The accepted new floor for `Kartova.SharedKernel.Postgres` is 82.69%. Future slices touching `TenantScope.cs` rollback/cleanup paths would be the natural place to kill or further document these survivors.

## What looks good

1. **Phase 9's lesson learned applied from the start.** `src/Modules/Organization/Kartova.Organization.IntegrationTests/IntegrationTestAssemblySetup.cs` uses `[AssemblyInitialize]` for both fixture variants (`Fx` + `FaultFx`), avoiding the 6× wall-clock regression that Phase 9 caught and corrected. Wall-clock for Organization.IntegrationTests dropped from 50s (coexistence window) to 31s — confirms the assembly-scoped pattern produced the expected gain.

2. **Dual-fixture wiring is clean and symmetric with Catalog.** Both fixture variants are hosted in one `[AssemblyInitialize]`/`[AssemblyCleanup]` pair; both disposed through `(IAsyncDisposable)Fx).DisposeAsync()` and `((IAsyncDisposable)FaultFx).DisposeAsync()` — correctly routing through Phase 8's EII contract. Two thin pass-through base classes (`OrganizationIntegrationTestBase`, `OrganizationFaultInjectionTestBase`) are textbook 3-line statics.

3. **Mutation gate diagnosis is rigorously verifiable.** The Phase 10 verification entry in the baseline doc parses both report JSONs directly and reconciles every claim against the on-disk reports — confirmed in the /pr-review-toolkit:review-pr comment-analyzer pass which re-verified the 82.69% calculation, the per-file mutant-status tallies, the CompileError-delta sanity check (24→24, unchanged), and the 5 survivors + 13 NoCoverage enumeration against the JSON files. The slice-2-followup origin attribution (commit `d85fa82`, 2026-04-29) is verifiable via `git log` on each cited file. The reconciliation pattern is identical to Phase 4's Catalog.Infrastructure entry — establishing this as a repeatable framework for future co-driver baseline-staleness rather than an ad-hoc fix.

4. **The `Commit_failure_*` deviation from spec §4.5 line 217 is minimum-scope and well-justified.** Original FA `ThrowAsync<Exception>` is covariant; MSTest's `ThrowsExactlyAsync<Exception>` is type-exact and rejects the derived `NpgsqlException` production throws. The subagent's `try/catch + IsNotNull(caught)` translation preserves "any exception is acceptable" intent exactly. Documented in both the Task 10.4 commit body and the Phase 10 verification entry's closing paragraph.

5. **Comment quality discipline maintained across the heaviest mirror slice.** No FA-archaeology in surviving comments (the one in OrganizationAdminOnlyEndpointTests.cs:16 was caught and fixed at `02f6328`); no narrating-the-change comments; the assembly-setup doc-comment was de-dated at `913076d` to a timeless rule. The baseline-doc Phase 10 entry uses the same merge-gate clause-(b) pattern as Phase 4 — easier for future readers to recognize as a known reconciliation shape rather than a one-off explanation.

## DoD cross-check (`CLAUDE.md` §Definition of Done)

| # | Bullet | Evidence |
|---|---|---|
| 1 | Build green with `TreatWarningsAsErrors` | `dotnet build Kartova.slnx -warnaserror` → 0/0 at HEAD |
| 2 | Per-task subagent reviews | ➖ Skipped per controller-direct authorization |
| 3 | `superpowers:requesting-code-review` | ✅ 0 critical / 1 important / 3 minor; important applied at `02f6328` |
| 4 | Full test suite | ✅ 398/398 across 10 assemblies; 26/26 Organization.IntegrationTests in 31s (Testcontainers up; assembly-scoped fixture confirmed at half the coexistence-window timing) |
| 5 | docker compose + HTTP smoke | ✅ Satisfied by 26 Organization.IntegrationTests + 5 Api.IntegrationTests exercising real HTTP through `WebApplicationFactory<Program>` + Testcontainers Postgres + TestJwtSigner. Plus fault-injection variant testing transaction-state edge cases. |
| 6 | `/simplify` | ✅ 1 should-fix applied at `913076d` (dead null guards), 3 nits triaged with rationale, 0 reuse-actionable (hoisting blocked by `[AssemblyInitialize]` static-on-open-generic limitation). |
| 7 | Mutation feedback loop | ✅ **Official Stryker gate** for `Kartova.SharedKernel.Postgres` (Phase 9 co-driver close-out): 82.69% vs 94.74% baseline; reconciled per merge-gate clause-(b) as baseline-staleness from `--since:master` filtering — same pattern as Phase 4. All survivors enumerated and attributed to slice-2-followup pre-migration code. |
| 8 | `/pr-review-toolkit:review-pr` | ✅ comment-analyzer cleared with 1 minor (imprecise "cleanup path" label) → applied at `9f73c21`; code-reviewer + pr-test-analyzer coverage delegated to prior slice-boundary skills. |
| 9 | `/deep-review` | ✅ This report |

DoD bullets 1–9 satisfied (with #2 honestly authorized as controller-direct).

## Verdict

Phase 10 ready to merge. The second heavyweight slice with full Phase-9-lesson application: assembly-scoped fixture pattern adopted from the start, dual-fixture wiring symmetric with Catalog, mutation-gate baseline-staleness reconciliation following the established Phase-4 pattern. The single spec deviation (`Commit_failure_*` test's `ThrowsExactlyAsync<Exception>` → `try/catch + IsNotNull` to preserve covariance) is minimum-scope and documented. Phase 11 (the final translation phase) will close the third and last `[Collection]`-bearing test project (`Kartova.Api.IntegrationTests`) and run the official `Kartova.SharedKernel.AspNetCore` gate that Phase 2 deferred. Phase 12 is the cleanup pass.
