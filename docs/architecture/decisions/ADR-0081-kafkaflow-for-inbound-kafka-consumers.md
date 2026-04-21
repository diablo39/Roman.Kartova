# ADR-0081: KafkaFlow for Inbound Kafka Consumers

**Status:** Accepted
**Date:** 2026-04-21
**Deciders:** Roman Głogowski (solo developer)
**Category:** Backend Architecture
**Related:** ADR-0003 (Kafka broker), ADR-0027 (.NET 10), ADR-0037 (Schema Registry), ADR-0074 (1000-tenant scale), ADR-0080 (Wolverine mediator + outbound)
**Supersedes (partial):** ADR-0003 (MassTransit client removed for consumers)

## Context

Kafka consumers for catalog events, scan pipeline events, notification dispatch results, and agent telemetry must scale well in a multi-tenant environment where a single partition carries interleaved events from many tenants. The desired property: messages with the **same Kafka key** process sequentially (preserves tenant-level ordering), but messages with **different keys** in the same partition process **in parallel**, allowing throughput above the partition count limit.

Wolverine (ADR-0080) does not provide this specific model natively as of early 2026.

## Decision

Use **KafkaFlow** (by Farfetch) as the library for **all inbound Kafka consumption** in the backend. Configure:

- **Worker pool** per consumer: `WithWorkersCount(N)` with `BytesSum` distribution strategy — parallel processing keyed by Kafka message key within each partition
- **Typed message contracts** from the shared `Kartova.Contracts` assembly (same contracts Wolverine uses for outbound per ADR-0080)
- **Middleware pipeline**: deserializer → tenant-context enrichment → logging/audit → retry policy → DLQ (dead-letter topic) → handler
- **Schema Registry integration** aligned with ADR-0037 (Confluent/Apicurio compatible)

KafkaFlow is **not** used for outbound publishing — that goes through Wolverine's outbox (ADR-0080).

## Rationale

- **Per-key parallelism within partition** — primary driver. At 1000-tenant scale (ADR-0074) with shared topics, this unlocks throughput beyond partition count without over-partitioning (which has its own operational cost).
- **Rich consumer middleware pipeline** — first-class support for typed handlers, per-consumer retry policies, DLQ routing, batch processing. Cleaner than rolling our own over raw `Confluent.Kafka`.
- **Preserves per-tenant ordering** — messages with key `tenant:{id}` stay sequential within that tenant while other tenants process in parallel.
- **Active maintenance** — Farfetch open-sources it, community updates tracking Confluent.Kafka releases.

## Alternatives Considered

- **Raw Confluent.Kafka consumers** — maximum control, but rebuilds middleware, DLQ, typed contracts, and worker pool ourselves. Rejected: too much foundational code for a solo dev.
- **Wolverine inbound** — no native per-key parallel-within-partition model today. Forces overparallelization globally or loses ordering guarantees.
- **MassTransit inbound** — similar concern; no equivalent to KafkaFlow's `BytesSum` worker distribution.

## Consequences

**Positive:**
- Throughput scales beyond partition count per consumer group
- Rich consumer pipeline reduces custom infrastructure code
- Clear separation: KafkaFlow = inbound, Wolverine = outbound + mediation

**Negative / Trade-offs:**
- **Two Kafka stacks in one process** (see ADR-0080 consequences) — dual config surface for auth, Schema Registry, serialization, metrics
- Smaller community than raw Confluent or MassTransit
- Shared `Kartova.Contracts` assembly becomes a coordination point — any contract change must be validated in both Wolverine (out) and KafkaFlow (in) codepaths

**Neutral:**
- Both libraries share the underlying Confluent.Kafka client; operational Kafka concerns (broker tuning, partition strategy) are library-agnostic

## Implementation Notes

```xml
<PackageReference Include="KafkaFlow" Version="3.*" />
<PackageReference Include="KafkaFlow.Microsoft.DependencyInjection" Version="3.*" />
<PackageReference Include="KafkaFlow.TypedHandler" Version="3.*" />
<PackageReference Include="KafkaFlow.Serializer" Version="3.*" />
<PackageReference Include="KafkaFlow.SchemaRegistry.ConfluentAvro" Version="3.*" />
```

```csharp
services.AddKafka(kafka => kafka
    .AddCluster(cluster => cluster
        .WithBrokers(kafkaConnectionString.Brokers)
        .WithSchemaRegistry(config => config.Url = schemaRegistryUrl)
        .AddConsumer(consumer => consumer
            .Topic("webhooks.inbound")
            .WithGroupId("kartova-webhooks-consumer")
            .WithBufferSize(100)
            .WithWorkersCount(8)
            .WithWorkDistributionStrategy<BytesSumDistributionStrategy>()  // per-key parallelism
            .AddMiddlewares(m => m
                .AddSchemaRegistryAvroSerializer()
                .Add<TenantContextMiddleware>()
                .Add<LoggingMiddleware>()
                .AddTypedHandlers(h => h
                    .AddHandler<WebhookReceivedHandler>()
                    .WithHandlerLifetime(InstanceLifetime.Scoped))))));
```

**Contract sharing:** the `Kartova.Contracts` assembly defines all Kafka message types and is referenced by both Wolverine configuration (ADR-0080) and KafkaFlow consumers. Schema Registry is the source of truth for wire format.

## References

- KafkaFlow docs: https://farfetch.github.io/kafkaflow/
- Phase 0: E-01.F-01 (scaffolding), Phase 1+: various consumers per feature
- ADR-0003 (Kafka broker), ADR-0037 (Schema Registry), ADR-0080 (Wolverine outbound)
