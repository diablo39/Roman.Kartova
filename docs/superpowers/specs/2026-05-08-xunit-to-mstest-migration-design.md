# xUnit → MSTest v4 + Microsoft.Testing.Platform migration

**Date:** 2026-05-08
**Status:** Draft (awaiting plan)
**Owner:** Roman Głogowski
**Slice scope:** Test framework + runner + assertion library swap across all `tests/` projects, plus superseding ADR.

## 1. Goals & non-goals

### Goals

1. Replace **xUnit 2.9.3** with **MSTest v4** across all five `tests/` projects.
2. Adopt **`MSTest.Sdk`** + **Microsoft.Testing.Platform (MTP)** as the runner, dropping VSTest.
3. Replace **FluentAssertions 6.12.0** with **MSTest v4 native assertions** (`Assert`, `CollectionAssert`, `StringAssert`, `Assert.ThrowsExactly`).
4. Keep NSubstitute, Testcontainers (Postgres + Keycloak), and NetArchTest unchanged — all are framework-agnostic.
5. **Supersede ADR-0083** with a new ADR documenting the framework + runner change. Five-tier pyramid (architecture / unit / integration / contract / E2E) is unchanged.
6. Land via phased delivery — **Phase 0 (tooling/ADR/CI) + Phases 1–5 (one project at a time)** — each phase mergeable on its own.
7. Translate test count and behavior **1:1**. No new tests, no removed coverage. Mutation score must match the pre-migration baseline ±1 percentage point.

### Non-goals

- Not changing test taxonomy (still arch / unit / integration / contract / E2E per ADR-0083 successor).
- Not introducing TUnit, NUnit, or xUnit.v3 as alternatives.
- Not touching contract (Pact — not yet implemented) or E2E (Playwright, JS-based) tests.
- Not introducing parallelization changes — preserve current per-class isolation behavior.
- Not refactoring tests for "MSTest-idiomatic" style beyond what migration mechanically requires.
- Not migrating mutation testing tool — Stryker.NET stays; only its config compatibility with MTP is verified.
- Not mass-renaming test files or methods.

## 2. Phase 0 — tooling, ADR, CI (no test code rewritten)

After Phase 0 lands, the existing xUnit suite still runs. Plumbing only.

### 2.1 Scope

1. **`Directory.Packages.props` updates** — add MSTest v4 packages alongside existing xUnit packages:
   - `MSTest` (v4 meta-package) or split: `MSTest.TestFramework`, `MSTest.TestAdapter`, `MSTest.Analyzers`
   - `MSTest.Sdk` (4.x)
   - `Microsoft.Testing.Extensions.CodeCoverage` — replaces `coverlet.collector` for migrated projects
   - **Keep** `xunit`, `xunit.runner.visualstudio`, `xunit.extensibility.core`, `Microsoft.NET.Test.Sdk`, `coverlet.collector`, `FluentAssertions` until Phase 5 cleanup.

2. **Root `Directory.Build.props`** — create one (currently absent) for cross-cutting test settings. Sets the convention; per-project `<EnableMSTestRunner>true</EnableMSTestRunner>` is opt-in per phase.

3. **`global.json`** — verify SDK pin allows `MSTest.Sdk` 4.x.

4. **CI updates** — `dotnet test` continues to drive both runners. For migrated projects, document MTP exit-code policy (exit code 8 = "no tests"; use `--ignore-exit-code` only with justification).

5. **Stryker config check** — verify `stryker-config.json` works against MTP-driven test runs (`migrate-vstest-to-mtp` skill flags this as a known risk area).

6. **Mutation testing baseline** — run `mutation-sentinel` against the current xUnit suite, capture baseline mutation score per project. Baseline is the regression yardstick: post-migration must match within ±1pt per project.

7. **`BeEquivalentTo` audit** — count `FluentAssertions.BeEquivalentTo(...)` call sites in the existing tests. If > ~15, revisit assertion-library choice (escape hatch: AwesomeAssertions for affected files only).

8. **New ADR — `ADR-NNNN-mstest-and-mtp-supersedes-xunit.md`** — Michael Nygard template. Supersedes ADR-0083.

