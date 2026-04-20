# ADR-0068: Fixed Relationship Type Vocabulary

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Domain Model
**Related:** ADR-0064 (entity taxonomy, pending), ADR-0065 (hybrid org), ADR-0067 (relationship origin)

## Context

Relationship semantics matter for graph analysis — "depends on" means something concrete (blast radius, deployment order) that "is related to" does not (PRD §3.3). An open vocabulary lets users invent relationship types that are individually meaningful but collectively prevent global analysis (impact, dependency sort, blast-radius computation). An opinionated, closed vocabulary forces consistency.

## Decision

The catalog supports a **fixed vocabulary of seven relationship types**:

1. `depends-on` — runtime or build-time dependency.
2. `provides-api-for` — this entity exposes an API consumed by the related entity.
3. `consumes-api-from` — this entity calls an API exposed by the related entity.
4. `publishes-to` — this entity produces messages/events on the related queue/topic/broker.
5. `subscribes-from` — this entity consumes messages/events from the related queue/topic/broker.
6. `deployed-on` — runtime/infrastructure relationship (service on infrastructure/environment).
7. `part-of` — composition: component is part of a system/application.

Each type has a well-defined directionality and permitted source/target entity types (e.g., only messaging-capable entities can `publishes-to` queues/topics). Extensions to this vocabulary require a platform release — tenants cannot add custom types in MVP.

## Rationale

- A closed vocabulary makes graph analytics tractable: impact analysis, dependency-ordered deploy hints, blast-radius reports, async-vs-sync coupling metrics.
- Seven types cover the 95% of real-world relationships observed in platform catalogs.
- Directionality and type-pair constraints prevent nonsensical relationships ("API-Sync deployed-on Queue+Topic") that confuse analytics.
- Opinionated model is consistent with the overall product stance (ADR-0064 fixed entity taxonomy).

## Alternatives Considered

- **Open-vocabulary user-defined relationships** — maximally flexible; breaks analytics and creates each-org-a-private-ontology silos.
- **Backstage-style (`dependencyOf` / `providesApi` / etc.)** — similar closed vocabulary; our types are aligned but named for the async world too.
- **RDF / triple-store model** — over-engineered for MVP; analytics still require a closed predicate set in practice.

## Consequences

**Positive:**
- Graph analytics (impact, dependency) work out of the box.
- Consistent semantics across tenants — cross-tenant benchmarks and patterns are meaningful.
- Scanners and agents have a clear target vocabulary to map discoveries into.

**Negative / Trade-offs:**
- Some real-world relationships may not fit cleanly; "other" escape hatch not provided in MVP.
- Adding a new type requires a platform change — by design, but worth being explicit about.

**Neutral:**
- Vocabulary may evolve; migration path is additive.

## References

- PRD §3.3
- Related ADRs: ADR-0064, ADR-0067
