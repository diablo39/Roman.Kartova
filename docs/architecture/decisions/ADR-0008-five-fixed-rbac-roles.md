# ADR-0008: Five Fixed RBAC Roles

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Authentication & Authorization
**Related:** ADR-0006 (KeyCloak), ADR-0007 (JWT)

## Context

Kartova's authorization model must cover solo developers, engineering teams, platform teams, and automation (CI/CD). A fully policy-driven model (ABAC/OPA) adds implementation and operational complexity that is unjustified for MVP. Personas in PRD §5.2 map cleanly to a small fixed role set.

## Decision

Define five fixed roles scoped per organization:

1. **Org Admin** — full tenant control, billing, member management.
2. **Team Admin** — manage their team's entities and members.
3. **Member** — create/edit entities in owned teams.
4. **Viewer** — read-only.
5. **Service Account** — programmatic access, scope determined by assignment (ADR-0009).

Roles are not fully customizable. Fine-grained permission extensions may be added via scorecard/policy features later.

## Rationale

- Matches the five personas without forcing users to design their own permission model.
- Makes authorization decisions fast, cacheable, and auditable.
- Reduces support surface area — no per-tenant role debugging.

## Alternatives Considered

- **ABAC / OPA / Cedar** — overkill for MVP; operationally costly; deferred as possible future addition for enterprise tier.
- **Fully custom roles per org** — tempting but multiplies test surface and support effort.
- **Casbin** — adds library dependency without removing the need to design the core model.
- **AWS-style JSON policies** — far too much rope for the target users.

## Consequences

**Positive:**
- Simple mental model for users and for authorization code
- Fast permission checks, easy to audit
- Clear upgrade path to policy-based model later

**Negative / Trade-offs:**
- Some customers will ask for custom roles — must say no at MVP
- Edge cases (e.g., "team viewer") may require synthetic sub-roles

**Neutral:**
- Roles are encoded as JWT claims (ADR-0007)

## References

- PRD §5.2
- Phase 0: E-01.F-04.S-03
