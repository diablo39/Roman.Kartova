# DoD Ledger — E-01.F-07.S-01 Health Check Endpoints

**Slice:** `2026-09-23-e01f07-s01-health-checks` · **Branch:** `worktree-e01f07-s01-health-checks` · **HEAD:** `bb50aa5`
**PR:** not yet opened · **Last updated:** 2026-09-23
**Spec:** `docs/superpowers/specs/2026-09-23-e01f07-s01-health-checks-design.md`
**Plan:** `docs/superpowers/plans/2026-09-23-e01f07-s01-health-checks-plan.md` (local scratch, gitignored — not committed)
**Findings telemetry:** `./gate-findings.yaml` — backfilled 2026-09-23 (gate 8, a should-fix finding) from the SDD execution ledger (deleted per subagent-driven-development convention once the branch record lived in git) and this file's own prose history.

> Records the Definition of Done from `CLAUDE.md`. Update each row the moment its gate runs.

## Summary

| Gate | Status | Updated |
|------|--------|---------|
| 1 Build (`TreatWarningsAsErrors`) | ✅ PASS | 2026-09-23 |
| 2 Per-task subagent reviews | ✅ PASS | 2026-09-23 |
| 3 Full suite (+ real-seam if wiring) | ✅ PASS | 2026-09-23 |
| 4 Container build (images CI) | ✅ PASS | 2026-09-23 |
| 5 `/simplify` | ✅ PASS | 2026-09-23 |
| 6 `requesting-code-review` | ✅ PASS | 2026-09-23 |
| 7 `review-pr` | ✅ PASS | 2026-09-23 |
| 8 `deep-review` | ✅ PASS | 2026-09-23 |
| Terminal re-verify (build + suite) | ✅ PASS | 2026-09-23 |
| 9 Visual / API verification (ADR-0084) | ⏳ PENDING | — |
| 10 CI green on PR (`ci-local.sh` = pre-push mirror) | ⏳ PENDING | — |

## Gate detail

### 1 — Build (`TreatWarningsAsErrors=true`)
**Status:** ✅ PASS
**Evidence:** `dotnet test Kartova.slnx` (which builds every project first) completed with exit code 0 across all 20 test assemblies — a `TreatWarningsAsErrors=true` project would fail to build, not just fail tests, on any warning. No separate `dotnet build` run.
**At:** `d2a04c7`

### 2 — Per-task subagent reviews (spec + quality)
**Status:** ✅ PASS
**Evidence:** Executed via `superpowers:subagent-driven-development`. 4 tasks, each with a fresh implementer + fresh task reviewer (spec-compliance + quality verdicts): Task 1 (`1e2ab94`, approved clean), Task 2 (`c55f124`, approved clean), Task 3 (`c55f124..a714d81`, 1 fix round — reviewer caught a false BCL-API claim, controller independently verified via reflection probe, fix applied and re-reviewed clean), Task 4 (`a714d81..da223b4`, implementer BLOCKED on a genuine tenant-scope architectural gap, controller ruled + redirected the fix, re-reviewed clean). Then one final whole-branch review (Opus) over the full diff (`960ad74..da223b4`) found 2 Important + 4 Minor findings; one fix wave (`8bf1ab1`) addressed all of them, verified by a scoped re-review (Opus) — "all findings addressed, no new Critical/Important breakage". Full transcript lived in the SDD workspace ledger, deleted per that skill's own convention once its commits landed in git history (see `git log 960ad74..d2a04c7`).
**At:** `d2a04c7`