9. **Update `CLAUDE.md` testing bullet** + ADR keyword index in `docs/architecture/decisions/README.md`. Update ADR-0083 status to `Superseded by ADR-NNNN`.

### 2.2 Phase 0 exit criteria

- Solution still builds with `TreatWarningsAsErrors=true`, 0 warnings.
- All xUnit tests still pass under existing runner.
- New ADR merged.
- Mutation baseline captured (stored in `docs/superpowers/specs/baselines/mstest-migration-mutation-baseline.md` or equivalent).
- CI green on `master` after Phase 0 PR merges.

## 3. Phases 1–5 — per-project migration

Each phase: rewrite that project's test files xUnit → MSTest, replace FluentAssertions with native asserts, build green, tests green, mutation score within baseline ±1pt, full DoD invoked at slice boundary, merge.

### Phase 1 — `Kartova.SharedKernel.Tests` (sets the patterns)

- 6 test files, ~82 attribute uses (CursorCodec, QueryablePaging, SortSpec, CursorFilterMismatch, KartovaConnectionStrings, TenantContextAccessor, TenantScopeWolverineMiddleware).
- Pure unit tests. No Testcontainers fixtures.
- One `IAsyncLifetime` site (`QueryablePagingExtensionsTests`) — translate to `[TestInitialize]`/`[TestCleanup]` async.
- **Output: canonical "what an MSTest file looks like in this repo" pattern.** All subsequent phases follow this style.

### Phase 2 — `Kartova.SharedKernel.AspNetCore.Tests`

- 12 test files, ~79 attribute uses. Endpoint filters, exception handlers, JWT auth wiring.
- Heavy NSubstitute usage — unchanged.
- A few `IAsyncLifetime` sites (none container-backed) — same pattern as Phase 1.

### Phase 3 — `Kartova.ArchitectureTests` (most `[MemberData]`/`[InlineData]` translation)

- 13 files, ~46 attribute uses, mostly `[Theory]` + `[InlineData]`.
- Translation rules applied uniformly per Section 4.
- NetArchTest doesn't care which framework drives it.

### Phase 4 — `Kartova.Testing.Auth` (test infra — keystone)

- Not a test project. Holds `KartovaApiFixtureBase`, `PostgresTestBootstrap`, `TestJwtSigner`, `SeededOrgs`.
- `KartovaApiFixtureBase` rewrite is the keystone. See Section 5.3.
- **No consumers in the test tree currently** — Phase 4 PR is a contract-only change. Build-green is the verification bar.
- After Phase 4, `Kartova.Testing.Auth.csproj` has zero test-framework dependency (drop `xunit.extensibility.core`).

### Phase 5 — `Kartova.Api.IntegrationTests` (hardest) + cleanup

- 4 test files + `KeycloakContainerFixture` + `KeycloakTestCollection`.
- Container fixture migration per Section 5.1.
- `[assembly: DoNotParallelize]` to preserve env-var-race protection currently provided by `[Collection]` (see Section 5.4).
- **Phase 5 cleanup (same PR):**
  - Remove `xunit`, `xunit.runner.visualstudio`, `xunit.extensibility.core` from `Directory.Packages.props`.
  - Remove `FluentAssertions` from `Directory.Packages.props`.
  - Remove `coverlet.collector` if MTP code coverage is fully wired.
  - Verify `Microsoft.NET.Test.Sdk` removal is safe under MTP-only setup.

### Phase ordering rationale

Pure-unit projects first (1, 2) → patterns established without container/fixture noise. Architecture (3) → `[Theory]/[DataRow]/[DynamicData]` translation at scale. Test infra (4) before integration (5). Integration (5) last → benefits from every pattern established earlier.

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

Test classes consume containers through `IntegrationTestAssemblySetup.Containers`. **Delete `KeycloakTestCollection.cs`** in Phase 5.

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

### 5.3 `KartovaApiFixtureBase` (Phase 4)

Drop `IAsyncLifetime`. Keep `WebApplicationFactory<Program>` inheritance.

```csharp
public abstract class KartovaApiFixtureBase : WebApplicationFactory<Program>, IAsyncDisposable
{
    public async Task InitializeAsync() { /* current body */ }
    public new async ValueTask DisposeAsync() { /* current body + base.DisposeAsync() */ }
    // ... rest unchanged
}
```

