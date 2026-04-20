# ADR-0027: .NET / ASP.NET Core for Backend API

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** API & Integration Architecture
**Related:** ADR-0028 (Clean Architecture), ADR-0041 (.NET agent), ADR-0046 (.NET CLI)

## Context

The backend framework choice has outsize impact on a solo-dev project: productivity, talent availability, library ecosystem, runtime performance, and long-term maintenance all hinge on it. Kartova's author's primary expertise is .NET (PRD §8).

## Decision

Use .NET (current LTS) and ASP.NET Core for the backend API. EF Core is the default ORM; the code follows standard idioms (minimal APIs or controllers per module, dependency injection, FluentValidation, MediatR/CQRS optional).

## Rationale

- Solo-developer primary expertise — highest productivity.
- Strong performance and efficient hosting (container-friendly, low memory footprint).
- Consistency with the agent (ADR-0041) and CLI (ADR-0046) means one language across the stack.
- Mature ecosystem for multi-tenancy, OIDC, logging, metrics, testing.

## Alternatives Considered

- **Node.js / NestJS** — broad ecosystem; slower in CPU-heavy paths; not primary expertise.
- **Go** — excellent for agents, less productive for large relational APIs.
- **Java / Spring** — heavy; not primary expertise.
- **Rust / Axum** — too much yak-shaving for solo-dev MVP.
- **Python / FastAPI** — rapid but weaker at multi-tenant typed DB work at scale.

## Consequences

**Positive:**
- Maximum solo-dev productivity
- Unified language across API / CLI / agent

**Negative / Trade-offs:**
- Narrower OSS contributor pool than Node
- Windows-centric stereotype may need active rebuttal with Linux/K8s-native customers (mitigated by containerization)

**Neutral:**
- LTS upgrade cadence (yearly) is manageable but non-optional

## References

- PRD §8
- Phase 0: E-01.F-01.S-01