### 3 — Full test suite (unit + arch + integration; real-seam if wiring)
**Status:** ✅ PASS
**Evidence:** `dotnet test Kartova.slnx` — every assembly green, 0 failures:
`Kartova.SharedKernel.Identity.Tests` 27/27 · `Kartova.SharedKernel.AspNetCore.Tests` 104/104 · `Kartova.SharedKernel.Tests` 146/146 · `Kartova.SharedKernel.Postgres.IntegrationTests` 8/8 · `Kartova.Organization.Infrastructure.Tests` 104/104 · `Kartova.Catalog.Infrastructure.Tests` 11/11 · `Kartova.Catalog.Tests` 381/381 · `Kartova.ArchitectureTests` 75/75 · `Kartova.Audit.Infrastructure.IntegrationTests` 35/35 · `Kartova.Organization.Tests` 83/83 · `Kartova.SharedKernel.Identity.IntegrationTests` 8/8 · `Kartova.Api.IntegrationTests` **21/21** (includes this slice's new real-seam tests: `KeycloakHealthCheckTests` against a real Testcontainers KeyCloak, `ModuleMigrationsHealthCheckTests` against real Testcontainers Postgres with real EF migrations, `HealthCheckEndpointTests` through the full real ASP.NET Core pipeline with real KeyCloak password-grant tokens — no mocked `DbContext`, no bypass-auth handler) · `Kartova.Catalog.IntegrationTests` 494/494 · `Kartova.Organization.IntegrationTests` 142/142. Exit code 0.
**At:** `d2a04c7`

### 4 — Container build (images CI job)
**Status:** ✅ PASS
**Evidence:** `docker compose build migrator api` — both images built clean (`kartova/migrator:dev`, `kartova/api:dev`). `docker build -f web/Dockerfile -t kartova/web:ci web` — built clean. This branch's only `*.csproj` change is `<PackageReference Include="NSubstitute" />` added to `tests/Kartova.Api.IntegrationTests.csproj` (a test project) — confirmed via `src/Kartova.Api/Dockerfile`'s `COPY` layer that it copies only `src/`, never `tests/`, so this restore-surface change cannot affect the runtime image's build graph. Ran anyway (not marked N/A) since the letter of the gate-4 rule triggers on any `*.csproj` package-ref change; all three images built green regardless.
**At:** `d2a04c7`

### 5 — `/simplify` against branch diff
**Status:** ✅ PASS
**Evidence:** 4 parallel review agents (reuse, simplification, efficiency, altitude) over `git diff 960ad74..8bf1ab1`. Applied: (1) altitude — `ModuleMigrationsHealthCheck` rebuilt to resolve module DbContexts via each module's existing `IModule.RegisterForMigrator` override (the tenant-scope-free path + resolution mechanism Kartova.Migrator already uses) instead of a hand-built `Dictionary<Type, Func<DbContext>>`; (2) efficiency — per-module migration checks now run via `Task.WhenAll`; per-call `DbContextOptions` rebuild eliminated as a side effect of (1); (3) simplification — `HealthCheckJsonResponseWriter`'s compact/detailed branches now share their common fields instead of duplicating them in two anonymous types; (4) reuse — `GetRealTokenAsync`/`EnvKey` test helpers moved onto `KeycloakContainerTestBase`, redundant private copies removed from `AuthSmokeTests`/`CorsTests`/`OpenApiTests`/`HealthCheckEndpointTests`. Skipped (documented, not silently dropped): reuse-finding re: `AuthSmokeTests`'s duplicated env-var-wiring block in `HealthCheckEndpointTests` (the two blocks aren't identical enough to collapse without touching more of the pre-existing `AuthSmokeTests.cs` than this pass's blast radius); reuse-finding re: `KeycloakHealthCheck`'s own named `HttpClient` vs `AddKeycloakAdminClient`'s typed client (correct intentional isolation — that client carries admin-token concerns the health check shouldn't touch). Full solution build (0 warnings/errors) + full `Kartova.slnx` test suite (15/15 assemblies, 0 failures) re-verified green after applying fixes. Commit `b4fa970`.
**At:** `b4fa970`

