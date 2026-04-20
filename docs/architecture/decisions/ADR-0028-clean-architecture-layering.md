# ADR-0028: Clean Architecture Layering

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** API & Integration Architecture
**Related:** ADR-0027 (.NET API)

## Context

A solo developer building a platform intended to grow to 1000+ tenants needs a codebase structure that keeps business logic independent of frameworks and infrastructure, enables straightforward testing, and scales to multiple bounded contexts (catalog, scans, notifications, billing, agents).

## Decision

Organize the solution into Clean Architecture layers with enforced reference direction:

- **Domain** — entities, value objects, domain events. No framework references.
- **Application** — use cases, interfaces, commands/queries. References Domain only.
- **Infrastructure** — EF Core, Elasticsearch client, KeyCloak client, messaging, file storage. References Application.
- **API** — ASP.NET Core composition root, controllers/endpoints, HTTP concerns. References Application and Infrastructure.

Reference direction is enforced at build time (e.g., NetArchTest or project references only).

## Rationale

- Keeps business logic testable in isolation.
- Makes infrastructure swaps (e.g., DB provider) mechanical.
- Well-understood idiom in the .NET community.

## Alternatives Considered

- **Vertical slice architecture** — excellent for small teams; may be adopted as a sub-pattern within modules.
- **Feature folders** — simpler but less disciplined as the codebase grows.
- **Modular monolith with bounded-context modules** — compatible with Clean Architecture; likely to evolve toward this as bounded contexts stabilize.
- **Microservices** — premature for solo-dev MVP.

## Consequences

**Positive:**
- Clear separation, testable domain, swappable infra
- Onboarding path for future contributors

**Negative / Trade-offs:**
- More projects and interfaces — some boilerplate
- Risk of over-abstraction; guard with pragmatic taste

**Neutral:**
- Vertical-slice organization within Application is fine and encouraged

## References

- Phase 0: E-01.F-01.S-01
