# Deep Review — E-01.F-07.S-01 Health Check Endpoints

**Diff:** `960ad74..5d58bba` · **Branch:** `worktree-e01f07-s01-health-checks` · **Reviewed:** 2026-09-23
**Spec:** `docs/superpowers/specs/2026-09-23-e01f07-s01-health-checks-design.md`
**Plan:** `docs/superpowers/plans/2026-09-23-e01f07-s01-health-checks-plan.md`
**DoD ledger:** `docs/superpowers/verification/2026-09-23-e01f07-s01-health-checks/dod.md`

## Overview

The slice wires `/health/live`, `/health/ready`, `/health/startup`, `/health/detailed` in `Kartova.Api` per ADR-0060: a new `KeycloakHealthCheck` (OIDC discovery-endpoint probe), a new `ModuleMigrationsHealthCheck` (per-module pending-migrations probe resolved via a dedicated, tenant-scope-free `ServiceProvider` built from each `IModule.RegisterForMigrator` override), a new `HealthCheckJsonResponseWriter` (compact/detailed JSON shapes), and a fix to the pre-existing `/health/startup` tag bug. It ships 16 new real-seam integration tests (Testcontainers Postgres + KeyCloak), 5 unit tests for the writer, and one new NetArchTest rule enforcing that every `IModule` overrides `RegisterForMigrator`. Four prior review passes (gates 2, 5, 6, 7) already found and fixed several issues, most notably a spec correction about `HealthCheckRegistration.Timeout`'s default and a widened `KeycloakHealthCheck` catch clause.

## Blocking-class issues

None.

## Should-fix issues

- **`gate-findings.yaml` required by CLAUDE.md is missing from the verification folder.**
  **Evidence:** `docs/superpowers/verification/2026-09-23-e01f07-s01-health-checks/` contains only `dod.md` — no `gate-findings.yaml`. `dod.md:5` states: *"Findings telemetry: none yet — no `gate-findings.yaml` created; SDD's own ledger ... deleted per subagent-driven-development convention once the branch record lived in git."*
  **Impact:** CLAUDE.md's Definition of Done section states plainly: *"Alongside `dod.md`, each slice keeps `gate-findings.yaml` ... so gate effectiveness is queryable across slices."* This is not conditional — the prose findings that ARE recorded (gate 2's one fix round, gate 5's applied/skipped items, gate 6's 1 Important + 2 Minor, gate 7's 2 Critical/Important/Minor fixed + 5 parked) exist only as free text inside `dod.md`, not as the machine-readable per-finding records the working agreement mandates for cross-slice gate-effectiveness tracking (the same tracking that produced the "gate 7 caught 2 findings the other two review gates missed" and "reviewer oracle quality" observations already baked into CLAUDE.md itself). Without it, this slice's findings can't be queried alongside prior slices'.
  **Fix:** Copy `docs/superpowers/templates/gate-findings-template.yaml` into this verification folder and backfill one entry per finding already described in `dod.md` §2/§5/§6/§7 (gate slug, severity, `real`/`delusion` verdict, one-line description) before closing gate 8.

- **`Program.cs:337-341`'s ASP0000 suppression comment makes a disposal claim that is not true of .NET DI.**
  **Evidence:** `src/Kartova.Api/Program.cs:339-341`:
  ```
  // The returned instance is registered as an app singleton, so the root
  // provider owns and disposes it with the app.
  ```
  The provider is registered via `builder.Services.AddSingleton(BuildMigrationsCheckProvider(modules, builder.Configuration))` (`Program.cs:62`) — i.e. `AddSingleton(instance)`, not a factory/type registration. I verified empirically (throwaway repro against `Microsoft.Extensions.DependencyInjection` 10.0.0: `services.AddSingleton(existingProvider)`, then `root.Dispose()`) that the root container does **not** call `Dispose` on an instance registered this way — only services the container itself constructs (via type or factory registration) are tracked for disposal. Microsoft's own DI guidance says the same: the container never disposes an instance it didn't create.
  **Impact:** `migrationsCheckProvider` (which owns the per-module `DbContext` factories built from `RegisterForMigrator`) is never explicitly disposed anywhere in this codebase. In practice the blast radius is low — it's a single process-lifetime object and process exit reclaims the underlying connections regardless — but the comment's stated justification for why this is safe is factually wrong, and if this "register the returned instance as a singleton and it'll be disposed with the app" pattern is copied elsewhere (e.g. for a disposable resource with buffered/flush-on-dispose semantics), the same wrong assumption would this time have a real consequence.
  **Fix:** Correct the comment to state the actual guarantee (an instance registration is not disposed by the container; this is accepted here because the object is process-lifetime-scoped and the app never restarts it without a process restart) — or, if orderly shutdown disposal is wanted, register it via a factory (`AddSingleton<ServiceProvider>(sp => BuildMigrationsCheckProvider(...))`) instead of a pre-built instance, or dispose it explicitly from an `IHostApplicationLifetime.ApplicationStopping` callback.

