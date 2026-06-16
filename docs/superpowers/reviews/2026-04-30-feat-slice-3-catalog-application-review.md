# Deep review — `feat/slice-3-catalog-application`

**Date:** 2026-04-30
**Status:** OPEN (pre-merge gate vs. `master`)
**Spec:** `docs/superpowers/specs/2026-04-29-slice-3-catalog-application-design.md`
**Plan:** `docs/superpowers/plans/2026-04-29-slice-3-catalog-application-plan.md`
**ADR index:** `docs/architecture/decisions/README.md`
**Test taxonomy:** ADR-0083
**Mutation report:** `mutation-report-surviving.md` (stale — generated 2026-04-26, predates slice-3)
**DoD reference:** `CLAUDE.md §Definition of Done`

---

## Overview

Slice 3 lands the first vertical write into the Catalog module: `Application` aggregate with required-field invariants, EF migration with RLS, three Wolverine-style handlers (resolved synchronously from the HTTP request scope), three minimal-API endpoints, and the cross-tenant-write probe. The slice also introduces `IModule.Slug` + `IModuleEndpoints`, the `MapTenantScopedModule`/`MapAdminModule` route helpers (ADR-0092), `ICurrentUser`, and the `DomainValidationExceptionHandler` that resolves spec §13.3 in-slice. KeyCloak realm seed gains `admin@orgb.kartova.local` so cross-tenant integration tests can authenticate as a second tenant.

---

## Blocking-class issues

**None.** All five DoD gates have a concrete code path or an explicit pending-verification flag in the plan; nothing in the diff falsifies the spec's invariants. (Docker compose smoke at plan Task 14 §4 is a *user-runs-locally* gate — its absence from the diff is expected, not a finding. Confirm before merge.)

---

## Should-fix issues

### 1. Branch is missing ADR-0093 referenced by slice-3 code

- **Evidence:** `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs:17` cites "ADR-0090, formalized in ADR-0093". The spec's §13.2 resolution (line 485) and the merge-resolution commit `d0dd2e1` claim ADR-0093 was filed in PR #11 (merged 2026-04-30 on master, `93a176f`). On this branch, `docs/architecture/decisions/ADR-0093-wolverine-scope-narrowed.md` does not exist (`git log feat/slice-3-catalog-application -- docs/architecture/decisions/ADR-0093-*.md` returns nothing).
- **Impact:** Anyone reviewing the PR by checking out the branch will hit a broken citation chain — the source comment names an ADR they cannot read. Post-merge the link will resolve, but the pre-merge reviewer experience is broken.
- **Fix:** `git fetch origin && git merge origin/master --no-edit` on the branch before merge so ADR-0093 lands in the same diff that cites it. Same pattern as the spec's pre-flight ADR-0092 step.

### 2. Spec/code divergence on `current_setting` strictness — undocumented

- **Evidence:** Spec §5.6 (line 262) prescribes `current_setting('app.current_tenant_id', true)::uuid` (the second arg makes a missing GUC return `NULL` instead of erroring). Migration `src/Modules/Catalog/Kartova.Catalog.Infrastructure/Migrations/20260429185603_AddApplications.cs:40` ships strict `current_setting('app.current_tenant_id')::uuid` — matches Organization's `20260423080230_InitialOrganization.cs:38`, but contradicts the spec.
- **Impact:** Behavior is correct *and arguably safer* (a missing GUC throws rather than silently returning all rows), and matches the existing Organization precedent. But the spec's prescribed form survives unchanged in the design doc, which means the next slice author copy-pasting from the spec gets the wrong form. The spec's §12 self-review claims "no issues found" on contract consistency.
- **Fix:** Either update spec §5.6 to match the strict form already on master (one-line edit; cite Organization migration as precedent), or add a one-line comment in the migration `Up` justifying the divergence. Strict form is the right default — `TenantScopeRequiredInterceptor` already prevents `SaveChanges` outside a scope, and a missing GUC at query time is a programmer error worth surfacing loudly.

### 3. Stale comments left in slice-3 files

