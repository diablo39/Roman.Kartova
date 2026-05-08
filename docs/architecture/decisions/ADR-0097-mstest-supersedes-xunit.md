# ADR-0097: MSTest v4 supersedes xUnit

**Status:** Accepted
**Date:** 2026-05-08
**Deciders:** Roman Głogowski (solo developer)
**Category:** Testing & Quality
**Supersedes:** ADR-0083 (Testing strategy with architecture tests)
**Related:** ADR-0028 (Clean Architecture), ADR-0080 (Wolverine mediator), ADR-0082 (Modular monolith), ADR-0084 (Playwright MCP for dev-time verification), ADR-0095 (Cursor pagination contract)

## Context

ADR-0083 picked xUnit + FluentAssertions for unit and integration tiers. Two forces have shifted since:

1. **First-party tooling alignment.** MSTest v4 ships with stronger Visual Studio / Rider integration, MSTest analyzers (`MSTest.Analyzers`), and is maintained directly by the .NET team. Aligning with the Microsoft-maintained stack reduces drift and matches the dominant skill ecosystem in this Claude Code installation (`writing-mstest-tests`, `migrate-mstest-v3-to-v4`, `test-anti-patterns` framework-aware) which has dedicated MSTest coverage but only ad-hoc xUnit coverage.
2. **FluentAssertions licence trajectory.** FluentAssertions 8+ moved to a commercial licence; the repo is pinned at 6.12.0. MSTest v4 native assertions (`Assert.AreEqual`, `CollectionAssert`, `StringAssert`, `Assert.ThrowsExactly`) have closed most of the readability gap that motivated FA originally, removing a third-party dependency from every test project.

## Decision

Across **all xUnit-using test projects in the repository** (under `tests/` and `src/Modules/**/*Tests*`):

| Layer | Choice | Notes |
|---|---|---|
| **Test framework** | MSTest v4 | `MSTest.TestFramework`, `MSTest.TestAdapter`, `MSTest.Analyzers` |
| **Project SDK** | `Microsoft.NET.Sdk` | Unchanged from ADR-0083 |
| **Test runner** | VSTest | Unchanged from ADR-0083; MTP deferred (see Note below) |
| **Assertions** | MSTest v4 native | `Assert`, `CollectionAssert`, `StringAssert`, `Assert.ThrowsExactly` |
| **Mocking** | NSubstitute | Unchanged from ADR-0083 |
| **Containers** | Testcontainers (Postgres, Keycloak) | Unchanged from ADR-0083 |
| **Architecture** | NetArchTest | Unchanged from ADR-0083 |
| **Code coverage** | `coverlet.collector` | Unchanged from ADR-0083 |
| **Mutation testing** | Stryker.NET (per-module orchestration via `mutation-targets.json`) | Unchanged from ADR-0083 |
| **Five-tier pyramid** | architecture / unit / integration / contract / E2E | Unchanged from ADR-0083 |

`KartovaApiFixtureBase` (in `tests/Kartova.Testing.Auth`) drops `IAsyncLifetime` and exposes `Task InitializeAsync()` + `IAsyncDisposable.DisposeAsync()` for MSTest consumers. Per-module integration test fixtures (`Kartova.Catalog.IntegrationTests`, `Kartova.Organization.IntegrationTests`) adopt the `[ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]` pattern (semantic equivalent of xUnit's `IClassFixture<T>`). The top-level `Kartova.Api.IntegrationTests` project's shared `KeycloakContainerFixture` becomes an assembly-scoped singleton via `[AssemblyInitialize]`. Every integration assembly carries `[assembly: DoNotParallelize]` to preserve env-var-race protection that xUnit's `[Collection]` previously provided.

### Note: Microsoft.Testing.Platform (MTP) deferred

Originally this ADR also adopted `MSTest.Sdk` + Microsoft.Testing.Platform as the runner. A Phase 0 compatibility probe (recorded in `docs/superpowers/specs/baselines/2026-05-08-mstest-migration-mutation-baseline.md`, §"Stryker × MTP compatibility probe") found that **Stryker.NET 4.14.1 does not support MTP** — tracked upstream as [stryker-mutator/stryker-net#3094](https://github.com/stryker-mutator/stryker-net/issues/3094). Since Stryker is a critical part of the project's testing discipline (mutation gate at ≥80% per Definition of Done), the runner switch is deferred. All test projects stay on `Microsoft.NET.Sdk` + VSTest with `Microsoft.NET.Test.Sdk` and `coverlet.collector`. Revisit MTP adoption in a future migration once Stryker support lands.

## Consequences

**Positive:**
- One-stop test framework: framework + analyzers maintained by Microsoft.
- `MSTest.Analyzers` catches mistakes that xUnit's looser convention previously let through (e.g., misnamed `[ClassInitialize]` signatures).
- One fewer third-party dependency (FluentAssertions) per test project.
- Skill ecosystem alignment for AI-assisted test work.

**Negative:**
- One-time migration cost paid in the slice that introduces this ADR (~64 files across 10 projects).
- Lose xUnit's per-test class-instance isolation; test classes now reuse one instance across `[TestMethod]` invocations within a class. Field initializers behave identically; constructor side-effects are migrated to `[TestInitialize]`.
- Lose FluentAssertions' deep-object-graph `BeEquivalentTo`. Per-property `Assert.AreEqual` is the replacement; if site count exceeds tolerable repetition, AwesomeAssertions is the documented escape hatch (community fork of FA, MIT-licensed).
- `[assembly: DoNotParallelize]` is more conservative than the per-collection serialization xUnit used; integration test wall-time may increase modestly.
- MTP benefits (hot reload, faster CI, cleaner exit codes) deferred indefinitely until Stryker support lands.

**Neutral:**
- Test taxonomy and CI gates (architecture-tests-must-pass) unchanged from ADR-0083.
- NSubstitute and Testcontainers usage patterns are framework-agnostic and unaffected.
- Project SDK, test runner, code coverage tooling, and mutation testing configuration all stay on the same stack as before — the migration is a framework-and-assertion swap only.

## Migration

Tracked in spec `docs/superpowers/specs/2026-05-08-xunit-to-mstest-migration-design.md` and plan `docs/superpowers/plans/2026-05-08-xunit-to-mstest-migration-plan.md`. 13 phases (Phase 0 + Phases 1–12).
