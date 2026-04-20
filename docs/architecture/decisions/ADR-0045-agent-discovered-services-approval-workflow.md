# ADR-0045: Agent-Discovered Services Require Approval Workflow

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Agent Architecture
**Related:** ADR-0041 (.NET agent), ADR-0044 (agent config), ADR-0042 (agent comms, pending), ADR-0067 (relationship origin)

## Context

Agents deployed in customer Kubernetes clusters discover running services, workloads, and network relationships (PRD §4.6.2). Without gating, every discovered workload would automatically appear in the catalog, producing noise and surfacing ephemeral or internal-only components that tenants do not want catalogued. Catalog trust is a core differentiator, and tenants must retain control over what becomes an official catalog entity.

## Decision

Agent-discovered services land in a "pending approval" state in a discovery inbox. A catalog admin (or designated reviewer) explicitly promotes, merges, or rejects each candidate before it appears in the main catalog. Relationships discovered alongside the service are staged with the same approval status and flagged with `origin = agent` (see ADR-0067).

## Rationale

- Tenants retain authoritative control of catalog contents — no surprise entries.
- Prevents noise from ephemeral pods, CI jobs, sidecars, or infrastructure components not intended to be catalogued.
- Aligns with the "catalog trust" product principle (PRD §1).
- Review queue dovetails with the manual-vs-auto conflict queue (ADR-0056).

## Alternatives Considered

- **Auto-accept all discoveries** — creates noise; erodes catalog trust; hard to undo in bulk.
- **Auto-accept with soft-delete on reject** — still pollutes catalog and notifications in the meantime.
- **Policy-gated auto-accept (accept if matches tag rules)** — deferred; can layer on top later as an admin convenience, but the default must be explicit review.

## Consequences

**Positive:**
- Catalog quality and tenant trust preserved.
- Reviewers can enrich entries (ownership, tags) before promotion.
- Clear audit trail of what was promoted, by whom, when.

**Negative / Trade-offs:**
- Review friction; large discovery backlogs require triage UX.
- Time-to-catalog for auto-discovered services is bounded by reviewer response.

**Neutral:**
- Forms the basis for later policy-driven auto-approval rules.

## References

- PRD §4.6.2
- Phase 6: E-15.F-04.S-02
- Related ADRs: ADR-0044, ADR-0056, ADR-0067