### 6 — `requesting-code-review` at slice boundary
**Status:** ✅ PASS
**Evidence:** Fresh reviewer (`superpowers:requesting-code-review`'s code-reviewer template) over the full branch diff `960ad74..d1be9f0`, working tree confirmed read-only-clean afterward. Verdict: "Ready to merge: With fixes" — 0 Critical, 1 Important (this ledger's own gate-4 summary/detail contradiction — fixed above, in the same pass), 2 Minor (both accepted trade-offs already reasoned about in the spec/design: compact-mode description suppression is a blunt-but-safe default; `Task.WhenAll` losing per-module diagnostic precision on a mixed real-failure is in-scope per spec's error-handling section). Independently re-verified the `IModule.RegisterForMigrator` reuse against each module's actual override and confirmed the `ASP0000` suppression is genuinely justified (the built `ServiceProvider` is registered as a root-owned singleton instance, so the analyzer's actual leak concern doesn't apply). 5 items explicitly declined-to-judge (documented, none silently dropped) — all pre-existing scope decisions (ADR-0060's "Operations role" phrasing vs. the spec's concrete `PlatformAdmin` choice; TD-011's Kafka/ES/MinIO deferral; writer's package placement; migrations-check unit-tier N/A).
**At:** `d1be9f0`

### 7 — `review-pr` (pr-review-toolkit)
**Status:** ✅ PASS
**Evidence:** Trimmed standing set per CLAUDE.md (`type-design-analyzer` + `pr-test-analyzer` + `code-reviewer`), plus `silent-failure-hunter` (diff adds/changes error handling). All 4 ran in parallel over `git diff 960ad74..d1be9f0`.

Fixed (commit `1c3612c`):
- **Critical** (silent-failure-hunter): `HealthCheckJsonResponseWriter`'s compact-mode `description` suppression conflated the framework's exception-message fallback with a check's own safe, hand-authored description — confirmed against `KeycloakHealthCheck`'s own real usage (`Unhealthy("KeyCloak discovery endpoint unreachable", ex)`), which was being wrongly nulled. Now only suppresses when `Description == Exception.Message`.
- **Important** (type-design-analyzer + silent-failure-hunter): `KeycloakHealthCheck`'s narrow catch (`HttpRequestException or TaskCanceledException`) missed config/URI errors — widened to catch broadly; the check now never throws for any failure mode.
- **Important** (code-reviewer): no explicit per-check `timeout:` — `HealthCheckRegistration.Timeout` defaults to `Timeout.InfiniteTimeSpan`, not a finite backstop as the spec had wrongly claimed (spec corrected). All three dependency checks now carry an explicit timeout.
- **Important** (code-reviewer): a future `IModule` that doesn't override `RegisterForMigrator` would silently fall back to the tenant-scoped `RegisterServices`, breaking the migrations-check provider at runtime with no build-time signal — added `IModuleRules.Every_IModule_implementation_overrides_RegisterForMigrator` (NetArchTest-style reflection check, matches this project's existing arch-test conventions).
- **Important** (pr-test-analyzer): no real HTTP-level test that an Unhealthy report yields 503 — added `Ready_and_startup_return_503_when_keycloak_is_actually_unreachable` (dedicated `WebApplicationFactory` with KeyCloak pointed at a closed port; Postgres — from the shared assembly container — stays real/healthy, so only that one dependency fails; confirms `/health/live` still 200).
- **Important** (pr-test-analyzer): no positive-path writer test proving an authored `description` (with or without an attached exception) survives compact mode — added two.
- Minor (type-design-analyzer): extracted the migrations-check `ServiceProvider` construction into a named `BuildMigrationsCheckProvider` method so the `ASP0000` justification travels with the code it protects.

