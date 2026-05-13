# ADR-0083: Testing Strategy — Test Pyramid with Architecture Tests as CI Gate

**Status:** Superseded by ADR-0097 (test framework + assertion library)
**Date:** 2026-04-21
**Deciders:** Roman Głogowski (solo developer)
**Category:** Testing & Quality
**Related:** ADR-0025 (CI/CD pipeline), ADR-0028 (Clean Architecture), ADR-0080 (Wolverine mediator), ADR-0082 (Modular monolith boundaries), ADR-0084 (Playwright MCP for dev-time verification — complementary to E2E test tier)

## Context

A solo developer scaling to 1000-tenant SaaS needs automated tests that catch regressions reliably without slowing the feedback loop. The codebase has strong structural invariants — Clean Architecture layers per module (ADR-0028), module boundaries (ADR-0082), Wolverine-only inter-module communication (ADR-0080) — that must not erode silently. Traditional unit/integration/E2E tests do not catch architectural drift (e.g., someone adding a direct reference from `Catalog.Application` to `Organization.Infrastructure`).

Explicit architecture tests close this gap by treating the architecture itself as testable specification, enforced on every CI build.

## Decision

Adopt a **five-tier test strategy** with architecture tests as a first-class, mandatory tier:

| Tier | Library | Scope | Speed | CI gate |
|------|---------|-------|-------|---------|
| **Architecture tests** | NetArchTest | Structural invariants — layers, module boundaries, naming, dependencies | <5s | **Must pass** on every PR |
| **Unit tests** | xUnit + FluentAssertions | Pure domain logic, value objects, handlers in isolation | ms-per-test | Must pass |
| **Integration tests** | xUnit + Testcontainers (PostgreSQL, Kafka, ES, KeyCloak) | Module boundaries, EF migrations, Wolverine handlers against real infra | seconds-per-test | Must pass |
| **Contract tests** | Pact.NET (or equivalent) | Kafka message contracts between producer/consumer modules; REST API contracts | seconds | Must pass |
| **E2E tests** | Playwright | Critical user flows (login → register entity → search → notify) | minutes | Runs on main branch; blocks prod deploy |

Architecture tests are **mandatory, non-optional**, and co-located in `tests/Kartova.ArchitectureTests/`. They run first in CI (fast-fail).

### What architecture tests enforce

1. **Module boundary rules (ADR-0082):**
   - Module A may only reference `Kartova.{B}.Contracts`, never `Kartova.{B}.{Domain|Application|Infrastructure}`
   - `Kartova.SharedKernel` must not reference any module
2. **Clean Architecture layering (ADR-0028):**
   - `Domain` layer must not reference `Infrastructure`, `API`, or any external library except BCL primitives
   - `Application` must not reference `Infrastructure` directly (only via interfaces in Application)
   - `Infrastructure` must not reference `API`
3. **Naming conventions:**
   - Command types end in `Command`, queries in `Query`, events in `Event`
   - Handler types end in `Handler` and live in `Application`
   - DbContext types end in `DbContext` and live in `Infrastructure`
4. **Wolverine/Kafka communication (ADR-0080, ADR-0081):**
   - Types in `Kartova.{Module}.Application` must not reference `Confluent.Kafka` or `KafkaFlow` directly (only via abstractions)
   - No module may bypass `IMessageBus` to call another module's handler
5. **Forbidden dependencies:**
   - No module references `MediatR` (ADR-0080 — not used)
   - No module references `MassTransit` (ADR-0003, ADR-0080 — not used)
   - `Domain` types do not reference EF Core attributes / annotations
6. **Immutability where expected:**
   - Domain events are sealed records
   - Value objects in `Domain/ValueObjects/` must be records or structs (no public setters)

### What gets tested where

| Concern | Tier |
|---------|------|
| Domain entity invariant (e.g., `Service.Rename` rejects empty) | Unit |
| Command handler orchestration (mocked repo) | Unit |
| Command handler with real DB + EF migration | Integration |
| RLS policy isolates tenants | Integration |
| Kafka event published after command commit | Integration (via Wolverine test harness) |
| Module A does not directly reference module B internals | **Architecture** |
| `CatalogDbContext` only contains Catalog-owned entities | **Architecture** |
| Webhook HMAC signature format | Unit + Integration |
| User logs in → creates entity → appears in search | E2E |

## Rationale

- **Architecture tests are the cheapest safety net** — NetArchTest runs in seconds, catches violations that code review misses, self-documents the intended structure.
- **Structural invariants are promises** — modular monolith (ADR-0082) only holds if the boundaries are enforced mechanically; otherwise erosion is inevitable.
- **xUnit + Testcontainers aligns with .NET 10 stack** (ADR-0027), no external test orchestration required.
- **Fast feedback loop** — architecture tier runs first; if boundaries break, you know in <5s without waiting for integration tier.
- **Wolverine test harness** — first-class support for testing handlers without starting the full host (`await host.InvokeMessageAndWaitAsync(cmd)`).
- **Solo-dev economics** — E2E tests are the most expensive to write and maintain; keeping them small (critical flows only) prevents the trap of a huge flaky E2E suite.

