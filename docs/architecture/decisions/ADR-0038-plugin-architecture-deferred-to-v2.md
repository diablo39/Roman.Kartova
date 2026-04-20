# ADR-0038: Plugin Architecture Deferred to v2.0+

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** API & Integration Architecture
**Related:** ADR-0033 (webhooks, pending)

## Context

Backstage's plugin ecosystem is a defining feature, but it also fragments the product and burdens maintainers. A solo-dev MVP cannot ship a plugin SDK and the core product simultaneously while maintaining quality (PRD §4.8.3).

## Decision

No plugin SDK in MVP. Extensibility for customers is provided via webhooks (ADR-0033, pending) for outbound events and via the REST API (ADR-0029) for inbound automation. A proper plugin framework is a Phase 9 / v2.0+ concern (Epic E-26).

## Rationale

- Scope control — the MVP scope is already ambitious.
- Webhooks + API cover the most common extensibility needs (notifications, custom automation).
- Committing to a plugin contract prematurely risks shipping the wrong abstractions.

## Alternatives Considered

- **OCI-style plugins (WASM / sidecars)** — future-proof but very large scope.
- **Backstage-style plugin framework** — massive surface area, requires frontend plugin system and frontend/backend plugin interop.
- **Scripted rules engine** — valuable but still a future feature.
- **No extensibility at all** — loses power users; webhooks fill this gap.

## Consequences

**Positive:**
- Focus on the core product for MVP
- Clear extensibility story (webhooks + API) for customers

**Negative / Trade-offs:**
- Customers wanting deep UI customization must wait
- Competing narratives with Backstage's plugin ecosystem — need clear messaging

**Neutral:**
- Design notes for a future plugin model can accumulate during MVP without committing

## References

- PRD §4.8.3
- Phase 9: Epic E-26
