# ADR-0072: Tag Taxonomy — Predefined Categories Plus Custom

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Domain Model
**Related:** ADR-0065 (hybrid org), ADR-0067 (relationship origin), ADR-0070 (scorecards)

## Context

Tags are the cross-cutting classification dimension in the hybrid org model (ADR-0065). Fully free-form tags become inconsistent across teams ("prod", "production", "PRD"), defeating the point. Strict controlled vocabulary slows teams down and fails to cover tenant-specific needs. The middle ground — **predefined categories with both curated values and room for custom tags** — gives consistency where it matters without blocking local adaptation.

## Decision

Tags are organized into **categories**:

- **System-defined categories** ship with predefined values and tenant-extensible value sets:
  - `domain` (e.g., payments, identity, catalog) — tenant-extensible.
  - `criticality` (tier-1, tier-2, tier-3, experimental) — fixed values.
  - `compliance` (gdpr, mifid-ii, pci, soc2, hipaa) — fixed values.
  - `tech-stack` (language/framework tags) — tenant-extensible.
  - `environment` (production, staging, dev, sandbox) — fixed values.
- **Tenant custom categories**: organizations may define additional categories with their own value sets.
- Tag format: `category:value` (Kubernetes-label style) for machine-friendly querying; humans see "Criticality: tier-1".
- Scorecard rules (ADR-0070) can reference tags directly (e.g., `criticality=tier-1 ⇒ requires_on_call=true`).

## Rationale

- Predefined system categories cover the 80% of what every tenant needs — consistency where it matters (compliance reporting, criticality-based alerting).
- Custom categories allow tenant-specific dimensions without platform changes.
- Fixed values for criticality/compliance/environment prevent the "prod vs production" inconsistency problem.
- `category:value` format is familiar (Kubernetes, Azure resource tags) and keeps tags machine-queryable.

## Alternatives Considered

- **Fully free-form labels** — tag sprawl, inconsistency, broken cross-org benchmarks.
- **Strict controlled vocabulary only** — fails for tenant-specific dimensions; blocks adaptation.
- **Kubernetes-style key:value with no category guidance** — essentially the free-form option under a syntax constraint; loses the semantic guidance.

## Consequences

**Positive:**
- Consistent compliance and criticality reporting across the platform.
- Tenant flexibility preserved via custom categories and extensible value sets.
- Scorecard rules become more reliable because their tag inputs are disciplined.

**Negative / Trade-offs:**
- Tag administration UI must distinguish system-defined from tenant-defined categories clearly.
- Migration from free-form legacy tags during onboarding needs a guided flow.

**Neutral:**
- System-defined category values can evolve over time via platform releases.

## References

- PRD §3.2
- Phase 1: Feature E-03.F-04 (feature-level)
- Related ADRs: ADR-0065, ADR-0067, ADR-0070