Parked (documented, not silently dropped — narrower/lower-blast-radius than the above, each with a stated reason):
- (silent-failure-hunter, Medium) `ModuleMigrationsHealthCheck`'s `Task.WhenAll` surfaces only the first fault when 2+ modules fail concurrently in *different* ways (one hard DI failure + one genuine pending-migrations finding on a sibling module), losing the sibling's status. Narrow (requires two distinct simultaneous failure modes across different modules) and doesn't defeat the feature's purpose the way the Critical finding did — deferred as a follow-up.
- (type-design-analyzer, Low) `ModuleMigrationsHealthCheck` ctor takes `IModule[]` rather than `IReadOnlyList<IModule>` — cheap defensive-copy-avoidance nitpick, no behavior change, deferred.
- (type-design-analyzer, Low) `Program.cs`'s four `MapHealthChecks` call sites have no compile-time tie preventing a future one from forgetting its `ResponseWriter`/tag predicate — a suggested DRY helper, not a defect; deferred.
- (pr-test-analyzer, Minor) only single-module-pending is tested for `ModuleMigrationsHealthCheck`'s `string.Join` formatting (2+ modules untested); dead `PostgresErrorCodes.DuplicateObject` catch in `ModuleMigrationsHealthCheckTests` (unreachable on a fresh-per-test container, harmless, matches the pattern used elsewhere in the file) — both deferred as polish.
- (code-reviewer, Note) ADR-0060 names an "Operations role" and extra `/health/detailed` fields (version, dependency URIs, percentiles) not present in this slice — already declined-to-judge at gate 6 (no `Operations` role exists in this codebase; the spec's concrete `PlatformAdmin` choice is authoritative per CLAUDE.md's "ADR wins, fix the ADR if stale" rule) and at gate 7; still deferred, no ADR amendment made in this slice.
- Helm chart probe `timeoutSeconds` mismatch (`deploy/helm/kartova/templates/api-deployment.yaml`) — still deferred as a separate infra concern (unchanged from gate-6's note), though the new C#-level per-check timeouts meaningfully reduce the risk even without a Helm change.

Full solution build (0 warnings/errors) + full `Kartova.slnx` suite (15/15 assemblies, 0 failures, including the new arch test and the new 503/writer tests) re-verified green after applying fixes.
**At:** `1c3612c`

### 8 — `deep-review`
**Status:** ✅ PASS
**Evidence:** `docs/superpowers/verification/2026-09-23-e01f07-s01-health-checks/deep-review.md` — fresh reviewer read the diff against the spec, the (gitignored, slice-local) plan, ADR-0060/0090/0085, ADR-0097's test taxonomy, and this ledger, then independently re-ran the evidence rather than trusting it (5/5 writer tests, 4/4 arch tests incl. the new `IModuleRules` rule, 16/16 filtered health-check tests, 22/22 full `Kartova.Api.IntegrationTests`, all against real Testcontainers Postgres/KeyCloak). **0 blocking, 2 should-fix (both fixed, commit `bb50aa5`), 4 nits (3 fixed alongside, 1 parked as harmless/delusion), 0 missing-test findings, 5 "what looks good" citations.** Fixed: `gate-findings.yaml` was missing (this folder's sibling file now exists, backfilled); the `ASP0000` suppression comment factually claimed the DI container disposes an `AddSingleton(instance)` registration, which it does not — verified empirically and corrected. Reviewer explicitly credited the plan's Task 4 divergence (hand-built dictionary → `IModule.RegisterForMigrator` reuse, discovered during `/simplify`) as "well justified and superior," not an undocumented deviation.
**At:** `bb50aa5`

### Terminal re-verify (build + full suite after gates 5–8)
**Status:** ✅ PASS
**Evidence:** `dotnet test Kartova.slnx` on the final commit after all gate 5-8 fixes — 15/15 assemblies green, 0 failures (same command/shape as gate 3's original run, now against the post-fix code).
**At:** `bb50aa5`

### 9 — Visual / API verification (observe the running system)
**Status:** ⏳ PENDING
**Evidence:** —
**At:** —

### 10 — CI green on the PR (terminal; `scripts/ci-local.sh` = required pre-push mirror)
**Status:** ⏳ PENDING
**Evidence:** —
**At:** —
