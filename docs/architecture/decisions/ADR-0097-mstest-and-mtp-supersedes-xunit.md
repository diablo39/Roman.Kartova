# ADR-0097: MSTest v4 + Microsoft.Testing.Platform supersedes xUnit

**Status:** Accepted
**Date:** 2026-05-08
**Deciders:** Roman Głogowski (solo developer)
**Category:** Testing & Quality
**Supersedes:** ADR-0083 (Testing strategy with architecture tests)
**Related:** ADR-0028 (Clean Architecture), ADR-0080 (Wolverine mediator), ADR-0082 (Modular monolith), ADR-0084 (Playwright MCP for dev-time verification), ADR-0095 (Cursor pagination contract)

## Context

ADR-0083 picked xUnit + FluentAssertions for unit and integration tiers. Three forces have shifted since:

1. **First-party tooling alignment.** MSTest v4 ships with stronger Visual Studio / Rider integration, MSTest analyzers (`MSTest.Analyzers`), and is maintained directly by the .NET team. Microsoft.Testing.Platform (MTP) is the canonical replacement for VSTest going forward — `dotnet test` continues to drive it transparently while MTP adds hot reload, cleaner exit-code semantics, and faster CI runs.
2. **Skill ecosystem.** The `dotnet-test:*` skill family in this Claude Code installation has dedicated coverage for MSTest (`writing-mstest-tests`, `migrate-vstest-to-mtp`, `mtp-hot-reload`, `migrate-mstest-v3-to-v4`, `test-anti-patterns` framework-aware) but only ad-hoc coverage for xUnit. Aligning with the skill ecosystem reduces friction for AI-assisted test work — the dominant mode of work in this repo.
3. **FluentAssertions licence trajectory.** FluentAssertions 8+ moved to a commercial licence; the repo is pinned at 6.12.0. MSTest v4 native assertions (`Assert.AreEqual`, `CollectionAssert`, `StringAssert`, `Assert.ThrowsExactly`) have closed most of the readability gap that motivated FA originally, removing a third-party dependency from every test project.

## Decision

Across **all xUnit-using test projects in the repository** (under `tests/` and `src/Modules/**/*Tests*`):

| Layer | Choice | Notes |
|---|---|---|
| **Test framework** | MSTest v4 | `MSTest.TestFramework`, `MSTest.TestAdapter`, `MSTest.Analyzers` |
| **Project SDK** | `MSTest.Sdk/4.x` | Adopted in Phase 12 of the migration; enables MTP runner |
| **Test runner** | Microsoft.Testing.Platform (MTP) | Replaces VSTest; invoked transparently by `dotnet test` |
| **Assertions** | MSTest v4 native | `Assert`, `CollectionAssert`, `StringAssert`, `Assert.ThrowsExactly` |
| **Mocking** | NSubstitute | Unchanged from ADR-0083 |
| **Containers** | Testcontainers (Postgres, Keycloak) | Unchanged from ADR-0083 |
| **Architecture** | NetArchTest | Unchanged from ADR-0083 |
| **Code coverage** | `Microsoft.Testing.Extensions.CodeCoverage` | Replaces `coverlet.collector` for MTP-driven projects |
| **Mutation testing** | Stryker.NET | Unchanged tool; verified compatible with MTP in Phase 0 |
| **Five-tier pyramid** | architecture / unit / integration / contract / E2E | Unchanged from ADR-0083 |

`KartovaApiFixtureBase` (in `tests/Kartova.Testing.Auth`) drops `IAsyncLifetime` and exposes `Task InitializeAsync()` + `IAsyncDisposable.DisposeAsync()` for MSTest consumers. Per-module integration test fixtures (`Kartova.Catalog.IntegrationTests`, `Kartova.Organization.IntegrationTests`) adopt the `[ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]` pattern (semantic equivalent of xUnit's `IClassFixture<T>`). The top-level `Kartova.Api.IntegrationTests` project's shared `KeycloakContainerFixture` becomes an assembly-scoped singleton via `[AssemblyInitialize]`. Every integration assembly carries `[assembly: DoNotParallelize]` to preserve env-var-race protection that xUnit's `[Collection]` previously provided.

## Consequences

**Positive:**
- One-stop test stack: framework, runner, analyzers all maintained by Microsoft.
- MTP hot reload available for tight test-fix iteration loops (`mtp-hot-reload` skill).
- `MSTest.Analyzers` catches mistakes that xUnit's looser convention previously let through (e.g., misnamed `[ClassInitialize]` signatures).
- One fewer third-party dependency (FluentAssertions) per test project.

**Negative:**
- One-time migration cost paid in the slice that introduces this ADR (~64 files across 10 projects).
- Lose xUnit's per-test class-instance isolation; test classes now reuse one instance across `[TestMethod]` invocations within a class. Field initializers behave identically; constructor side-effects are migrated to `[TestInitialize]`.
- Lose FluentAssertions' deep-object-graph `BeEquivalentTo`. Per-property `Assert.AreEqual` is the replacement; if site count exceeds tolerable repetition, AwesomeAssertions is the documented escape hatch (community fork of FA, MIT-licensed).
- `[assembly: DoNotParallelize]` is more conservative than the per-collection serialization xUnit used; integration test wall-time may increase modestly.

**Neutral:**
- Test taxonomy and CI gates (architecture-tests-must-pass) unchanged from ADR-0083.
- NSubstitute and Testcontainers usage patterns are framework-agnostic and unaffected.

## Migration

Tracked in spec `docs/superpowers/specs/2026-05-08-xunit-to-mstest-migration-design.md` and plan `docs/superpowers/plans/2026-05-08-xunit-to-mstest-migration-plan.md`. 13 phases (Phase 0 + Phases 1–12).
