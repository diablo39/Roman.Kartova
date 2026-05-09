# xUnit → MSTest v4 migration

**Date:** 2026-05-08
**Status:** Draft (awaiting plan)
**Owner:** Roman Głogowski
**Slice scope:** Test framework + assertion library swap across **all xUnit-using test projects in the repository** (both `tests/` and `src/Modules/**/*Tests*`), plus superseding ADR. Runner (VSTest), project SDK (`Microsoft.NET.Sdk`), and code-coverage tooling (`coverlet.collector`) stay unchanged — MTP adoption is deferred (see §1 Non-goals).

## 1. Goals & non-goals

### Goals

1. Replace **xUnit 2.9.3** with **MSTest v4** across **all ten xUnit-using test projects** (5 under `tests/`, 5 under `src/Modules/**`).
2. Replace **FluentAssertions 6.12.0** with **MSTest v4 native assertions** (`Assert`, `CollectionAssert`, `StringAssert`, `Assert.ThrowsExactly`).
3. Keep NSubstitute, Testcontainers (Postgres + Keycloak), and NetArchTest unchanged — all are framework-agnostic.
4. **Supersede ADR-0083** with a new ADR documenting the framework + assertion change. Five-tier pyramid (architecture / unit / integration / contract / E2E) is unchanged.
5. Land via phased delivery — **Phase 0 (tooling/ADR) + Phases 1–11 (per-project migration) + Phase 12 (cleanup)** — each phase mergeable on its own.
6. Translate test count and behavior **1:1**. No new tests, no removed coverage. Per-project mutation scores (per `mutation-targets.json` orchestration; mapping in baseline-doc §"Per-phase mutation-gate ownership") must match the pre-migration baseline ±1 percentage point. Degenerate-baseline projects (n/a or tiny denominators) follow the absolute-survivor-count rule documented in the baseline doc Notes.

### Non-goals

