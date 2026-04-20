# ADR-0034: OpenAPI Auto-Generated & Self-Rendered (Dogfood)

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** API & Integration Architecture
**Related:** ADR-0029 (REST), ADR-0079 (dogfooding)

## Context

Kartova's documentation engine is itself a product feature (PRD §4.3). The best way to validate the documentation experience is to render Kartova's own API spec using Kartova's doc engine — classic dogfooding.

## Decision

The API emits an OpenAPI 3.x specification generated from the ASP.NET Core controllers/endpoints and DTOs. This spec is consumed by Kartova's documentation engine and rendered inside the product. Swagger UI may be exposed in dev/staging as a fallback; the primary public documentation is served by Kartova itself.

## Rationale

- Guarantees API reference is always in sync with code.
- Exercises the documentation engine on a non-trivial spec.
- Signals quality to prospective customers ("they use it themselves").

## Alternatives Considered

- **Swagger UI / Redoc** — fine tools, but do not validate our own product.
- **Stoplight / hand-written docs** — manual, goes stale, misses the dogfooding benefit.

## Consequences

**Positive:**
- Real, demanding test case for the documentation engine
- Always-current API reference

**Negative / Trade-offs:**
- Bugs in the doc engine are immediately visible in Kartova's own docs (also a positive — forces fixes)
- Spec generation must be high quality; annotations matter

**Neutral:**
- Swagger UI remains available internally as a secondary renderer

## References

- Phase 0: E-01.F-06.S-06
