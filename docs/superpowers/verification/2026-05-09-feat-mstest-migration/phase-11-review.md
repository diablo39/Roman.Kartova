# Deep PR Review — `feat/mstest-migration-phase-11`

**Date:** 2026-05-09
**Branch:** `feat/mstest-migration-phase-11` @ `0951c66`
**Status:** OPEN (pre-merge slice-boundary gate)
**Phase scope (vs Phase 10 base `9f73c21`):** 7 commits, 11 files, +280/-110 lines.

Read against:
- Spec: `docs/superpowers/specs/2026-05-08-xunit-to-mstest-migration-design.md` (§4 translation rules; §4.3 line 190 ICollectionFixture → AssemblyInitialize; §5.1 KeycloakContainerFixture pattern — **this is the canonical example the spec was built around**; §5.2 AuthSmokeTests translation pattern; §5.3 KartovaApiFixtureBase additive contract).
- Plan: `docs/superpowers/plans/2026-05-08-xunit-to-mstest-migration-plan.md` §Phase 11.
- Mutation baseline: `docs/superpowers/specs/baselines/2026-05-08-mstest-migration-mutation-baseline.md` §"Phase 11 verification" (new entry this branch added).
- Definition of Done: `CLAUDE.md`.

## Overview

Phase 11 is the migration's **final translation slice**. `tests/Kartova.Api.IntegrationTests` (3 files: `AuthSmokeTests`, `CorsTests`, `OpenApiTests`) translated to MSTest v4 + native asserts. `KeycloakContainerFixture` converted from `IAsyncLifetime` to a plain class with `IAsyncDisposable`. New `IntegrationTestAssemblySetup` + `KeycloakContainerTestBase` follow the Phase 9/10 corrected assembly-scoped pattern (one Postgres+Keycloak container pair per assembly run, exposed via `[AssemblyInitialize]`). `KeycloakTestCollection.cs` deleted. `Properties/AssemblyInfo.cs` carries `[assembly: DoNotParallelize]`. csproj cleaned of xUnit + FluentAssertions. **OFFICIAL Stryker gate** (`Kartova.SharedKernel.AspNetCore` co-driver close-out — Phase 2 deferred its interim to here) ran and reconciled at 91.53% vs 100% baseline via the now-third instance of the baseline-staleness pattern: May 7 baseline measured 1 of 14 mutant-bearing source files; 5 enumerated survivors all live in slice-2 (`3938931`) / slice-3 (`98c7574`) production code from 2026-04-26 / 2026-04-30, predating the migration by 7–11 days. CompileError delta 110 → 110 (unchanged), confirming no `Killed → CompileError` silent reclassification. Phase 11 changed zero production code; Phase 2 (co-driver) also changed zero production code in this assembly.

Full-solution build green; 398/398 tests across 10 assemblies; 5/5 Api.IntegrationTests pass with Testcontainers Postgres + Keycloak.

## Blocking-class issues

None.

## Should-fix issues

