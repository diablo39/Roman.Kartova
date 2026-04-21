# ADR-0082: Modular Monolith Architecture

**Status:** Accepted
**Date:** 2026-04-21
**Deciders:** Roman Głogowski (solo developer)
**Category:** Backend Architecture
**Related:** ADR-0027 (.NET API), ADR-0028 (Clean Architecture layering), ADR-0080 (Wolverine mediator), ADR-0081 (KafkaFlow inbound), ADR-0003 (Kafka event bus), ADR-0012 (RLS multi-tenancy)

## Context

Kartova spans many bounded contexts: Catalog, Relationships, Organization & Teams, Search, Notifications, Auto-Import/Scans, Documentation, Status Page, Billing, Agent coordination, Policy Engine. A solo developer cannot operate microservices across this scope during MVP, but also cannot afford a "big ball of mud" that becomes impossible to split later when scale or team growth demands it.

Clean Architecture (ADR-0028) organizes each bounded context well internally but does not prescribe how bounded contexts relate to each other within one solution. A decision is needed on inter-module boundaries, physical structure, and communication rules.

## Decision

Organize the backend as a **modular monolith**: a single deployable artifact composed of strongly-isolated modules, where each module is an independent bounded context with its own Clean Architecture layers (Domain / Application / Infrastructure) and its own set of projects.

**Module list (MVP):**

| Module | Bounded context |
|--------|-----------------|
| `Catalog` | Entity registry (9 fixed types + JSONB custom_attributes) |
| `Organization` | Tenants, orgs, teams, users, RBAC bindings |
| `Relationships` | Manual + auto-discovered entity graph |
| `Search` | Elasticsearch indexing + query surface |
| `Notifications` | Dispatch engine + channel adapters |
| `AutoImport` | Git provider integrations + scan pipeline |
| `Documentation` | Markdown/OpenAPI/AsyncAPI docs |
| `StatusPage` | Public status page (separate deployable, ADR-0023) |
| `Billing` | Subscriptions, invoices, feature gating |
| `Agent` | Hybrid agent coordination & config |
| `Policy` | Policy engine (Phase 5) |
| `Platform` | Cross-cutting: auth, audit, tenant context, observability |

**Module boundary rules (enforced):**

1. **Project structure** — each module has its own csproj tree: `Kartova.{Module}.Domain`, `Kartova.{Module}.Application`, `Kartova.{Module}.Infrastructure`, `Kartova.{Module}.Contracts` (public API).
2. **No direct references across modules** — module A may reference only `Kartova.{B}.Contracts` (public integration surface), never `Kartova.{B}.Domain` or `Kartova.{B}.Application`.
3. **Inter-module communication** — only two allowed channels:
   - Synchronous: Wolverine commands/queries via `IMessageBus` (in-process, ADR-0080)
   - Asynchronous: domain events over Kafka (ADR-0003, published via Wolverine outbox ADR-0080, consumed via KafkaFlow ADR-0081)
4. **No cross-module database transactions** — each module owns its tables; cross-module data consistency uses the Wolverine transactional outbox (eventual consistency pattern).
5. **No cross-module foreign keys** — logical references by ID only; referential integrity is the owning module's responsibility.
6. **Shared kernel is minimal** — `Kartova.SharedKernel` contains only: `TenantId`, `UserId`, result types, common value objects, base domain event.
7. **Each module registers itself** via `IModule.RegisterServices(IServiceCollection)` + `IModule.ConfigureWolverine(WolverineOptions)`; composition root in the API project loads all modules explicitly.

**Architecture fitness functions** (CI-enforced):

- `NetArchTest` rules: "types in module A must not reference module B internals"
- `NetArchTest` rules: Domain layer must not reference Infrastructure or API
- `Kartova.SharedKernel` must not reference any module
- Build fails on violation.

## Rationale

- **Preserves option to split** — a module can become a separate service later with refactoring measured in days, not months, because contracts are already explicit and communication is already message-based.
- **Solo-dev productivity** — single solution, single debugger, single deploy. No distributed tracing, no cross-service contract versioning, no service mesh during MVP.
- **Prevents ball of mud** — fitness functions enforce boundaries mechanically; "just add a reference" is not possible without deliberate ADR.
- **Aligns with existing decisions** — Wolverine (ADR-0080) and KafkaFlow (ADR-0081) are already the designated messaging layers; modules plug into them naturally.
- **Matches domain reality** — the bounded contexts above are real; they don't share state meaningfully (catalog doesn't care about billing invoices).
- **Scales the codebase** — as the system grows past 100k LOC, module isolation keeps cognitive load bounded per feature.