## Alternatives Considered

- **No architecture tests** — relies on reviewer vigilance. Rejected: in a solo-dev project, there is no second reviewer; boundaries erode silently.
- **ArchUnitNET instead of NetArchTest** — viable alternative; NetArchTest chosen for lighter API, more active maintenance in the .NET ecosystem, and closer match to the Clean Architecture idiom.
- **Heavy E2E suite instead of integration tier** — slow, flaky, hides root causes. Rejected: integration tier (Testcontainers) gives the same confidence for module behavior without browser costs.
- **Mutation testing as mandatory tier** — valuable but overkill for MVP; may be added post-MVP for critical modules (Billing, Audit).

## Consequences

**Positive:**
- Module boundaries and layering rules are mechanically enforced
- CI fails fast on structural drift — within seconds of a bad push
- Test pyramid balances speed and confidence; no single tier dominates
- Architecture tests double as living documentation of architectural intent

**Negative / Trade-offs:**
- Architecture tests require maintenance when intentional structure changes (e.g., adding a new module requires adding it to boundary rules)
- Testcontainers integration tests are slower than in-memory alternatives — accepted as the price of realism
- E2E suite requires Playwright infrastructure (browsers installed in CI)

**Neutral:**
- Mutation testing, property-based testing, and chaos testing are out of scope for MVP; may be added per-module later
- Test coverage thresholds are **not** enforced (gaming-resistant only with mutation testing); code review + architecture tests are the primary gates

## Implementation Notes

**Test project layout — co-located per module:**

Module tests (Tier 2 unit, Tier 3 integration) are **co-located** inside each module folder so a module is a physically self-contained vertical slice (aligned with ADR-0082 modular monolith). Only cross-cutting tests live in the top-level `tests/` directory.

```
src/
  Modules/
    Catalog/
      Kartova.Catalog.Domain/
      Kartova.Catalog.Application/
      Kartova.Catalog.Infrastructure/
      Kartova.Catalog.Contracts/
      Kartova.Catalog.Tests/              # Tier 2 — unit, co-located
      Kartova.Catalog.IntegrationTests/   # Tier 3 — Testcontainers, co-located
    Organization/
      Kartova.Organization.{Domain, Application, Infrastructure, Contracts}/
      Kartova.Organization.Tests/
      Kartova.Organization.IntegrationTests/
    ...

tests/                                     # cross-cutting only
  Kartova.ArchitectureTests/              # Tier 1 — NetArchTest (spans all modules)
    CleanArchitectureLayerTests.cs
    ModuleBoundaryTests.cs
    ForbiddenDependencyTests.cs
    AssemblyRegistry.cs
  Kartova.E2E/                            # Tier 5 — Playwright (cross-module flows)
  Kartova.ContractTests/                  # Tier 4 — Pact (cross-module contracts)
```

**Rationale for co-location:** "Delete a module" becomes `rm -rf src/Modules/{Module}/` — no test dir orphans. Test project names mirror production: `Kartova.Catalog.Tests` references `Kartova.Catalog.Domain` via `..\Kartova.Catalog.Domain\Kartova.Catalog.Domain.csproj`. NetArchTest boundary rule is trivially enforceable by namespace prefix (module A's test project may see its own internals, never another module's).

**Example architecture test (forbidden dependency):**

```csharp
[Fact]
public void No_Module_References_MediatR()
{
    var result = Types.InAssemblies(AllModuleAssemblies())
        .Should()
        .NotHaveDependencyOn("MediatR")
        .GetResult();

    result.IsSuccessful.Should().BeTrue(
        because: "MediatR is not used per ADR-0080; use Wolverine IMessageBus instead");
}
```

**Example architecture test (layer rule):**

```csharp
[Fact]
public void Domain_Does_Not_Reference_Infrastructure()
{
    var result = Types.InAssembly(typeof(CatalogDomainMarker).Assembly)
        .Should()
        .NotHaveDependencyOnAny(
            "Microsoft.EntityFrameworkCore",
            "Confluent.Kafka",
            "KafkaFlow",
            "Elastic.Clients.Elasticsearch")
        .GetResult();

    result.IsSuccessful.Should().BeTrue();
}
```

**CI ordering:** architecture tests → unit → integration → contract → E2E. Fast-fail reduces feedback time on PRs.

## References

- NetArchTest: https://github.com/BenMorris/NetArchTest
- Wolverine test harness: https://wolverinefx.net/guide/testing.html
- Testcontainers for .NET: https://dotnet.testcontainers.org/
- Pact.NET: https://github.com/pact-foundation/pact-net
- Phase 0: E-01.F-01.S-01 (scaffolding includes architecture test project from day one)
- ADR-0025 (CI/CD), ADR-0028 (Clean Architecture), ADR-0082 (Modular monolith)
