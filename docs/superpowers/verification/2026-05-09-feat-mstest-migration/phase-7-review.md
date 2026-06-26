# Deep PR Review — `feat/mstest-migration-phase-7`

**Date:** 2026-05-09
**Branch:** `feat/mstest-migration-phase-7` @ `2964ce4`
**Status:** OPEN (pre-merge slice-boundary gate)
**Phase scope (vs Phase 6 base `b2dffba`):** 3 commits, 2 files, +17/-19 lines.

Read against:
- Spec: `docs/superpowers/specs/2026-05-08-xunit-to-mstest-migration-design.md` §4 Translation rules (line 217 `ThrowsExactly` mandate; line 218 `NotThrow` → bare invocation).
- Plan: `docs/superpowers/plans/2026-05-08-xunit-to-mstest-migration-plan.md` §Phase 7 (Tasks 7.1, 7.2, 7.3 — no Task 7.4 because no Stryker gate).
- ADR index: `docs/architecture/decisions/README.md` (ADR-0085 governs the migrator's DbContext-registration path; ADR-0097 governs the MSTest framework choice).
- Definition of Done: `CLAUDE.md` §Definition of Done.

## Overview

Phase 7 translates `OrganizationModuleRegisterForMigratorTests.cs` (3 methods / 3 cases) — the Organization mirror of Phase 6's `CatalogModuleRegisterForMigratorTests`. Identical translation patterns: `Should().NotThrow()` collapsed to bare invocation, `Should().Throw<T>().WithMessage("exact-string")` (no globs) translated to `ThrowsExactly` + ordinal `AreEqual` on `ex.Message`. csproj swap matches Phase 6 verbatim. No production code touched. **No Stryker gate** for this slice — `Kartova.Organization.Infrastructure.Tests` is not a driver per the per-phase ownership table at baseline doc line 50.

## Blocking-class issues

None.

## Should-fix issues

None.

## Nits

None worth flagging — Phase 7 carries the same two informational notes as Phase 6 (collapsed `NotThrow` indirection, tightened message-match), both noted by /pr-review-toolkit:review-pr's pr-test-analyzer as net positives accepted under spec §1 Goals item 6.

## Missing tests

None. Phase 7 is a 1:1 translation slice. No Stryker gate applies — `Kartova.Organization.Infrastructure.Tests` pins DI wiring contracts, not behavioral mutation coverage; the corresponding Organization source mutations are owned by Phase 5 (already passed at HEAD `49a6aa9`).

## What looks good

1. **Translation is byte-equivalent to Phase 6's pattern.** `OrganizationModuleRegisterForMigratorTests.cs` and `CatalogModuleRegisterForMigratorTests.cs` now share an identical shape — `[TestClass]`, `[TestMethod]`, `Assert.IsNotNull` + `Assert.AreEqual` for happy paths, `Assert.ThrowsExactly<T>` + `Assert.AreEqual(expected, ex.Message)` for the missing-connection-string negative path. Cross-module translation discipline is intact.
2. **csproj cleanup matches Phase 6 byte-for-byte (modulo project references).** `Kartova.Organization.Infrastructure.Tests.csproj` final state has the same `MSTest.TestFramework` + `MSTest.TestAdapter` + `MSTest.Analyzers` package list, the same `<Using Include="Microsoft.VisualStudio.TestTools.UnitTesting" />` global, and the same dropped `xunit` + `xunit.runner.visualstudio` + `FluentAssertions` references as Phase 6.
3. **All 4 surviving comment blocks are accurate WHY-comments** verified by comment-analyzer against the production source: class summary at lines 9-15 cites Slice-3 §13.10 + the Catalog mirror; lines 22-24 cite `OrganizationModule.cs:81` and `CatalogModule.cs:105` (verified line numbers); lines 47-51 explain the migrator-vs-tenant-scope distinction; lines 75-76 explain why the message format is pinned (CI log-scraper dependency).
4. **Plan alignment exact.** Three commits map 1:1 to plan Tasks 7.1 (csproj add MSTest packages), 7.2 (file translation), 7.3 (drop xUnit + FA + alias).
5. **Three slice-boundary skill passes all returned 0/0/0.** Same result as Phase 6, as expected for an identical-shape mirror.

## DoD cross-check (`CLAUDE.md` §Definition of Done)

| # | Bullet | Evidence |
|---|---|---|
| 1 | Build green with `TreatWarningsAsErrors` | `dotnet build Kartova.slnx -warnaserror` → 0/0 at Task 7.3 commit |
| 2 | Per-task subagent reviews | ➖ Skipped per controller-direct authorization |
| 3 | `superpowers:requesting-code-review` | ✅ 0/0/0 — "matches Phase 6's clean review" |
| 4 | Full test suite | ✅ 398/398 across 10 assemblies |
| 5 | docker compose + HTTP smoke | ➖ N/A (no production code) |
| 6 | `/simplify` | ✅ 0/0/0 across reuse, quality, efficiency |
| 7 | Mutation feedback loop | ➖ N/A (not a driver per ownership table) |
| 8 | `/pr-review-toolkit:review-pr` | ✅ 0/0/0 across all three reviewers |
| 9 | `/deep-review` | ✅ This report |

## Verdict

Phase 7 ready to merge. Identical mirror of Phase 6 with identical clean review outcome.