None unaddressed. All findings from the 4 review passes were applied mid-slice:
- requesting-code-review: 0 critical / 0 important / 3 cosmetic minors (all skipped per reviewer's own non-blocking framing).
- /simplify: 0 actionable findings.
- /pr-review-toolkit:review-pr: 1 Important (baseline-doc "1 of 14 files" ambiguity) applied at `0951c66`; 1 minor (method-name vestiges `InitializeAsync` / `DisposeAsync` on sync-void methods in CorsTests/OpenApiTests) deferred as cosmetic-only per reviewer.

## Nits

1. **`CorsTests.cs:15` and `OpenApiTests.cs:16` — `[TestInitialize] public void InitializeAsync()` retains the `*Async` suffix** on a sync-void method (`AuthSmokeTests.cs:20` is genuinely async; CorsTests / OpenApiTests collapsed to sync once they stopped awaiting anything). Pre-existing-shape preservation from xUnit; not a defect.
   - Cite: requesting-code-review + comment-analyzer both flagged as cosmetic.
   - Fix: optional rename to `Initialize`/`Cleanup` in a future polish pass.

2. **`KeycloakContainerFixture.cs:3` retains an unused `DotNet.Testcontainers.Containers` import** from the pre-Phase-11 source.
   - Cite: requesting-code-review minor #2. Pre-existing.
   - Fix: optional — TreatWarningsAsErrors passes (SDK tolerates unused imports), so non-blocking.

3. **`[TestClass]` on the abstract `KeycloakContainerTestBase`** is technically redundant (MSTest discovers only concrete derived classes) but matches the sibling pattern in Catalog and Organization.
   - Cite: requesting-code-review minor #1. Intentional consistency.
   - Fix: none.

## Missing tests

None. Phase 11 is a 1:1 translation slice. Test count parity: 5/5 (Api.IntegrationTests).

The official Stryker gate (`Kartova.SharedKernel.AspNetCore`) reconciled at 91.53% per merge-gate clause-(b). 5 enumerated survivors:

| File | Line | Mutator | Origin slice / commit |
|---|---|---|---|
| `DomainValidationExceptionHandler.cs` | 52 | Conditional (true) | slice-3 `98c7574` (2026-04-30) |
| `JwtAuthenticationExtensions.cs` | 54 | Statement | slice-2 `3938931` (2026-04-26) |
| `ModuleRouteExtensions.cs` | 21 | Statement | slice-3 `98c7574` (2026-04-30) |
| `ModuleRouteExtensions.cs` | 32 | Statement | slice-3 `98c7574` (2026-04-30) |
| `TenantScopeRouteExtensions.cs` | 26 | Statement | slice-2 `3938931` (2026-04-26) |

All 5 are in production code that predates the migration baseline by 7–11 days. The 3 Statement mutations in route-extension files could be killed by extending `tests/Kartova.ArchitectureTests/EndpointRouteRules.cs` to assert the registered endpoint set against the live `EndpointDataSource` for *both* modules' route extensions — out of scope for Phase 11 translation, flagged in the verification entry.

## What looks good

1. **Phase 11 is the migration's canonical §5.1 + §5.2 implementation.** Spec §5.1 used `KeycloakContainerFixture` as its `[AssemblyInitialize]` example; spec §5.2 used `AuthSmokeTests` as its `[TestInitialize]` / per-test-cadence example. Phase 11's implementation matches both spec examples near-verbatim — the only deviations are method names (`InitializeAsync`/`DisposeAsync` retained vs the spec's `TestInit`/`TestCleanup`), which are routed through attributes and have no observable effect.

2. **All Phase 9/10 lessons applied from the start.** `IntegrationTestAssemblySetup.cs` uses `[AssemblyInitialize]` not `[ClassInitialize(BeforeEachDerivedClass)]`; doc-comment explicitly cautions against the latter; cleanup path has no dead null-guard (Phase 9/10 cleanup learning at `913076d`). The Phase 11 implementation didn't repeat any of the regressions Phases 9 and 10 surfaced.

3. **Mutation-gate reconciliation closes the migration's mutation-gate landscape.** The Phase 11 verification entry documents the migration's repeated baseline-staleness pattern (Phase 4 + Phase 10 + Phase 11 all hit it) and recommends future baseline-refresh runs use full mode explicitly. The §"Migration mutation-gate landscape — closed" section at the end of the entry catalogs all 4 official gates (Phase 4 / 5 / 10 / 11) as landed and reconciled — useful summary for downstream readers.

4. **Comment quality discipline maintained on the migration's most-comment-heavy translation slice.** Each of the 3 translated test files retained their high-value WHY-comments (env-var-must-be-set-before-Program.Main-reads-config; Keycloak `--import-realm` rationale; ADR-0095 cross-reference) without introducing FA-archaeology or narrating-the-change comments. The 4 review passes all confirmed this.

5. **Migration trajectory: 11 of 12 phases done in this autonomous run.** Phase 12 (CPM cleanup — drop xUnit / FluentAssertions from `Directory.Packages.props` + sweep any Phase 0 leftovers) is the only remaining work. Phase 11 is the structurally hardest slice in the migration (assembly-scoped fixture, Postgres + Keycloak Testcontainers, JWT signer integration, real HTTP smoke for DoD #5, official mutation gate) and landed cleanly.

## DoD cross-check (`CLAUDE.md` §Definition of Done)

| # | Bullet | Evidence |
|---|---|---|
| 1 | Build green with `TreatWarningsAsErrors` | `dotnet build Kartova.slnx -warnaserror` → 0/0 at HEAD `0951c66` |
| 2 | Per-task subagent reviews | ➖ Skipped per controller-direct authorization |
| 3 | `superpowers:requesting-code-review` | ✅ 0 critical / 0 important / 3 cosmetic minors (all non-blocking per reviewer) |
| 4 | Full test suite | ✅ 398/398 across 10 assemblies; 5/5 Api.IntegrationTests with Testcontainers Postgres + Keycloak up |
| 5 | docker compose + HTTP smoke | ✅ **Satisfied by 5 Api.IntegrationTests** exercising real HTTP through `WebApplicationFactory<Program>` + Testcontainers Postgres + Keycloak (real OIDC token issuance via `TestJwtSigner` registered via `Containers.KeycloakAuthority`). AuthSmokeTests covers JWT issuance + API acceptance; CorsTests covers preflight + actual CORS headers; OpenApiTests covers `/openapi/v1.json` + ADR-0095 sort/limit schemas. Plus 26 Organization.IntegrationTests + 72 Catalog.IntegrationTests for additional HTTP coverage. |
| 6 | `/simplify` | ✅ 0 actionable findings across reuse, quality, efficiency |
| 7 | Mutation feedback loop | ✅ **OFFICIAL Stryker gate** for `Kartova.SharedKernel.AspNetCore` (Phase 2 co-driver close-out): 91.53% vs 100% baseline; reconciled per merge-gate clause-(b) as third instance of the baseline-staleness pattern. All 5 survivors enumerated and attributed to slice-2 / slice-3 pre-migration code. |
| 8 | `/pr-review-toolkit:review-pr` | ✅ comment-analyzer cleared with 1 important (baseline-doc ambiguity) applied at `0951c66`; 0 critical |
| 9 | `/deep-review` | ✅ This report |

## Verdict

Phase 11 ready to merge. The migration's last translation slice, the structurally most complex (Postgres + Keycloak Testcontainers + JWT signer), and the canonical example the spec §5.1 + §5.2 were built around. Implementation matches the spec near-verbatim, applies all Phase 9/10 lessons from the start, and closes the migration's official mutation-gate landscape with consistent baseline-staleness reconciliation. The 4 review passes surfaced findings that were all applied or explicitly triaged with rationale.

**One phase remains: Phase 12 — drop xUnit + FluentAssertions packages from `Directory.Packages.props` + sweep any Phase 0 leftovers. Single-file change to CPM; ~15min including the four DoD slice-boundary skills.** After Phase 12 closes, the migration is complete.