## Alternatives Considered

- **Single-project monolith with namespaces** — fastest to start; but boundaries are conventions, not enforced. "Temporary" cross-namespace references become permanent. Rejected: sets up Phase 2+ pain.
- **Microservices from day one** — premature; operational cost per service (CI, deploy, observability, schema migrations) is prohibitive for a solo dev. Rejected in ADR-0028; this ADR reinforces.
- **Vertical slice architecture flat** — good within a module; used as sub-pattern. Not a substitute for bounded-context boundaries.
- **Service-oriented modular monolith with in-process RPC** — adds an indirection (IModuleApi interfaces) that Wolverine mediator already provides more cleanly.

## Consequences

**Positive:**
- Clear path from monolith → services per module when justified by scale/team
- Enforced boundaries via fitness functions → self-healing architecture
- Single deployment simplifies operations throughout MVP
- Wolverine in-process mediation is fast (no network hop) while using the same command/query shape that would work across a network later
- Each module can be owned / rewritten independently

**Negative / Trade-offs:**
- Upfront project structure is heavier than single-project — ~40 csproj files for MVP (vs ~5–10 for a flat solution)
- Shared DbContext is tempting but forbidden — each module needs its own `DbContext` scoped to its tables; requires discipline in migrations
- Cross-module queries that need multiple modules' data go through `IMessageBus.InvokeAsync<TResponse>(query)` — adds ceremony vs a direct query
- Eventual consistency between modules requires thinking about saga patterns earlier than a shared-DB monolith would

**Neutral:**
- Module count may consolidate — if `Policy` stays thin through Phase 5, it may fold into `Platform`
- Status Page is already a separate deployable (ADR-0023) — it is a module like others, just with its own host

## Implementation Notes

**Solution layout:**

```
src/
  Kartova.SharedKernel/              # tenant id, value objects, base events
  Kartova.Api/                       # composition root, ASP.NET Core host
  Modules/
    Catalog/
      Kartova.Catalog.Domain/
      Kartova.Catalog.Application/
      Kartova.Catalog.Infrastructure/
      Kartova.Catalog.Contracts/     # public: commands, queries, events
    Organization/
      Kartova.Organization.Domain/
      Kartova.Organization.Application/
      Kartova.Organization.Infrastructure/
      Kartova.Organization.Contracts/
    ... (one tree per module)
tests/
  Kartova.ArchitectureTests/         # NetArchTest fitness functions
  Modules/
    Catalog.Tests/
    ...
```

**Module registration example:**

```csharp
public class CatalogModule : IModule
{
    public void RegisterServices(IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<CatalogDbContext>(o => o.UseNpgsql(...));
        services.AddScoped<IEntityRepository, EntityRepository>();
    }

    public void ConfigureWolverine(WolverineOptions opts)
    {
        opts.Discovery.IncludeAssembly(typeof(CatalogModule).Assembly);
        opts.PublishMessage<EntityRegisteredEvent>().ToKafkaTopic("catalog.events");
    }
}
```

**Fitness function example:**

```csharp
[Fact]
public void Catalog_Must_Not_Reference_Other_Modules_Internals()
{
    var result = Types.InAssembly(typeof(CatalogModule).Assembly)
        .Should()
        .NotHaveDependencyOnAny(
            "Kartova.Organization.Domain",
            "Kartova.Organization.Application",
            "Kartova.Organization.Infrastructure")
        .GetResult();

    result.IsSuccessful.Should().BeTrue();
}
```

## References

- *Monolith to Microservices* — Sam Newman (module-first migration argument)
- *Building Modular Monoliths* — Kamil Grzybek series
- NetArchTest: https://github.com/BenMorris/NetArchTest
- Phase 0: E-01.F-01 (scaffolding with module structure)
- ADR-0028 (Clean Architecture — applied per module)
- ADR-0080 (Wolverine — inter-module communication)