## Nits

- **`ModuleMigrationsHealthCheck.cs:14`'s doc comment says "the same `GetService(module.DbContextType)` resolution mechanism" but the code calls `GetRequiredService`.** `src/Kartova.Api/HealthChecks/ModuleMigrationsHealthCheck.cs:29` uses `scope.ServiceProvider.GetRequiredService(module.DbContextType)`, while `Kartova.Migrator/Program.cs:39` uses `GetService(...) ?? throw new InvalidOperationException(...)`. Functionally equivalent (both end in an exception on a missing registration, which is what the "not throwing" test exercises), but the comment overclaims literal parity with Migrator's call. Fix: say "the same DbContextType-keyed resolution, expressed as `GetRequiredService`" or similar.
- **`dod.md`'s header `HEAD` field is stale relative to the branch tip.** `docs/superpowers/verification/2026-09-23-e01f07-s01-health-checks/dod.md:2` says `**HEAD:** \`1c3612c\`` but the ledger itself was last updated by commit `5d58bba` (one commit later, docs-only). Trivial, but a ledger whose own header trails its authoring commit invites the exact "is this actually current" doubt the ledger exists to remove. Fix: bump to `5d58bba` (or note that the field tracks last-code-commit, not last-ledger-commit, if that's intentional).
- **Spec's Components table (`docs/superpowers/specs/2026-09-23-e01f07-s01-health-checks-design.md:31`) describes the KeyCloak check as using "a named `HttpClient` (`AddHttpClient<KeycloakHealthCheck>`)"** — `AddHttpClient<T>()` is the *typed*-client overload; the shipped code (`Program.cs:288`) correctly uses the *named*-client overload `AddHttpClient(KeycloakHealthCheck.ClientName, ...)`, matching the constructor's `IHttpClientFactory` dependency. The implementation is right; the spec's parenthetical is just mislabeled. Fix: reword to `AddHttpClient(KeycloakHealthCheck.ClientName, ...)` next time the spec is touched.
- **The JSON writer always emits a `"description"` key (often `null`), where ADR-0060's illustrative sample response omits the key entirely for healthy entries with no description.** `src/Kartova.SharedKernel.AspNetCore/HealthChecks/HealthCheckJsonResponseWriter.cs:396-409`. Harmless — the ADR sample is illustrative, not a schema, and every consumer here already null-checks — but worth a one-line ADR amendment if the shape is ever pinned more strictly for the future status-page reuse the ADR calls out.

## Missing tests

None found — every acceptance criterion I could locate in the spec's Testing section and the plan's Review Focus section has a corresponding test, and I ran the real ones (see Evidence below) rather than take the claim on faith:
- Live excludes deps / exact `{self}` entry set → `HealthCheckEndpointTests.Live_returns_200_and_only_the_self_entry`, `HealthCheckIsolationTests.Live_check_stays_healthy_even_though_an_unrelated_ready_dependency_is_down`.
- Ready 503 when a real dependency is down → `HealthCheckEndpointTests.Ready_and_startup_return_503_when_keycloak_is_actually_unreachable`.
- Startup includes `migrations` → `HealthCheckEndpointTests.Startup_returns_postgres_keycloak_and_migrations_entries_and_is_healthy`.
- `/health/detailed` 401/403/200 → the three `Detailed_*` tests in `HealthCheckEndpointTests`.
- KeyCloak check never leaks the admin secret → `KeycloakHealthCheckTests.Unhealthy_result_never_contains_the_configured_admin_secret`.
- Migrations check never throws on an unregistered module's `DbContextType` → `ModuleMigrationsHealthCheckTests.Unhealthy_not_throwing_when_a_module_DbContextType_has_no_factory_entry`.
- Writer doesn't blow up on null `Description`/`Exception` → `HealthCheckJsonResponseWriterTests.WriteCompactAsync_emits_valid_json_and_omits_exception_field`.
- Every `IModule` overrides `RegisterForMigrator` (build-time signal for the ADR-0090 tenant-scope hazard) → `IModuleRules.Every_IModule_implementation_overrides_RegisterForMigrator`.

