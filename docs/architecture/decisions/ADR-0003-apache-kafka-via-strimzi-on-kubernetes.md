# ADR-0003: Apache Kafka via Strimzi Operator on Kubernetes

**Status:** Accepted
**Date:** 2026-04-20
**Deciders:** Roman Głogowski (solo developer)
**Category:** Data Platform / Messaging
**Related:** ADR-0001 (PostgreSQL), ADR-0022 (Kubernetes cloud-agnostic deployment), ADR-0037 (Schema Registry integrations), ADR-0041 (.NET agent uses Kafka), ADR-0047 (notification dispatch), ADR-0080 (Wolverine outbound client), ADR-0081 (KafkaFlow inbound client)

## Context

Kartova's event-driven architecture requires a message bus for several cross-cutting concerns:

- Phase 1 notification dispatch (ADR-0047) — in-app, email, webhook, Slack/Teams fan-out with retry and DLQ semantics.
- Phase 6 agent health/metrics ingestion — thousands of agents (tysiące agentów) each emitting hundreds of probe results per second, plus Prometheus scrape batches forwarded from customer networks.
- Scheduled re-scan orchestration — fan-out of scan jobs, progress events, and completion signals across workers.
- Webhook retry queues — durable retry with exponential backoff and DLQ replay (ADR-0033 pending).

Scale target is 1000+ tenants (ADR-0074). As a solo developer, the chosen platform must minimize operational burden while offering a credible exit path to managed services. A decision was previously deferred (PRD §8 "TBD (RabbitMQ/Kafka recommended)"); it must be resolved before Phase 6 implementation begins.

## Decision

Use **Apache Kafka** as the primary event streaming / message bus, deployed on Kubernetes via the **Strimzi Operator** in **KRaft mode** (no ZooKeeper). A 3-broker minimum is used in production for high availability. .NET services integrate via two complementary libraries over the **Confluent.Kafka** client: **Wolverine** for outbound publishing with transactional outbox (ADR-0080) and **KafkaFlow** for inbound consumers with per-key parallel-within-partition workers (ADR-0081). MassTransit is **not** used. Schema Registry concerns are already covered by ADR-0037 (Confluent Schema Registry compatibility and Apicurio).

## Rationale

- Developer has direct Kafka experience — effectively zero learning curve.
- Apache 2.0 license — no BSL concerns, no enterprise upsell pressure.
- Strimzi is the de facto standard for Kafka on K8s; it reduces operational complexity to a handful of CRDs (`Kafka`, `KafkaTopic`, `KafkaUser`) and aligns with the cloud-agnostic K8s posture established in ADR-0022.
- KRaft mode eliminates ZooKeeper, reducing moving parts, memory footprint, and failure modes.
- Ecosystem: Kafka Connect, Schema Registry, ksqlDB, Mirror Maker 2 — all available if needed later (e.g., CDC from Postgres into Elasticsearch).
- Exit options: Confluent Cloud, AWS MSK, Azure Event Hubs (Kafka API) — all wire-compatible; migration is a configuration change, not a rewrite.
- Fits existing ADR-0022 cloud-agnostic Kubernetes deployment without locking Kartova to any cloud's native messaging service.

## Alternatives Considered

- **Postgres Outbox + LISTEN/NOTIFY** — insufficient throughput for 1000+ tenants with agent telemetry; no partitioning story; no offline-consumer retention; LISTEN/NOTIFY is best-effort and connection-scoped.
- **RabbitMQ** — developer has no operational experience; learning curve is a real cost for a solo team; no tail-log replay; weaker .NET event-sourcing story; queues are consumption-destructive by default.
- **Redpanda** — Kafka-API compatible with marginal operational advantages (single binary in C++, no ZK), but BSL license raises legal ambiguity for a proprietary SaaS; the ZK/JVM arguments it leans on are already negated by Strimzi + KRaft; ecosystem is smaller; no perceived advantage for someone already fluent in Kafka.
- **NATS JetStream** — smaller ecosystem, weaker .NET client story, unfamiliar semantics (subjects vs. topics + partitions), less operational experience in the community.
- **Cloud-native (Azure Service Bus / SQS+SNS / GCP Pub/Sub)** — directly conflicts with ADR-0022's cloud-agnostic deployment strategy; couples the platform to one cloud provider.

## Consequences

**Positive:**
- Leverages existing developer expertise — fastest time-to-productivity.
- Battle-tested, massive ecosystem, abundant documentation and community support.
- Exit options to managed services (Confluent Cloud, MSK, Event Hubs) are all Kafka API-compatible.
- Schema Registry (ADR-0037) and Kafka Connect available for future needs (CDC to Elasticsearch, data lake sinks, etc.).

**Negative / Trade-offs:**
- Higher resource footprint than Redpanda (JVM) — requires 3 brokers × 2–4 GB RAM minimum baseline.
- JVM tuning may be required at higher scale (producer throughput, Kafka Streams workloads).
- Strimzi Operator + CRDs add more conceptual surface than a simple queue on Postgres.
- 3-broker HA minimum means always-on baseline infrastructure cost.

**Neutral:**
- Two Kafka client libraries (Wolverine outbound + KafkaFlow inbound) in one process — see ADR-0080 and ADR-0081 for trade-off discussion; both share the underlying Confluent.Kafka client and Schema Registry settings must be kept aligned.
- KRaft mode is GA and stable since Kafka 3.3+, but is still newer in field-experience terms than ZooKeeper-based deployments.

## References

- PRD §8 (Technology Stack — Message Bus row, previously TBD)
- Phase 1 Epic E-06a (Notifications), Phase 6 Epic E-15 (Hybrid Agent)
- Strimzi Operator: https://strimzi.io
- Apache Kafka KRaft: https://kafka.apache.org/documentation/#kraft
- ADR-0022 (Kubernetes cloud-agnostic deployment), ADR-0037 (Schema Registry), ADR-0041 (.NET agent), ADR-0047 (notification dispatch)