- **Evidence:**
  - `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs:64` — `// Handlers and publish routes arrive in Slice 3.` This *is* slice 3, and the slice deliberately uses direct dispatch (ADR-0093), so "publish routes" is wrong tense.
  - `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogDbContext.cs:6-7` — XML doc says `Owns KartovaMetadata in Slice 1. Domain entities (Service, Application, API, etc.) arrive in Slice 3.` Slice 3 is here; the comment is now describing the past in future tense.
- **Impact:** The CLAUDE.md guidance is "don't reference the current task / fix / callers — those rot as the codebase evolves." These comments will mislead the next reader exactly because they're written in slice-temporal voice.
- **Fix:** Drop both temporal references. `CatalogModule.ConfigureWolverine` just discovers handlers — that's what the comment should say. `CatalogDbContext` summary should describe what the DbContext owns *now* (`KartovaMetadata` + `Application`), without slice numbers.

### 4. Mutation pass is stale

- **Evidence:** `mutation-report-surviving.md:1-8` reports a Stryker run timestamped `2026-04-26T17:01:02` against pre-slice-3 code. The Catalog mutants it lists (`CatalogModule.cs:17/21/28`) target the slice-2 baseline DbContext registration, not the slice-3 handler / aggregate / endpoint surface this PR adds.
- **Impact:** The mutation-sentinel feedback loop the project relies on (`init-code-quality` + `mutation-sentinel` skills, CLAUDE.md mentions ≥80% target) has not run against the new domain factory, the three handlers, the endpoint delegates, the exception handler, or the cross-tenant probe. Specific surviving-mutant risks for slice-3 code are unknown.
- **Fix:** Run `mutation-sentinel` against `src/Modules/Catalog/**` and `src/Kartova.SharedKernel.AspNetCore/{HttpContextCurrentUser,DomainValidationExceptionHandler,ModuleRouteExtensions}.cs` before declaring the slice complete; address survivors per usual loop. At minimum, regenerate `mutation-report-surviving.md` so the next slice doesn't inherit a stale baseline.

### 5. Spec drift: `ITenantScope` in handler signature → actually `ITenantContext`

- **Evidence:** Spec §5.5 (line 232) shows handler signature `ITenantScope scope`. Implementation `src/Modules/Catalog/Kartova.Catalog.Infrastructure/RegisterApplicationHandler.cs:26` uses `ITenantContext tenant`. The `ITenantContext` is the correct read-only surface (handlers should never call `BeginAsync`/`CommitAsync` per ADR-0090); `ITenantScope` is the lifecycle interface owned by the transport adapter.
- **Impact:** The shipped code is *more correct* than the spec — handlers do not have access to the lifecycle. But the spec still reads as if handlers see `ITenantScope`. Anyone copying the spec into a new module's handler picks up a too-broad dependency.
- **Fix:** One-line spec edit at §5.5 changing `ITenantScope scope` → `ITenantContext tenant` (and `scope.TenantId` → `tenant.Id`) so the spec describes what shipped.

---

## Nits

### 1. `Application` uses `private set` rather than spec's `private init`

- **Evidence:** `src/Modules/Catalog/Kartova.Catalog.Domain/Application.cs:7-12`. Spec §5.4 (line 189) uses `private init`. EF Core can populate either; `init` would prevent post-construction mutation but EF still materializes via the parameterless constructor + private setters, so `init` would have meant changing how EF binds. The shipped form is consistent with `Organization` aggregate.
- **Fix:** Either align spec §5.4 with the shipped form, or — if you want immutability post-materialization — switch to `init` and verify EF still binds (it does, with the right configuration). Either way, document which is the project convention.

### 2. `ListApplicationsHandler.OrderBy(CreatedAt)` has no tiebreaker