## Evidence — I ran these, not just read them

- `dotnet test tests/Kartova.SharedKernel.AspNetCore.Tests` (writer tests, filtered): **5/5 passed**, 105 ms.
- `dotnet test tests/Kartova.ArchitectureTests` (filtered to `IModuleRules`): **4/4 passed**, including the new `Every_IModule_implementation_overrides_RegisterForMigrator`.
- `dotnet build tests/Kartova.Api.IntegrationTests`: **0 warnings, 0 errors** (confirms `TreatWarningsAsErrors=true` holds for the new/changed files, including the `ASP0000` pragma-suppressed method).
- `dotnet test tests/Kartova.Api.IntegrationTests --filter "FullyQualifiedName~HealthChecks"` against real Testcontainers Postgres + KeyCloak: **16/16 passed** (`HealthCheckEndpointTests` ×7, `HealthCheckIsolationTests` ×2, `KeycloakHealthCheckTests` ×4, `ModuleMigrationsHealthCheckTests` ×3), ~1 min.
- `dotnet test tests/Kartova.Api.IntegrationTests` (full project, no filter): **22/22 passed** — no regression in `AuthSmokeTests`/`CorsTests`/`OpenApiTests` from the `EnvKey` helper's move to `KeycloakContainerTestBase`.
- Confirmed via a throwaway console repro that `IServiceCollection.AddSingleton(existingInstance)` is not disposed by the root `ServiceProvider` (see Should-fix #2) — corrects the `ASP0000` comment's stated rationale.

All DoD-ledger claims I could independently re-run (gate 1 build, gate 3 real-seam suite, the specific architecture test, the specific 503 test) checked out. Gates 8 (this review), the terminal re-verify, 9 (visual/API), and 10 (CI) are correctly marked pending in `dod.md` and are not blocking findings — they are the expected next steps, not gaps in this review.

## What looks good

- **`ModuleMigrationsHealthCheck` correctly respects ADR-0085** — it only calls `GetPendingMigrationsAsync`, never `MigrateAsync`; migration application stays exclusively `Kartova.Migrator`'s job. Verified by reading `src/Kartova.Api/HealthChecks/ModuleMigrationsHealthCheck.cs:31` against `src/Kartova.Migrator/Program.cs:43`.
- **The `/simplify` → gate-7 divergence from the plan's original `Dictionary<Type, Func<DbContext>>` design is well justified and superior.** Reusing each module's existing `IModule.RegisterForMigrator` override (the same mechanism `Kartova.Migrator` already relies on) instead of hand-building a parallel factory map is less code, avoids drift between the migrator's and the health check's module-resolution logic, and the risk it introduces (a future module silently falling back to the tenant-scoped default) is closed by a new, well-targeted architecture test (`IModuleRules.Every_IModule_implementation_overrides_RegisterForMigrator`) rather than left as an undocumented assumption.
- **The ADR-0090 tenant-scope hazard is handled correctly and defended in depth**, not just documented: `ModuleMigrationsHealthCheck` resolves DbContexts from a dedicated, tenant-scope-free `ServiceProvider` (never from `builder.Services`/`AddModuleDbContext`), the reasoning is in three places (spec, `Program.cs` comment, the health check's own doc comment), and the new arch test makes a regression a build-time failure instead of a runtime one.
- **Real-seam discipline is genuinely honored, not just claimed.** Every wiring-tier test (HTTP + auth + DB + external KeyCloak call) runs against real Testcontainers Postgres/KeyCloak with real JWT password-grant tokens — no mocked `DbContext`, no bypass-auth handler — and I confirmed this by actually running them rather than trusting the ledger.
- **The gate-7 fix for the compact-writer description suppression is precise, not a blunt fix.** Rather than blanket-suppressing every `description` when an exception is present (which would have nulled `KeycloakHealthCheck`'s own deliberately-safe messages), it narrows to `Description == Exception.Message` — the one case that's actually the framework's own leak-prone fallback — and there's a dedicated regression test (`WriteCompactAsync_preserves_a_check_authored_description_carried_alongside_an_exception`) pinning exactly that distinction.
