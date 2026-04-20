# ADR-0065: Hybrid Organizational Structure (Hierarchy + Tags)

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Domain Model
**Related:** ADR-0066 (multi-ownership), ADR-0072 (tag taxonomy), ADR-0064 (entity taxonomy, pending)

## Context

Real organizations have two simultaneous structures: a reporting/ownership hierarchy (Organization → Team → System → Component) and a web of cross-cutting classifications (criticality, compliance regime, tech stack, domain, product line). Picking only one produces either rigid silos or a flat chaos that offers no navigation (PRD §3.2). The catalog must support both.

## Decision

Kartova's org model combines:

- **A strict ownership hierarchy**: Organization → Team → System → Component/Entity. Every entity has exactly one position in this tree (or shares via multi-ownership — ADR-0066).
- **Tags (cross-cutting, many-to-many)**: entities carry 0..N tags. Tags enable non-hierarchical classification (domain, criticality, compliance, tech stack, etc. — see ADR-0072).
- Navigation UX surfaces both: hierarchical drill-down and tag-based filtering/faceting.

## Rationale

- Hierarchy provides clear ownership and blast-radius semantics — essential for access control, notifications, and on-call routing.
- Tags provide the flexibility real enterprises need: a service can be "payments domain," "PCI-scope," "Java," and "tier-1" simultaneously without contorting the hierarchy.
- Best-of-breed model: Backstage-style hierarchy + Kubernetes-style label flexibility.

## Alternatives Considered

- **Flat with tags only** — loses clear ownership semantics; RBAC, notifications, and maturity all need hierarchy.
- **Pure hierarchy** — rigid; forces arbitrary choices (is this service in "payments" or "integrations"?); tags solve this without hierarchy compromises.
- **Graph-based ownership (any-to-any)** — too flexible; hard to reason about; no clear "who owns this."
- **Label-based Kubernetes-style only** — same issue as flat+tags; powerful but ownership becomes convention.

## Consequences

**Positive:**
- Clear ownership for access control and notifications.
- Tag flexibility for slicing across any dimension the org cares about.
- Well-understood model for new users.

**Negative / Trade-offs:**
- Users must decide what goes in hierarchy vs what goes in tags — requires documentation/guidelines.
- Two query patterns to support in the UI (tree navigation + tag filtering).

**Neutral:**
- Tag taxonomy (ADR-0072) governs how much structure we impose on tags themselves.

## References

- PRD §3.2
- Related ADRs: ADR-0066, ADR-0067, ADR-0068, ADR-0072