- Not changing test taxonomy (still arch / unit / integration / contract / E2E per ADR-0083 successor).
- Not introducing TUnit, NUnit, or xUnit.v3 as alternatives.
- Not touching contract (Pact — not yet implemented) or E2E (Playwright, JS-based) tests.
- Not introducing parallelization changes — preserve current per-class isolation behavior.
- Not refactoring tests for "MSTest-idiomatic" style beyond what migration mechanically requires.
- Not migrating mutation testing tool — Stryker.NET stays.
- Not mass-renaming test files or methods.
- Not adopting Microsoft.Testing.Platform (MTP) — Stryker.NET does not support it as of Phase 0 (stryker-net#3094; baseline-doc records the exact version probed); deferred to a future migration once Stryker support lands.

## 2. Phase 0 — tooling, ADR, CI (no test code rewritten)

After Phase 0 lands, the existing xUnit suite still runs. Plumbing only.

### 2.1 Scope

1. **`Directory.Packages.props` updates** — add MSTest v4 packages alongside existing xUnit packages:
   - `MSTest.TestFramework`, `MSTest.TestAdapter`, `MSTest.Analyzers`
   - **Keep** `xunit`, `xunit.runner.visualstudio`, `xunit.extensibility.core`, `Microsoft.NET.Test.Sdk`, `coverlet.collector`, `FluentAssertions` until Phase 12 cleanup.

2. **Root `Directory.Build.props`** — create one (currently absent) for cross-cutting test settings.

3. **`global.json`** — verify SDK pin is unchanged (no SDK band changes required since the runner stays on VSTest).

4. **CI updates** — current `dotnet test` invocations continue to work unchanged; no CI changes required.

5. **Stryker × MTP probe** — verify whether Stryker.NET supports Microsoft.Testing.Platform. (Result recorded in the baseline doc: FAIL — stryker-net#3094 — so MTP is dropped from this migration's scope.)

6. **Mutation testing baseline** — run `mutation-sentinel` against the current xUnit suite, capture baseline mutation score per project. Baseline is the regression yardstick: post-migration must match within ±1pt per project.

7. **`BeEquivalentTo` audit** — count `FluentAssertions.BeEquivalentTo(...)` call sites in the existing tests. If > ~15, revisit assertion-library choice (escape hatch: AwesomeAssertions for affected files only).

8. **New ADR — `ADR-NNNN-mstest-supersedes-xunit.md`** — Michael Nygard template. Supersedes ADR-0083.

9. **Update `CLAUDE.md` testing bullet** + ADR keyword index in `docs/architecture/decisions/README.md`. Update ADR-0083 status to `Superseded by ADR-NNNN`.

### 2.2 Phase 0 exit criteria

- Solution still builds with `TreatWarningsAsErrors=true`, 0 warnings.
- All xUnit tests still pass under existing runner.
- New ADR merged.
- Mutation baseline captured (stored in `docs/superpowers/specs/baselines/mstest-migration-mutation-baseline.md` or equivalent).
- CI green on `master` after Phase 0 PR merges.

## 3. Phases 1–12 — per-project migration

Each phase: rewrite that project's test files xUnit → MSTest, replace FluentAssertions with native asserts, build green, tests green, mutation score within baseline ±1pt (where Stryker mutates this project), full DoD invoked at slice boundary, merge.

**Per-project mechanic for build-green incrementalism:** keep the project on `Microsoft.NET.Sdk` and add MSTest packages alongside the existing xUnit packages while files are translated one at a time. xUnit and MSTest test classes coexist within a single assembly during the translation window (xUnit discovers `[Fact]`, MSTest discovers `[TestMethod]`). After the last file is translated, drop xUnit references from the project. Project SDK stays on `Microsoft.NET.Sdk` throughout — no `MSTest.Sdk` flip in Phase 12 (MTP deferred).

### Phase 1 — `tests/Kartova.SharedKernel.Tests` (sets the patterns)

- ~7 test files, ~82 attribute uses (CursorCodec, QueryablePaging, SortSpec, CursorFilterMismatch, KartovaConnectionStrings, TenantContextAccessor, TenantScopeWolverineMiddleware).
- Pure unit tests. No Testcontainers fixtures.
- One `IAsyncLifetime` site (`QueryablePagingExtensionsTests`) — translate to `[TestInitialize]`/`[TestCleanup]` async.
- **Output: canonical "what an MSTest file looks like in this repo" pattern.** All subsequent phases follow this style.

### Phase 2 — `tests/Kartova.SharedKernel.AspNetCore.Tests`

- 12 test files, ~79 attribute uses. Endpoint filters, exception handlers, JWT auth wiring.
- Heavy NSubstitute usage — unchanged.
- A few `IAsyncLifetime` sites (none container-backed) — same pattern as Phase 1.

### Phase 3 — `tests/Kartova.ArchitectureTests` (most `[MemberData]`/`[InlineData]` translation)

- 13 files, ~46 attribute uses, mostly `[Theory]` + `[InlineData]`.
- Translation rules applied uniformly per Section 4.
- NetArchTest doesn't care which framework drives it.

### Phase 4 — `src/Modules/Catalog/Kartova.Catalog.Tests` (module unit tests)

- ~7 test files (`ApplicationTests`, `ApplicationLifecycleTests`, `CatalogAssemblyLoadsTests`, `EfApplicationConfigurationTests`, `InvalidLifecycleTransitionExceptionTests`, `ListApplicationsHandlerFilterTests`, `ListApplicationsHandlerTests`).
- **Stryker target.** Mutation score change > ±1pt blocks the phase.
- Pure unit tests; same pattern as Phase 1.

### Phase 5 — `src/Modules/Organization/Kartova.Organization.Tests` (module unit tests)

- ~1 test file (`OrganizationAggregateTests`).
- **Stryker target.** Mutation score gate as Phase 4.
- Pure unit tests.

### Phase 6 — `src/Modules/Catalog/Kartova.Catalog.Infrastructure.Tests`

- ~1 test file (`CatalogModuleRegisterForMigratorTests`).
- Pure unit tests; trivially small.

### Phase 7 — `src/Modules/Organization/Kartova.Organization.Infrastructure.Tests`

- ~1 test file (`OrganizationModuleRegisterForMigratorTests`).
- Pure unit tests; trivially small.

### Phase 8 — `tests/Kartova.Testing.Auth` (test infra — additive contract change)

- Not a test project. Holds `KartovaApiFixtureBase`, `PostgresTestBootstrap`, `TestJwtSigner`, `SeededOrgs`.
- **`KartovaApiFixtureBase` HAS consumers**: `Kartova.Catalog.IntegrationTests/KartovaApiFixture` and `Kartova.Organization.IntegrationTests/KartovaApiFixture` both inherit from it; `KartovaApiCollection` in each module integration project uses `ICollectionFixture<KartovaApiFixture>`.
- **Strategy: additive change, not replacement.** In Phase 8, `KartovaApiFixtureBase`:
  - Keeps `IAsyncLifetime` interface (consumers still rely on it).
  - Adds `IAsyncDisposable` interface (alongside `IAsyncLifetime`).
  - The existing `InitializeAsync()` / `DisposeAsync()` method bodies are unchanged.
  - Drop `using Xunit;` only if `IAsyncLifetime` can be referenced via fully-qualified name; otherwise keep the using until Phase 12.
- After Phase 8, both old (xUnit `IClassFixture`/`ICollectionFixture` consumption) and new (MSTest `[ClassInitialize]` consumption with `await Fx.InitializeAsync(); await Fx.DisposeAsync();`) patterns work.
- `Kartova.Testing.Auth.csproj` retains `xunit.extensibility.core` reference until Phase 12.

### Phase 9 — `src/Modules/Catalog/Kartova.Catalog.IntegrationTests`

- ~9 test files + `KartovaApiFixture` (derives from `KartovaApiFixtureBase`) + `KartovaApiCollection` (`ICollectionFixture<KartovaApiFixture>`) + `Fixtures/PostgresFixture : IAsyncLifetime` + `Migrations/MigrationIntegrationTests : IClassFixture<PostgresFixture>`.
- Container-backed integration tests; uses `KartovaApiFixtureBase` contract from Phase 8.
- Migration:
  - Convert `KartovaApiFixture` to MSTest via `[ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]` pattern from Section 5.3, calling `Fx.InitializeAsync()` directly (the additive Phase 8 method).
  - Convert `PostgresFixture` to a plain `IAsyncDisposable` class with an `InitializeAsync()` method, consumed via `[ClassInitialize]` in `MigrationIntegrationTests`.
  - Delete `KartovaApiCollection.cs` (xUnit collection-definition class).
  - Apply `[assembly: DoNotParallelize]` if existing collection serialization is needed.

### Phase 10 — `src/Modules/Organization/Kartova.Organization.IntegrationTests`

- ~9 test files + `KartovaApiFixture` + `KartovaApiCollection` + `KartovaApiFaultInjectionFixture` + `KartovaApiFaultInjectionCollection`.
- Same pattern as Phase 9, with two fixture variants instead of one.
- Test base class `KartovaApiFaultInjectionFixture` migrates the same way as the standard fixture.

### Phase 11 — `tests/Kartova.Api.IntegrationTests`

- 4 test files + `KeycloakContainerFixture` + `KeycloakTestCollection`.
- Container fixture migration per Section 5.1 (assembly-scoped singleton via `[AssemblyInitialize]`).
- `[assembly: DoNotParallelize]` to preserve env-var-race protection currently provided by `[Collection]` (see Section 5.4).

### Phase 12 — Final cleanup

- Drop `IAsyncLifetime` interface from `KartovaApiFixtureBase` (no consumers left after Phases 9–10).
- Drop `using Xunit;` from `KartovaApiFixtureBase.cs`.
- Remove `xunit`, `xunit.runner.visualstudio`, `xunit.extensibility.core` from `Directory.Packages.props`.
- Remove `FluentAssertions` from `Directory.Packages.props`.
- Drop `xunit.extensibility.core` reference from `Kartova.Testing.Auth.csproj`.
- Run full mutation suite; all module test projects within ±1pt of Phase 0 baseline.

**Project SDK / runner / coverage tooling all stay on the same stack as Phase 0** (Microsoft.NET.Sdk + VSTest + Microsoft.NET.Test.Sdk + coverlet.collector). MTP adoption is out of scope — see §1 Non-goals.

### Phase ordering rationale

Pure-unit projects first (1, 2, 4–7) → patterns established without container/fixture noise. Architecture (3) → `[Theory]/[DataRow]/[DynamicData]` translation at scale. Test infra (8) is **additive**, so it doesn't break the still-on-xUnit consumers. Module integration tests (9, 10) consume the new contract. Top-level integration tests (11) come last because they're independent. Phase 12 is the cleanup that needs all consumers off the xUnit `IAsyncLifetime` shape.

## 4. Translation rules (xUnit → MSTest v4 + native asserts)

### 4.1 Test discovery & lifecycle

| xUnit | MSTest v4 | Notes |
|---|---|---|
| Test class (no attribute) | `[TestClass]` on the class | Required. Public, non-static. |
| `[Fact]` | `[TestMethod]` | Direct swap. |
| `[Fact(Skip = "reason")]` | `[TestMethod] [Ignore("reason")]` | Skip moves off the method attribute. |
| `[Fact(DisplayName = "x")]` | `[TestMethod("x")]` | v4 supports display name. |
| `[Fact(Timeout = X)]` | `[TestMethod] [Timeout(X)]` | — |
| Constructor | `[TestInitialize] public void Setup()` | xUnit per-test ctor → MSTest reuses class instance + per-test `[TestInitialize]`. Field initializers behave identically; constructor side-effects → migrate to `[TestInitialize]`. |
| `IDisposable.Dispose()` / `IAsyncDisposable.DisposeAsync()` | `[TestCleanup] public void Cleanup()` / `async Task Cleanup()` | Same per-test cadence. |
| `IAsyncLifetime.InitializeAsync()` | Audit each site: usually `[TestInitialize] async Task InitAsync()`; if truly per-class, `[ClassInitialize] static async Task ClassInit(TestContext _)`. | Map case-by-case. |

### 4.2 Theories & data

| xUnit | MSTest v4 | Notes |
|---|---|---|
| `[Theory] [InlineData(1, "a")]` | `[TestMethod] [DataRow(1, "a")]` | Direct swap. Stack multiple `[DataRow]`. |
| `[Theory] [MemberData(nameof(Cases))]` returning `IEnumerable<object[]>` | `[TestMethod] [DynamicData(nameof(Cases), DynamicDataSourceType.Method)]` | Source method must be `static`. |
| `[Theory] [ClassData(typeof(X))]` | `[TestMethod] [DynamicData(nameof(X.GetData), typeof(X), DynamicDataSourceType.Method)]` | Rare; verify before applying. |

**Decision:** keep `IEnumerable<object[]>` shape for minimum-diff. Opportunistic upgrade to `IEnumerable<(...)>` ValueTuples (per `writing-mstest-tests` skill guidance) only where current code is awkward.

### 4.3 Fixtures & shared state

| xUnit | MSTest v4 | Notes |
|---|---|---|
| `IClassFixture<T>` + ctor injection | `[ClassInitialize] static void Init(TestContext _)` setting a `private static T _fixture` + `[ClassCleanup] static void Clean()` | Lose ctor injection — fixture exposed via static field. |
| `ICollectionFixture<T>` + `[Collection("Name")]` | `[AssemblyInitialize]` on a `TestSetup` class + static accessor consumed by test classes | Granularity widens from collection to assembly. Acceptable when collection already spans the project (our case). |
| `[CollectionDefinition("Name")]` class | Delete; replaced by `[AssemblyInitialize]` setup class. | — |

### 4.4 Output & context

| xUnit | MSTest v4 | Notes |
|---|---|---|
| `ITestOutputHelper` ctor-injected | `public TestContext TestContext { get; set; }` + `TestContext.WriteLine(...)` | MSTest auto-injects `TestContext` into a public settable property named `TestContext`. |
| Assertion failure → `Xunit.Sdk.XunitException` | `Microsoft.VisualStudio.TestTools.UnitTesting.AssertFailedException` | Affects any test that catches assertion exceptions (rare). |

### 4.5 Assertions: FluentAssertions → MSTest v4 native

| FluentAssertions | MSTest v4 native |
|---|---|
| `x.Should().Be(y)` | `Assert.AreEqual(y, x)` |
| `x.Should().NotBe(y)` | `Assert.AreNotEqual(y, x)` |
| `x.Should().BeTrue()` / `BeFalse()` | `Assert.IsTrue(x)` / `Assert.IsFalse(x)` |
| `x.Should().BeNull()` / `NotBeNull()` | `Assert.IsNull(x)` / `Assert.IsNotNull(x)` |
| `x.Should().BeOfType<T>()` | `Assert.IsInstanceOfType<T>(x)` (v4 generic, no `out` param) |
| `x.Should().BeSameAs(y)` | `Assert.AreSame(y, x)` |
| `s.Should().Contain("foo")` | `StringAssert.Contains(s, "foo")` |
| `s.Should().StartWith("foo")` | `StringAssert.StartsWith(s, "foo")` |
| `s.Should().Match("p*r")` | `StringAssert.Matches(s, new Regex(...))` |
| `coll.Should().BeEmpty()` | `Assert.AreEqual(0, coll.Count())` |
| `coll.Should().HaveCount(n)` | `Assert.AreEqual(n, coll.Count())` |
| `coll.Should().Contain(x)` | `CollectionAssert.Contains((ICollection)coll, x)` |
| `coll.Should().BeEquivalentTo(other)` | `CollectionAssert.AreEquivalent(other, coll)` (order-independent) — **deep object-graph equivalence lost**: write per-property `Assert.AreEqual` |
| `act.Should().Throw<T>()` / `ThrowExactly<T>()` | `Assert.ThrowsExactly<T>(act)` (v4) — **always use `ThrowsExactly`**, not `Throws` |
| `act.Should().NotThrow()` | call `act()` directly; if it throws, the test fails |
| `act.Should().ThrowAsync<T>()` | `await Assert.ThrowsExactlyAsync<T>(act)` |
| `task.Should().CompleteWithinAsync(...)` | none direct — `cts.CancelAfter(...)` + assert |

**Deep-equivalence callout:** FA's `BeEquivalentTo` on objects compares by structure. MSTest has no equivalent. Inline per-property `Assert.AreEqual` (or extract a small helper) at each call site. Audit count first (Phase 0 task 2.1.7); if it's > 15, revisit AwesomeAssertions for the affected subset.

## 5. Fixture migration strategy (the hard part)

Current xUnit shape:

- `KeycloakContainerFixture : IAsyncLifetime` — owns one Postgres + one Keycloak container, shared across the integration assembly via `[CollectionDefinition("Keycloak")]` + `[Collection]` on test classes.
- `AuthSmokeTests : IAsyncLifetime` — ctor-injects the fixture, runs seeding + `WebApplicationFactory<Program>` creation **per test** (xUnit creates one class instance per test).
- `KartovaApiFixtureBase : WebApplicationFactory<Program>, IAsyncLifetime` — abstract base in `Kartova.Testing.Auth`, **no test-tree consumers currently** but positioned for future module integration tests.

### 5.1 `KeycloakContainerFixture` → assembly-scoped singleton

Convert to a plain class: drop `IAsyncLifetime`, implement `IAsyncDisposable` (containers need async disposal), expose a `Task InitializeAsync()` method. Add a new `IntegrationTestAssemblySetup` (non-static — MSTest's `[TestClass]` is canonical on a non-static class even when all members are static):

```csharp
[TestClass]
public sealed class IntegrationTestAssemblySetup
{
    public static KeycloakContainerFixture Containers { get; private set; } = null!;

    [AssemblyInitialize]
    public static async Task InitAsync(TestContext _)
    {
        Containers = new KeycloakContainerFixture();
        await Containers.InitializeAsync();
    }

    [AssemblyCleanup]
    public static async Task CleanupAsync()
    {
        if (Containers is not null) await Containers.DisposeAsync();
    }
}
```

Test classes consume containers through `IntegrationTestAssemblySetup.Containers`. **Delete `KeycloakTestCollection.cs`** in Phase 11.

### 5.2 `AuthSmokeTests` (and similar) translation

```csharp
[TestClass]
public sealed class AuthSmokeTests
{
    private static KeycloakContainerFixture Fx => IntegrationTestAssemblySetup.Containers;
    private WebApplicationFactory<Program>? _app;

    [TestInitialize]
    public async Task TestInit() { /* env vars + seed + migrations + factory create */ }

    [TestCleanup]
    public void TestCleanup() => _app?.Dispose();

    [TestMethod]
    public async Task Full_KeyCloak_realm_issues_token_and_API_accepts_it() { /* native MSTest asserts */ }
}
```

Per-test cadence is preserved (xUnit's per-test-instance lifecycle ≡ MSTest's `[TestInitialize]` per test). DB-seed idempotency assumption is unchanged.

### 5.3 `KartovaApiFixtureBase` — additive change in Phase 8, cleanup in Phase 12

`KartovaApiFixtureBase` has **two real consumer fixtures** today: `Kartova.Catalog.IntegrationTests/KartovaApiFixture` and `Kartova.Organization.IntegrationTests/KartovaApiFixture` (plus `KartovaApiFaultInjectionFixture`), each consumed via `ICollectionFixture<...>`. We can't replace `IAsyncLifetime` outright without also flipping those consumer projects in the same atomic operation, which would force one large PR.

**Phase 8 — additive contract change in `KartovaApiFixtureBase`:**

```csharp
public abstract class KartovaApiFixtureBase
    : WebApplicationFactory<Program>, IAsyncLifetime, IAsyncDisposable
{
    // unchanged: existing IAsyncLifetime methods serve current xUnit consumers
    public async Task InitializeAsync() { /* current body */ }
    Task IAsyncLifetime.DisposeAsync() => DisposeAsyncCore();

    // new: IAsyncDisposable.DisposeAsync wraps the same teardown for MSTest consumers
    async ValueTask IAsyncDisposable.DisposeAsync() => await DisposeAsyncCore();

    private async Task DisposeAsyncCore()
    {
        await _pg.DisposeAsync();
        await base.DisposeAsync();
    }
    // ... rest unchanged
}
```

This keeps existing consumers (Phases 9–10 not yet migrated) working through `IAsyncLifetime`, while letting Phase 9–10's MSTest test classes call `Fx.InitializeAsync()` + `await ((IAsyncDisposable)Fx).DisposeAsync()` directly.

**Phase 9 / 10 consumer pattern (MSTest):**

```csharp
[TestClass]
public abstract class CatalogIntegrationTestBase
{
    protected static KartovaApiFixture Fx { get; private set; } = null!;

    [ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]
    public static async Task ClassInit(TestContext _)
    {
        Fx = new KartovaApiFixture();
        await Fx.InitializeAsync();
    }

    [ClassCleanup(InheritanceBehavior.BeforeEachDerivedClass)]
    public static async Task ClassDone() => await ((IAsyncDisposable)Fx).DisposeAsync();
}
```

`[ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]` is the v4 idiom for "run for every concrete subclass of an abstract test base" — semantic equivalent of `IClassFixture<T>`.

**Phase 12 — cleanup:** drop `IAsyncLifetime` from `KartovaApiFixtureBase`, drop `using Xunit;`, drop `xunit.extensibility.core` package reference.

### 5.4 Parallelism / serialization (subtle correctness risk)

xUnit's `[Collection]` on `KeycloakTestCollection` does double duty: shares the fixture instance **and** serializes execution across the collection. Comment in `KeycloakTestCollection.cs` explicitly cites the env-var race protection.

MSTest's defaults: methods within a class run sequentially, but classes can run in parallel (assembly-level setting). **Decision: `[assembly: DoNotParallelize]` in every integration-test assembly** (`Kartova.Api.IntegrationTests`, `Kartova.Catalog.IntegrationTests`, `Kartova.Organization.IntegrationTests`) to preserve current behavior. Integration tests are slow and shared-state-y; serial execution is honest. Unit-test projects (Phases 1–7) keep MSTest's default class-level parallelism since they're framework-pure logic with no shared state.

### 5.5 `Kartova.Testing.Auth` xUnit reference removal

`KartovaApiFixtureBase.cs` currently has `using Xunit;` for `IAsyncLifetime`. The `.csproj` references `xunit.extensibility.core`. Both stay through Phase 8 (still serving xUnit consumers in Phases 9–10) and are dropped in Phase 12 along with the `IAsyncLifetime` interface. After Phase 12, `Kartova.Testing.Auth` has zero test-framework dependency — the right shape for an infra project.

### 5.6 Items confirmed at execution time (not now)

- `BeEquivalentTo` count audit (Phase 0 sub-task 2.1.7).
- Stryker × MTP compatibility — confirmed FAIL on 2026-05-08 (stryker-net#3094); MTP dropped from migration scope, runner stays on VSTest.

## 6. ADR strategy

ADR-0083 currently states xUnit + FluentAssertions for unit and integration tiers. **Supersede with a new ADR** rather than amending in place (per repo Nygard convention — ADRs are append-only after `Accepted`).

### 6.1 New ADR

- File: `docs/architecture/decisions/ADR-NNNN-mstest-supersedes-xunit.md` (next free integer at write time).
- Title: *"MSTest v4 supersedes xUnit"*.
- Context: first-party tooling alignment, MSTest skill coverage, FluentAssertions licence trajectory.
- Decision: framework = MSTest v4; assertions = MSTest v4 native; mocking = NSubstitute (unchanged); containers = Testcontainers (unchanged); arch = NetArchTest (unchanged); runner stays on VSTest (MTP deferred — Stryker incompatibility).
- Consequences: migration cost paid in this slice; FluentAssertions removed; mutation score and behavior preserved 1:1.
- Status: `Accepted` once Phase 0 PR merges.

### 6.2 Other docs touched in Phase 0

- `docs/architecture/decisions/README.md` — add new ADR to keyword index ("test framework", "MSTest"); update ADR-0083 row to `Superseded by ADR-NNNN`.
- ADR-0083 file itself — update status line to `Superseded by ADR-NNNN`. No body edits.
- `CLAUDE.md` testing bullet — change `xUnit` → `MSTest v4`. Five-tier pyramid wording stays.

## 7. Definition of Done per phase + risks/rollback

### 7.1 Per-phase Definition of Done

| DoD point | Phase 0 (tooling) | Phases 1–7 (unit/arch projects) | Phase 8 (Testing.Auth additive) | Phases 9–11 (integration projects) | Phase 12 (cleanup) |
|---|---|---|---|---|---|
| 1. Full build, `TreatWarningsAsErrors=true` | yes | yes | yes | yes | yes |
| 2. Per-task subagent reviews | yes | yes | yes | yes | yes |
| 3. `requesting-code-review` at slice boundary | yes | yes | yes | yes | yes |
| 4. Test suite green | xUnit suite still green | mixed: this project all-MSTest, others still xUnit | xUnit consumers of `KartovaApiFixtureBase` still green | this project all-MSTest, others still on prior state | all-MSTest, full suite |
| 5. Real HTTP happy + negative path | n/a | n/a | n/a (no test runtime changes) | **mandatory** — `docker compose up` + at least one HTTP test from this project + one negative path | not required (cleanup only) |
| 6. `/simplify` on diff | yes | yes | yes | yes | yes |
| 7. Mutation sentinel (≥80%, baseline ±1pt) | establish baseline | gate-owners must match baseline (see baseline-doc §"Per-phase mutation-gate ownership" for the canonical phase-to-target mapping) | n/a (no production-code or test-code semantic changes) | gate-owners must match baseline (see baseline-doc §"Per-phase mutation-gate ownership") | full-suite run, all Stryker targets within ±1pt of baseline |
| 8. `/pr-review-toolkit:review-pr` | yes | yes | yes | yes | yes |
| 9. `/deep-review` | yes | yes | yes | yes | yes |

Phase 0 has slightly relaxed DoD — no test code changes → mutation baseline is the deliverable, not a regression check. The mutation gate applies per-phase based on which test projects drive which production-assembly mutations — see baseline-doc §"Per-phase mutation-gate ownership" for the canonical phase-to-target mapping. Phases without a Stryker-driven mutation target (Phases 3, 6, 7, 8) skip DoD #7.

### 7.2 Risks and mitigations

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Env-var race regression if MSTest parallel defaults differ from xUnit collections | Med | High | `[assembly: DoNotParallelize]` in every integration assembly; documented in AssemblyInfo comment |
| Mutation score regression post-migration in Stryker target projects | Med | Med | Phase 0 baseline; Phases 4, 5, 12 ±1pt gate; investigate any drop > 1pt before merging |
| `BeEquivalentTo` deep-equality loss breaks tests silently | Med | Med | Phase 0 audit (now scoped to all 10 projects); if > 15 sites, escape hatch to AwesomeAssertions for that subset |
| `KartovaApiFixtureBase` consumer breakage during Phase 8 additive change | Med | High | Phase 8 keeps `IAsyncLifetime` interface alongside new `IAsyncDisposable`; consumer projects (Phases 9–10) verify they still compile and pass before their own migration |
| Phase 12 cleanup forgets a transitive xUnit reference | Low | Med | Phase 12 task list explicitly enumerates every CPM entry and every `using Xunit;` site (find via Grep); CI build with `xunit.*` removed from CPM is the gate |
| `[ClassInitialize]` inheritance quirks for module fixtures (Phases 9–10) | Med | Med | Document `InheritanceBehavior.BeforeEachDerivedClass` requirement; verify by writing one consumer in Phase 9 first and checking it runs against derived classes |
| Long migration window keeps mixed-framework state for many PRs | Low | Med | Each phase is small and mergeable; no phase blocks others except by ordering. Mid-migration state is functionally correct (both runners coexist) |

### 7.3 Rollback strategy

Each phase merges as its own PR. Rollback = revert that PR.

**Mid-migration state is safe.** During Phases 1–11 the test suite has mixed frameworks running side-by-side. xUnit and MSTest test classes coexist within the same `Microsoft.NET.Sdk` projects on the VSTest runner — both adapters discover their respective attributes from a single `dotnet test` invocation. Project SDK and runner stay on `Microsoft.NET.Sdk` + VSTest throughout; no Phase 12 flip.

**Phase 8 atomicity.** Phase 8 is purely additive (adds `IAsyncDisposable` alongside `IAsyncLifetime`); no consumer changes. Phases 9 and 10 each migrate one consumer project. Phase 12 removes the `IAsyncLifetime` interface only after both consumers have flipped. This sequence avoids any single PR carrying both contract and consumer changes.

### 7.4 Out-of-scope items explicitly punted

- Mutation testing tool replacement — Stryker.NET stays.
- Coverage tooling redesign — `coverlet.collector` stays unchanged; no replacement in scope.
- Test parallelism tuning — preserve current behavior.
- Test naming conventions — `*Tests.cs` files keep their names.
- `[TestProperty]` / `[TestCategory]` for filtering — not needed; future improvement.

## 8. Skills referenced during execution

- `dotnet-test:writing-mstest-tests` — MSTest v4 idioms for new tests + concrete fixes.
- `dotnet-test:test-anti-patterns` — sanity check on translated assertions.
- `misc:mutation-sentinel` — baseline + per-phase regression check.
- `misc:test-generator` — only if mutation gaps appear post-translation (should not, since translation is 1:1).

## 9. Out-of-spec changes the plan must not introduce

- New tests beyond what is needed to translate existing tests.
- Changes to production code other than the additive contract change in `KartovaApiFixtureBase` (Phase 8 adds `IAsyncDisposable`, Phase 12 removes `IAsyncLifetime`).
- Renaming or restructuring test projects (under `tests/` or `src/Modules/**/*Tests*`).
- Any change to mutation-testing config (`stryker-config.json`) beyond verifying compatibility with MTP.

### 9.1 Exception: production fixes surfaced by migration assertion strictness

When MSTest's strict `Object.Equals` semantics expose a latent production bug that FluentAssertions' silent numeric/string coercion was masking — and the bug genuinely needs fixing for the translated test to pass — the production fix MAY land in the same per-project phase as the test that surfaced it, subject to all of the following gates:

1. The fix is **≤1 production file** with a **≤10-line production-code diff** (excluding comments and tests). Test changes accompanying the fix are not counted toward this limit.
2. The fix is accompanied by a **tightened test** (e.g., `Assert.IsInstanceOfType<T>` plus exact-value `Assert.AreEqual`) that locks in the corrected runtime type or behaviour. Translation shims that merely accommodate the bug (e.g., asserting against `Convert.ToDouble(value)` to match the buggy widening) are explicit deviations and disqualify the exception.
3. The fix has a **clear root-cause comment** at the point of change naming the language rule, the failure mode, and the observable symptom.
4. The fix is in a **separate commit** (not bundled into a translation commit) so `git revert` of the translation work does not unintentionally revert the production fix and vice versa.
5. The phase's slice-boundary code review explicitly calls out the fix as a deviation from this section, and the deviation is acknowledged in the PR body.

If any gate fails, the production fix MUST extract to its own PR ahead of (or alongside) the migration phase PR.

**Precedent:** Phase 1 (commit `19f52dd`) fixed `CursorCodec.UnwrapJsonElement` where C#'s `?:` common-type rule was silently widening `long` to `double`, causing every integer cursor sort-value to decode as `Double` instead of `Int64`. FluentAssertions' numeric coercion was masking this; MSTest's strict `Object.Equals` exposed it. All five gates above were met (1 file, +12/-7 net incl. tests, root-cause comment on the `JsonValueKind.Number` arm of `CursorCodec.UnwrapJsonElement`, separate commit, slice-boundary review acknowledged).