Future consumer pattern (documented in XML doc on `KartovaApiFixtureBase`):

```csharp
[TestClass]
public abstract class CatalogIntegrationTestBase
{
    protected static MyFixture Fx { get; private set; } = null!;

    [ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]
    public static async Task ClassInit(TestContext _)
    {
        Fx = new MyFixture();
        await Fx.InitializeAsync();
    }

    [ClassCleanup(InheritanceBehavior.BeforeEachDerivedClass)]
    public static async Task ClassDone() => await Fx.DisposeAsync();
}
```

`[ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]` is the v4 idiom for "run for every concrete subclass of an abstract test base" — the semantic equivalent of `IClassFixture<T>`.

### 5.4 Parallelism / serialization (subtle correctness risk)

xUnit's `[Collection]` on `KeycloakTestCollection` does double duty: shares the fixture instance **and** serializes execution across the collection. Comment in `KeycloakTestCollection.cs` explicitly cites the env-var race protection.

MSTest's defaults: methods within a class run sequentially, but classes can run in parallel (assembly-level setting). **Decision: `[assembly: DoNotParallelize]` in `Kartova.Api.IntegrationTests/Properties/AssemblyInfo.cs`** to preserve current behavior. Integration tests are slow and shared-state-y; serial execution is honest.

### 5.5 `Kartova.Testing.Auth` xUnit reference removal

`KartovaApiFixtureBase.cs` currently has `using Xunit;` for `IAsyncLifetime`. Drop the using when we drop the interface. The `.csproj` references `xunit.extensibility.core` — drop in Phase 4. After Phase 4, this project has zero test-framework dependency, the right shape for an infra project.

### 5.6 Items confirmed at execution time (not now)

- `BeEquivalentTo` count audit (Phase 0 sub-task 2.1.7).
- Whether `coverlet.collector` can be fully replaced by `Microsoft.Testing.Extensions.CodeCoverage` without breaking `coverage-auditor` and `mutation-sentinel` skills.
- Whether Stryker.NET works against MTP in this repo without config changes (Phase 0 sub-task 2.1.5).

## 6. ADR strategy

ADR-0083 currently states xUnit + FluentAssertions for unit and integration tiers. **Supersede with a new ADR** rather than amending in place (per repo Nygard convention — ADRs are append-only after `Accepted`).

### 6.1 New ADR

- File: `docs/architecture/decisions/ADR-NNNN-mstest-and-mtp-supersedes-xunit.md` (next free integer at write time).
- Title: *"MSTest v4 + Microsoft.Testing.Platform supersedes xUnit"*.
- Context: first-party tooling alignment, MTP adoption, MSTest skill coverage, FluentAssertions licence trajectory.
- Decision: framework = MSTest v4; runner = Microsoft.Testing.Platform via `MSTest.Sdk`; assertions = MSTest v4 native; mocking = NSubstitute (unchanged); containers = Testcontainers (unchanged); arch = NetArchTest (unchanged).
- Consequences: migration cost paid in this slice; FluentAssertions removed; mutation score and behavior preserved 1:1.
- Status: `Accepted` once Phase 0 PR merges.

### 6.2 Other docs touched in Phase 0

- `docs/architecture/decisions/README.md` — add new ADR to keyword index ("test framework", "MSTest", "MTP", "Microsoft.Testing.Platform"); update ADR-0083 row to `Superseded by ADR-NNNN`.
- ADR-0083 file itself — update status line to `Superseded by ADR-NNNN`. No body edits.
- `CLAUDE.md` testing bullet — change `xUnit` → `MSTest v4 + Microsoft.Testing.Platform`. Five-tier pyramid wording stays.

## 7. Definition of Done per phase + risks/rollback

### 7.1 Per-phase Definition of Done

