# ADR-0080: Wolverine for In-Process Mediation and Outbound Messaging

**Status:** Accepted
**Date:** 2026-04-21
**Deciders:** Roman Głogowski (solo developer)
**Category:** Backend Architecture
**Related:** ADR-0003 (Kafka broker), ADR-0027 (.NET 10), ADR-0028 (Clean Architecture), ADR-0033 (HMAC webhooks outbox), ADR-0047 (notification dispatch), ADR-0081 (KafkaFlow inbound consumers)
**Supersedes (partial):** ADR-0028 (MediatR mention removed)

## Context

The backend needs two overlapping capabilities:

1. CQRS command/query mediation inside the Application layer (ADR-0028) — decouples ASP.NET Core endpoints from handlers, enables validation/audit/tenant-filter middleware.
2. Outbound Kafka publishing with **transactional outbox guarantees** — required by ADR-0033 (HMAC webhooks) and ADR-0047 (notification dispatch). Messages must be persisted in the same DB transaction as domain writes and delivered at-least-once after commit.

Inbound Kafka consumption is handled by a separate library per ADR-0081 (KafkaFlow) because of its native per-key parallel-within-partition worker model, which Wolverine does not match today.

## Decision

Adopt **Wolverine (JasperFx)** as the single library for:

- **CQRS command & query mediation** — `IMessageBus.InvokeAsync<TResponse>(command)`
- **Outbound Kafka publishing** — `IMessageBus.PublishAsync(event)` routed to Kafka
- **Transactional outbox** — `UseDurableOutboxOnAllSendingEndpoints()` with PostgreSQL persistence, integrated with EF Core transactions
- **Sagas / stateful workflows** when needed in later phases (e.g., scan pipeline)

Wolverine is **not** used for inbound Kafka consumers — those belong to KafkaFlow (ADR-0081).

CQRS convention:

- Commands: `XxxCommand` + `XxxCommandHandler` (returns void or result DTO)
- Queries: `XxxQuery` + `XxxQueryHandler` (returns DTO)
- Handlers live in Application layer, one per command/query
- Validation via FluentValidation middleware in the Wolverine pipeline
- Cross-cutting concerns (logging, audit, tenant filter) as Wolverine middleware

## Rationale

- **Mediator + outbox in one place** — eliminates hand-rolling outbox code (table + worker + retry + idempotency). Transactional outbox is the primary driver.
- **Code-gen dispatch** — handler routing is generated at startup, no reflection per request. Relevant for 1000-tenant scale (ADR-0074) and p95 SLOs (ADR-0075).
- **Saga support** — available when Phase 2 scan workflows need orchestration, without adding a separate saga library.
- **CQRS is mandatory, uniform** — not "optional if you need it" as in ADR-0028 original wording; promotes consistency across bounded contexts.
- **.NET 10 supported** (ADR-0027); active development by JasperFx.

## Alternatives Considered

- **MediatR alone + manual outbox** — MediatR licensing future uncertain (2024–25 commercial move); manual outbox is ~200 LOC but bulletproof once written. Lost the saga/workflow story.
- **MassTransit** — heavier, optimized for bus semantics, outbox less ergonomic than Wolverine's; ecosystem large but not a differentiator for solo dev.
- **Wolverine for inbound too** — lacks KafkaFlow's per-key parallel-within-partition worker model (ADR-0081 rationale).
- **Custom mediator** — wastes time every project rebuilds this.

## Consequences

**Positive:**
- Transactional outbox solves ADR-0033 and ADR-0047 with near-zero custom code
- One library for mediator + outbound + future sagas
- Code-gen performance at scale

**Negative / Trade-offs:**
- **Two Kafka producer/consumer stacks in one process** (Wolverine outbound + KafkaFlow inbound) — double the Kafka client config surface, two Prometheus metric sets, two connection pools. Operational cost is real.
- **Serialization must be manually kept consistent** between Wolverine (out) and KafkaFlow (in) — same Schema Registry, same JSON conventions, same CloudEvents wrapping where applicable.
- Smaller community than MediatR/MassTransit — fewer StackOverflow hits.
- Tighter coupling to JasperFx release cadence.

**Neutral:**
- Clean Architecture boundaries (ADR-0028) preserved; handlers live in Application.

## Implementation Notes

```xml
<PackageReference Include="WolverineFx" Version="3.*" />
<PackageReference Include="WolverineFx.Kafka" Version="3.*" />
<PackageReference Include="WolverineFx.EntityFrameworkCore" Version="3.*" />
<PackageReference Include="WolverineFx.FluentValidation" Version="3.*" />
```

```csharp
builder.Host.UseWolverine(opts =>
{
    // Outbound transport only — inbound consumers handled by KafkaFlow (ADR-0081)
    opts.UseKafka(kafkaConnectionString).AutoProvision();

    opts.PersistMessagesWithPostgresql(connectionString);
    opts.UseEntityFrameworkCoreTransactions();
    opts.UseFluentValidation();
    opts.Policies.AutoApplyTransactions();
    opts.Policies.UseDurableOutboxOnAllSendingEndpoints();

    // Route outbound events
    opts.PublishMessage<WebhookDispatchRequested>().ToKafkaTopic("webhooks.outbound");
    opts.PublishMessage<NotificationRequested>().ToKafkaTopic("notifications.dispatch");
});
```

**Serialization contract** enforced via shared `Kartova.Contracts` assembly referenced by both Wolverine config and KafkaFlow config (see ADR-0081).

## References

- Wolverine docs: https://wolverinefx.net/
- Phase 0: E-01.F-01 (scaffolding), E-01.F-03 (outbox table in migrations)
- ADR-0003 (Kafka broker), ADR-0033 (webhooks outbox), ADR-0081 (KafkaFlow consumers)