- **Evidence:** `src/Modules/Catalog/Kartova.Catalog.Infrastructure/ListApplicationsHandler.cs:21`. Two rows inserted in the same `DateTimeOffset.UtcNow` tick will have unstable order across runs; the integration test `RegisterApplicationTests.cs:117` asserts `body.OrderBy(x => x.CreatedAt).Should().Equal(body)` which is tautological for ties.
- **Fix:** `.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)` for deterministic order; same one-line change in the test if it asserts an explicit sequence.

### 3. `RegisterForMigrator` swallows the wrong exception text

- **Evidence:** `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs:52-54`. Error message references `ConnectionStrings__{KartovaConnectionStrings.Main}` — fine — but the same string literal appears in `Program.cs:38` and `OrganizationModule.cs:49`. Three copies of the same diagnostic string drift independently the moment one of them changes.
- **Fix:** Lift to a single static helper on `KartovaConnectionStrings` (`KartovaConnectionStrings.RequireMain(IConfiguration)` returning the string or throwing the canonical message). Out of slice-3 scope as a refactor; record as a follow-up.

### 4. Empty XML summary on `ICurrentUser` doesn't mention DI registration

- **Evidence:** `src/Kartova.SharedKernel.AspNetCore/ICurrentUser.cs:6-9`. The interface contract says "caller must run inside the auth pipeline" but doesn't say where the registration lives — `JwtAuthenticationExtensions.cs:55` does the `AddScoped<ICurrentUser, HttpContextCurrentUser>()`. A reader resolving `ICurrentUser` from a non-AspNetCore module won't find the registration without grepping.
- **Fix:** Add one line to the interface summary: "Registered by `AddKartovaJwtAuth`."

### 5. `KartovaApiFixture` duplicated, not shared

- **Evidence:** `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/KartovaApiFixture.cs:28` is structurally identical to the Organization fixture (Postgres container, role bootstrap, JWT signer wiring). The only Catalog-specific bit is `RunMigrationsAsync<CatalogDbContext>` at line 43.
- **Fix:** Extract the common parts to `Kartova.Testing.Auth` (or a new `Kartova.Testing.Api`) so the next module's integration tests don't copy this 140-line file. Out of slice-3 scope; record as follow-up.

---

## Missing tests

### 1. Boundary test for name length — exactly 256 chars

- **Acceptance criterion:** Spec §5.4 invariant `name.Length > 256` throws. Mutation-report parallel: Organization has identical `if (name.Length > 100)` boundary mutated to `>= 100` and survived (`mutation-report-surviving.md:271-276`). The Application factory has the same shape and is exposed to the same mutation.
- **Test that should exist:** `Kartova.Catalog.Tests.ApplicationTests.Create_succeeds_with_name_at_exactly_256_chars` — pass `new string('x', 256)` and assert no throw, plus `app.Name.Length == 256`. Pairs with the existing `Create_throws_on_name_over_256_chars` (257 chars) to kill the boundary mutant.

### 2. `CatalogModule.RegisterForMigrator` is uncovered

- **Acceptance criterion:** Plan Task 5 / spec §4.3 require `RegisterForMigrator` to use raw `AddDbContext` against the migrator connection string. Mutation-report Organization parallel: `OrganizationModule.cs:49/53` mutants survive with NoCoverage (`mutation-report-surviving.md:302-316`). Catalog's identical method has no caller in tests.
- **Test that should exist:** `Kartova.Catalog.Infrastructure.Tests.CatalogModuleRegisterForMigratorTests` — call `RegisterForMigrator(services, config)` with a stub config, resolve `CatalogDbContext`, assert it's wired to the configured connection string and not registered through the tenant-scoped `AddModuleDbContext` path.

### 3. Endpoint route names are not pinned

- **Acceptance criterion:** Plan Task 9-11 specify `.WithName("RegisterApplication")` / `"GetApplicationById"` / `"ListApplications"`. Mutation-report parallel: `OrganizationEndpoints.cs:17` `MapGet(...)` mutated to `;` survived (`mutation-report-surviving.md:22-28`).
- **Test that should exist:** `Kartova.Catalog.Tests.CatalogEndpointRouteNamesTests` (or extend an arch test) — enumerate the WebApplication's `EndpointDataSource`, assert exactly these three named routes exist with their HTTP methods and paths. Kills `MapPost`/`MapGet` statement mutants in `CatalogModule.MapEndpoints`.