| DoD point | Phase 0 (tooling) | Phases 1–4 (per project) | Phase 5 (integration + cleanup) |
|---|---|---|---|
| 1. Full build, `TreatWarningsAsErrors=true` | yes | yes | yes |
| 2. Per-task subagent reviews (spec + code-quality) | yes | yes | yes |
| 3. `requesting-code-review` at slice boundary | yes | yes | yes |
| 4. Test suite green | xUnit suite still green | mixed: this phase's project on MSTest, others still xUnit | all-MSTest, full suite |
| 5. Real HTTP happy + negative path | n/a | n/a (Phases 1–3 unit/arch; Phase 4 has no consumers) | **mandatory** — `docker compose up` + `AuthSmokeTests` + one negative path |
| 6. `/simplify` on diff | yes | yes | yes |
| 7. Mutation sentinel (≥80%, baseline ±1pt) | establish baseline | per-project run, must match baseline | full-suite run, must match baseline |
| 8. `/pr-review-toolkit:review-pr` | yes | yes | yes |
| 9. `/deep-review` | yes | yes | yes |

Phase 0 has slightly relaxed DoD — no test code changes → mutation baseline is the deliverable, not a regression check.

### 7.2 Risks and mitigations

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Env-var race regression if MSTest parallel defaults differ from xUnit collections | Med | High | `[assembly: DoNotParallelize]` in integration project; documented in AssemblyInfo comment |
| Mutation score regression post-migration | Med | Med | Phase 0 baseline; per-phase ±1pt gate; investigate any drop > 1pt before merging that phase |
| `BeEquivalentTo` deep-equality loss breaks tests silently | Med | Med | Phase 0 audit; if > 15 sites, escape hatch to AwesomeAssertions for that subset |
| MTP exit-code semantics break CI | Low | High | Phase 0 includes CI dry-run with `--ignore-exit-code` policy documented; `migrate-vstest-to-mtp` skill drives the conversion |
| Stryker.NET breaks under MTP | Med | High | Phase 0 verification; if broken, hold migration on VSTest one phase longer |
| `KartovaApiFixtureBase` future consumers expect xUnit shape | Low | Low | No consumers exist yet; XML doc the new contract; sample consumer in file header |
| Phase 4 lands in isolation with no test coverage | Low | Low | Phase 4 PR has no test-runtime changes; build green is sufficient. Optional: fold Phase 4 into Phase 5 |
| `[ClassInitialize]` inheritance quirks for future module fixtures | Low | Med | Document `InheritanceBehavior.BeforeEachDerivedClass` requirement |

### 7.3 Rollback strategy

Each phase merges as its own PR. Rollback = revert that PR.

**Mid-migration state is safe.** During Phases 1–4 the test suite has mixed frameworks running side-by-side: MSTest projects use `MSTest.Sdk` + MTP; xUnit projects use `Microsoft.NET.Sdk` + VSTest. Both are invoked by `dotnet test` at the solution level — the documented `MSTest.Sdk` + xUnit coexistence pattern.

**Phase 4 + Phase 5 atomicity.** Phase 4 leaves `Kartova.Testing.Auth` with a new shape and no consumers. Recommend keeping Phases 4 and 5 separate (smaller diffs, clearer review). Folding them is acceptable if review burden is the larger concern.

### 7.4 Out-of-scope items explicitly punted

- Mutation testing tool replacement — Stryker.NET stays.
- Coverage tooling redesign — `Microsoft.Testing.Extensions.CodeCoverage` adoption is in scope; `coverage-auditor` skill compatibility is verified, not redesigned.
- Test parallelism tuning — preserve current behavior.
- Test naming conventions — `*Tests.cs` files keep their names.
- `[TestProperty]` / `[TestCategory]` for filtering — not needed; future improvement.

## 8. Skills referenced during execution

- `dotnet-test:writing-mstest-tests` — MSTest v4 idioms for new tests + concrete fixes.
- `dotnet-test:migrate-vstest-to-mtp` — VSTest → Microsoft.Testing.Platform conversion.
- `dotnet-test:mtp-hot-reload` — fast iteration on test fixes.
- `dotnet-test:test-anti-patterns` — sanity check on translated assertions.
- `misc:mutation-sentinel` — baseline + per-phase regression check.
- `misc:test-generator` — only if mutation gaps appear post-translation (should not, since translation is 1:1).

## 9. Out-of-spec changes the plan must not introduce

- New tests beyond what is needed to translate existing tests.
- Changes to production code other than the minimal additions to `KartovaApiFixtureBase` (new method names) needed to drop `IAsyncLifetime`.
- Renaming or restructuring `tests/` projects.
- Any change to mutation-testing config beyond verifying compatibility.
