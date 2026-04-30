# ADR-0028: Clean Architecture Layering

**Status:** Accepted (scope narrowed by ADR-0093 — Wolverine no longer mandatory for synchronous HTTP request handlers; remains mandatory for outbox, async messaging, and Kafka outbound)
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** API & Integration Architecture
**Related:** ADR-0027 (.NET API), ADR-0080 (Wolverine CQRS mediator), ADR-0082 (Modular monolith — Clean Architecture applied per module), ADR-0093 (Wolverine scope narrowed)

## Context

A solo developer building a platform intended to grow to 1000+ tenants needs a codebase structure that keeps business logic independent of frameworks and infrastructure, enables straightforward testing, and scales to multiple bounded contexts (catalog, scans, notifications, billing, agents).

## Decision

Organize the solution into Clean Architecture layers with enforced reference direction. CQRS is a mandatory pattern realized via Wolverine (ADR-0080), not optional:

- **Domain** — entities, value objects, domain events. No framework references.
- **Application** — use cases, interfaces, commands/queries (CQRS via Wolverine — ADR-0080, mandatory). References Domain only.
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
- **Modular monolith with bounded-context modules** — adopted per ADR-0082. Clean Architecture is applied independently inside each module.
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