### 4. `DomainValidationExceptionHandler` does not assert ProblemDetails body shape

- **Acceptance criterion:** Spec §13.3 resolution claims the handler emits RFC 7807 with `type = ProblemTypes.ValidationFailed`. The unit tests at `tests/Kartova.SharedKernel.AspNetCore.Tests/DomainValidationExceptionHandlerTests.cs:18-53` assert only `StatusCode == 400` and the boolean return. They do not deserialize the response body.
- **Test that should exist:** Extend `TryHandleAsync_maps_ArgumentException_to_400_problem_details` to read `ctx.Response.Body`, deserialize as `ProblemDetails`, and assert `Type == ProblemTypes.ValidationFailed`, `Title == "Invalid request"`, `Detail == "name must not be empty"`. Without this, mutating `Type = ProblemTypes.ValidationFailed` to any other constant survives.

### 5. No test pins that `ICurrentUser` resolves from the same scope as `CatalogDbContext`

- **Acceptance criterion:** Spec §3 decision 5 + ADR-0093 require both to live in the HTTP request scope so the tenant scope is shared. The cross-tenant probe (`CrossTenantWriteTests.cs:42-44`) resolves both manually but doesn't assert *scoped* lifetime.
- **Test that should exist:** Architecture-level assertion in `IModuleRules` (or a new `DiLifetimeRules`) that `ICurrentUser` and every `IModule.DbContextType` are registered as `Scoped` (not `Singleton`/`Transient`). Reflect on the `IServiceCollection` after `RegisterServices` runs against a stub config.

---

## What looks good

1. **`CrossTenantWriteTests.cs:46-58` is the strongest test in the slice.** Drives the handler directly, populates `ITenantContext` to one tenant, calls `Handle` with a payload that has no tenant field — and asserts the persisted row's `TenantId` matches the scope, not anything else. This is the regression net for ADR-0090's single-source rule and will catch any future drift in handler signatures (e.g., a developer adding a `TenantHint` parameter).

2. **`GET /catalog/applications/{id}` returns 404, not 403, for cross-tenant rows** (`CatalogEndpointDelegates.cs:56-63` + `RegisterApplicationTests.cs:139-154`). Pinned by an explicit two-tenant integration test. Preserves the no-information-leak semantics RLS promises — a 403 would betray that the row exists.

3. **`DomainValidationExceptionHandler.cs:36-38` returns `false` for non-`ArgumentException`.** Lets the global handler chain emit 500 for unhandled exceptions without this handler eating them. The pinning test at `DomainValidationExceptionHandlerTests.cs:44-53` exists. Cleanly resolves spec §13.3 in-slice rather than deferring it.

4. **`MapInboundClaims = false` is pinned** (`tests/Kartova.SharedKernel.AspNetCore.Tests/JwtAuthenticationExtensionsTests.cs:204` against `JwtAuthenticationExtensions.cs:49`). Closes spec §13.4 — `HttpContextCurrentUser`'s reading of literal `"sub"` is now a deliberate, tested contract rather than a load-bearing accident.

5. **`IModuleEndpoints` split out from `IModule`** (`src/Kartova.SharedKernel.AspNetCore/IModuleEndpoints.cs:10`). Spec §5.1 originally put `MapEndpoints` on `IModule` itself, which would have forced `Kartova.Migrator` (DDL only) to depend on `Microsoft.AspNetCore.Routing`. The two-interface split keeps the migrator surface clean. Captured indirectly by commit `8aa06b8`.

---

## Notes for the merge step (if combined with another reviewer)

Stale-comments / spec-drift findings (Should-fix #2, #3, #5, Nit #1) are all "doc says X, code says Y, code is right" — they should be rolled up as a single spec-update follow-up commit, not five separate fixes. ADR-0093-on-master is the highest-leverage Should-fix because it changes the post-merge reviewer experience.
