# Deep PR Review — `feat/mstest-migration-phase-6`

**Date:** 2026-05-09
**Branch:** `feat/mstest-migration-phase-6` @ `b2dffba`
**Status:** OPEN (pre-merge slice-boundary gate)
**Phase scope (vs Phase 5 base `49a6aa9`):** 3 commits, 2 files, +18/-19 lines.

Read against:
- Spec: `docs/superpowers/specs/2026-05-08-xunit-to-mstest-migration-design.md` (especially §1 Goals item 6 on translation parity, §3 incremental-translation mechanic, §4 Translation rules — particularly §4.5 FA→MSTest assertion table; line 218 governs the `act.Should().NotThrow()` translation).
- Plan: `docs/superpowers/plans/2026-05-08-xunit-to-mstest-migration-plan.md` §Phase 6 (Tasks 6.1, 6.2, 6.3 — no Task 6.4 because no Stryker gate).
- ADR index: `docs/architecture/decisions/README.md` (ADR-0085 governs the migrator's DbContext-registration path that this test pins; ADR-0097 governs the MSTest framework choice).
- Definition of Done: `CLAUDE.md` §Definition of Done.

## Overview

Phase 6 translates the single test file in `src/Modules/Catalog/Kartova.Catalog.Infrastructure.Tests` (`CatalogModuleRegisterForMigratorTests.cs`, 3 methods / 3 cases) from xUnit + FluentAssertions to MSTest v4 + native asserts, drops xUnit + FA package references, swaps the `<Using Include="Xunit" />` global for the MSTest one. No production code is touched. **No Stryker gate** runs for this slice — `Kartova.Catalog.Infrastructure.Tests` is not a driver for any source assembly per the per-phase ownership table at line 50 of the baseline doc; the file pins DI-wiring contracts of `CatalogModule.RegisterForMigrator(...)` (the migrator-only DbContext path used by the Kartova.Migrator container per ADR-0085), not behavioral coverage of mutation targets.

## Blocking-class issues

None.

## Should-fix issues

None.

## Nits

1. **Test 2 translation drops the `var act = () => …` indirection that the xUnit version had.**
   - Evidence: `src/Modules/Catalog/Kartova.Catalog.Infrastructure.Tests/CatalogModuleRegisterForMigratorTests.cs:67-69`.
   - Cite: spec §4.5 line 218 explicitly licenses this — "`act.Should().NotThrow()` → call `act()` directly; if it throws, the test fails." MSTest treats unhandled exceptions in `[TestMethod]` bodies as failures, so the lambda indirection was load-bearing only for FA's fluent shape.
   - Impact: net positive — eliminates the xUnit version's redundant double-invocation (`act.Should().NotThrow(); var db = act();`) without changing the test's pass/fail predicate. /simplify and pr-test-analyzer both flagged this as a small efficiency improvement.
   - Fix: none required.

2. **Test 3 translation tightens the message-match semantics from FA's wildcard to MSTest's ordinal exact match.**
   - Evidence: `src/Modules/Catalog/Kartova.Catalog.Infrastructure.Tests/CatalogModuleRegisterForMigratorTests.cs:82-86`.
   - Cite: spec §4.5 line 217 mandates uniform `ThrowsExactly` adoption; the FA `WithMessage(...)` with no `*` globs translates to `Assert.AreEqual(expected, ex.Message)` (ordinal exact match) per the spec's tabulated mapping. The test's docstring at lines 75-76 explicitly says "CI bootstrap log scrapers depend on this format" — tightening the match is aligned with the test's stated intent.
   - Impact: net positive — pr-test-analyzer noted this is a "stronger than xUnit + FA original" assertion that better protects the format pin against drift.
   - Fix: none required. Spec §1 Goals item 6 ("Translate test count and behavior 1:1") is satisfied because the assertion still pins the same string with the same exception type; it just rejects loosely-matching mutations more aggressively.

## Missing tests

None. Phase 6 is a 1:1 translation slice. The Stryker gate is **not applicable** for this test project per the per-phase ownership table at baseline doc line 50 — `Kartova.Catalog.Infrastructure.Tests` is unit-tier coverage of `CatalogModule.RegisterForMigrator` DI wiring, and the corresponding mutation gates for `Kartova.Catalog.Infrastructure` source code are owned by Phase 4 (Catalog.Tests for unit-tier mutations) and Phase 9 (Catalog.IntegrationTests for handler-integration mutations). Phase 6 closes a different concern: that the migrator-only path keeps its tenant-scope-independence contract.

## What looks good

1. **`act.Should().NotThrow()` translation correctly applies spec §4.5 line 218.** `CatalogModuleRegisterForMigratorTests.cs:67-69` — the migrated test inlines `var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();` directly and follows with `Assert.IsNotNull(db)`. The xUnit version used `var act = () => …; act.Should().NotThrow(); var db = act();` (two invocations of the same lambda). MSTest's failure-on-unhandled-exception semantics make the explicit assertion redundant, and the spec tabulates this exact pattern.

2. **Exact-message translation correctly uses `Assert.AreEqual` (not `StringAssert.Contains`) for non-glob `WithMessage`.** `CatalogModuleRegisterForMigratorTests.cs:82-86` — the FA `WithMessage("Connection string 'Kartova' is required. Set it via ConnectionStrings__Kartova env var.")` had no `*` globs, so the spec §4.5 row maps it to `Assert.AreEqual(expected, ex.Message)`. The translation chose `AreEqual` correctly. The associated comment at lines 75-76 ("CI bootstrap log scrapers depend on this format") makes the format-pin intent legible — exactly the kind of WHY-comment that justifies an ordinal exact match.

3. **csproj cleanup is exactly the spec'd shape.** `Kartova.Catalog.Infrastructure.Tests.csproj:11-22` — `xunit`, `xunit.runner.visualstudio`, `FluentAssertions` removed; `MSTest.TestFramework` + `MSTest.TestAdapter` + `MSTest.Analyzers` added; `<Using Include="Xunit" />` swapped for `Microsoft.VisualStudio.TestTools.UnitTesting`. Per-file `using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;` alias was added in Task 6.2 (to disambiguate against the still-active xUnit global) and correctly removed in Task 6.3 once xUnit was off the compilation — final file at `CatalogModuleRegisterForMigratorTests.cs:1-5` has clean imports and no dead alias.

4. **All 4 surviving comments earn their place under CLAUDE.md's WHY-not-WHAT discipline.** Class-level summary at lines 9-15 names the contract being pinned and cites ADR-0085 + the slice-3 §13.10 mutation-coverage motivation. Inline comments at lines 22-24 (BYPASSRLS context for the Main connection), lines 47-51 (migrator-vs-tenant-scope distinction — names the specific regression this test guards against), and lines 75-76 (CI log scraper format-pin rationale) all explain WHY rather than WHAT. The translation didn't introduce any FA-archaeology.

5. **Plan alignment is exact.** Three commits map 1:1 to plan Tasks 6.1 (csproj add MSTest packages + parallel global), 6.2 (file translation), 6.3 (drop xUnit + FA + xUnit global + per-file alias). No Task 6.4 because the plan correctly does not specify a Stryker gate for this slice.

## DoD cross-check (`CLAUDE.md` §Definition of Done)

| # | Bullet | Evidence |
|---|---|---|
| 1 | Build green with `TreatWarningsAsErrors` | `dotnet build Kartova.slnx -warnaserror` → `Build succeeded. 0 Warning(s), 0 Error(s)` at the Task 6.3 commit. |
| 2 | Per-task subagent reviews | **Skipped per user's explicit "controller-direct" choice.** Slice-boundary skills (#3, #6, #8, #9) compensate. |
| 3 | `superpowers:requesting-code-review` at slice boundary | Run completed: "Ready to merge as Phase 6. Zero findings." 0 critical / 0 important / 0 minor. |
| 4 | Full test suite green | `dotnet test Kartova.slnx --no-build` → 398/398 passed across 10 test assemblies at the Task 6.3 commit. |
| 5 | `docker compose up` + real HTTP | **N/A.** Phase 6 changes no production code, no middleware, no DI, no Dockerfile. |
| 6 | `/simplify` | 3 parallel agents — reuse 0 actionable, quality 0 worth flagging, efficiency 0 regressions; reviewer noted Phase 6 *eliminated* a small redundancy from the xUnit version's double-invocation. |
| 7 | Mutation feedback loop | **N/A.** `Kartova.Catalog.Infrastructure.Tests` is not a driver for any source assembly's mutation gate per the per-phase ownership table at baseline doc line 50; the test project pins DI wiring contracts, not behavioral mutation coverage. |
| 8 | `/pr-review-toolkit:review-pr` | 3 reviewers — code-reviewer 0/0/0, pr-test-analyzer 0/0/0 (with note that test 3 marginally tightened via `ThrowsExactly` + ordinal exact match, accepted under spec §1 Goals item 6 as a tightening), comment-analyzer 0/0/0. |
| 9 | `/deep-review` | This document. |

DoD bullets 1–9 are satisfied (with #2 honestly authorized by the user as controller-direct, #5 honestly N/A, #7 honestly N/A per the per-phase ownership table).

## Verdict

Phase 6 is ready to merge. This is the smallest and cleanest slice in the migration so far — 3 tests, 3 commits, 0 findings across 4 slice-boundary review passes. Two of the three tests are marginally tightened by the translation (`NotThrow` indirection collapsed; non-glob `WithMessage` becomes ordinal `AreEqual`), and both tightenings are aligned with the tests' stated intent rather than introducing new behavioral pinning. The only real callout is one not in this slice: `OrganizationModuleRegisterForMigratorTests.cs` is the next sibling slice (Phase 7) and currently shares the same per-test setup-duplication pattern that this slice preserved-as-is — Phase 7 will be a near-identical translation against an even smaller xUnit baseline.
