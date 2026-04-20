# ADR-0026: Fully Proprietary (No Open-Source Core)

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Platform Infrastructure
**Related:** —

## Context

Developer-portal competitors include Backstage (fully OSS) and Port (closed SaaS). Each model has different commercial and community dynamics. As a solo-dev pre-revenue project, the strategy must align with time investment and monetization (PRD §11).

## Decision

Kartova source code is fully proprietary. No open-source core, no source-available license. Selected internal libraries may be open-sourced opportunistically if they hold no commercial value, but the product itself remains closed.

## Rationale

- Commercial differentiation — the feature set is the product.
- Avoids community-management overhead a solo dev cannot sustain.
- No risk of a hyperscaler forking and offering a hosted competitor.

## Alternatives Considered

- **Open-core** — splits focus; community-management load without guaranteed commercial upside.
- **Source-available (BSL / SSPL)** — deters hyperscaler forks but complicates enterprise procurement.
- **Fully OSS with hosted offering** — Backstage's model; requires a community management function and a larger team.

## Consequences

**Positive:**
- Clear commercial story
- No community distraction during MVP

**Negative / Trade-offs:**
- No community contributions
- Slower brand awareness compared to OSS-led growth

**Neutral:**
- Documentation may still be public; SDKs/CLIs may be independently licensed under permissive terms if useful

## References

- PRD §11 (Resolved Decision #9)
