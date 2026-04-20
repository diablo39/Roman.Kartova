# ADR-0037: Schema Registry Integrations (Confluent + Apicurio)

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** API & Integration Architecture
**Related:** ADR-0054 (deep scan)

## Context

Kartova presents a unified view of both sync and async APIs (PRD §4.3.2a). For async APIs (Kafka topics, events), schemas typically live in registries (Confluent Schema Registry, Apicurio). Pulling live schemas keeps documentation current without manual effort.

## Decision

Integrate with Confluent Schema Registry and Apicurio to pull live Avro/JSON/Protobuf schemas into the catalog. Schemas are mapped to API-Async entities and related topics/queues. Other registries (AWS Glue, Azure Schema Registry, Pulsar) are deferred.

## Rationale

- Confluent and Apicurio cover the majority of Kafka/AsyncAPI ecosystem adoption.
- Live schemas avoid stale copy-pasted docs.
- Matches Kartova's opinionated stance on treating async APIs as first-class.

## Alternatives Considered

- **AWS Glue Schema Registry** — AWS-specific; defer until a design partner requires it.
- **Azure Schema Registry** — smaller user base; defer.
- **Pulsar schema registry** — small share; defer.
- **Import-only from files** — doesn't stay current; fallback only.

## Consequences

**Positive:**
- Current, authoritative schemas
- Strong async-API differentiator vs competitors

**Negative / Trade-offs:**
- Two integrations to maintain
- Must handle schema evolution and compatibility properly

**Neutral:**
- File-based import remains available for air-gapped tenants

## References

- PRD §4.3.2a
- Phase 3: E-11.F-03.S-03
