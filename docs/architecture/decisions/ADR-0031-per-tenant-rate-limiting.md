# ADR-0031: Per-Tenant Rate Limiting With Burst + 429 Retry-After

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** API & Integration Architecture
**Related:** ADR-0029 (REST), ADR-0032 (bulk endpoints)

## Context

At 1000+ tenants (ADR-0074), a single noisy tenant (misconfigured CI, runaway script, or malicious actor) can saturate the API and degrade every other tenant's experience.

## Decision

Implement per-tenant rate limiting with a burst allowance (token-bucket algorithm). Over-limit requests return HTTP 429 with standard `Retry-After` and `X-RateLimit-*` headers. Default limits are shipped with the platform and are overridable per tenant (for enterprise customers). Critical auth and read endpoints may have more generous limits than write/bulk endpoints.

## Rationale

- Tenant is the correct unit of fairness (see ADR-0014).
- Burst allowance avoids penalizing legitimate short spikes (e.g., bulk imports — ADR-0032).
- 429 + `Retry-After` is the standards-based contract clients already expect.

## Alternatives Considered

- **Per-user / per-key limits** — useful in addition, but tenant is the billing boundary.
- **Global limits only** — unfair; one tenant can starve others.
- **Adaptive / concurrency-based limiting (AIMD)** — sophisticated; can layer on top later.
- **Gateway-level (Envoy/Kong)** — valid alternative; defer to in-process for MVP simplicity.

## Consequences

**Positive:**
- Tenants are isolated from each other's traffic spikes
- Standards-based client contract

**Negative / Trade-offs:**
- Counter storage (Redis or similar) adds operational surface
- Tuning the default limits requires real usage data

**Neutral:**
- Configuration is exposed in the admin UI and billing tiers

## References

- PRD §4.5.1
- Phase 0: E-01.F-06.S-02
