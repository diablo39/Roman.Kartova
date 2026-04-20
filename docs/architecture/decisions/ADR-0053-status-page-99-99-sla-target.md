# ADR-0053: 99.99% SLA Target for Status Page (vs 99.9% Platform)

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Status Page Architecture / Availability
**Related:** ADR-0005 (independent data replica), ADR-0023 (separate cluster), ADR-0051 (subscribers), ADR-0052 (custom domains), ADR-0076 (SLA targets, pending)

## Context

The status page exists specifically to inform users when the main platform degrades. It must therefore remain available during platform incidents — meaning its SLA must strictly exceed the platform SLA (PRD §7.2). A status page that goes down with the system it monitors is worse than no status page at all.

## Decision

The status page carries a **99.99% availability SLA** (≤ ~52 minutes downtime/year), one nine higher than the **99.9% platform SLA** (≤ ~8.7 hours/year). This drives concrete architectural constraints:

- Deployed in a **separate Kubernetes cluster** (ADR-0023), ideally a different region/AZ layout than the main platform.
- Reads from an **independent data replica** (ADR-0005) so that main-platform DB incidents don't propagate.
- **Cache-first rendering**: the public status page serves pre-rendered content with short TTLs; even if the read replica is unreachable, the last good render is served with a staleness indicator.
- **No hard dependency** on KeyCloak for public pages; only the internal authenticated status page depends on KeyCloak (ADR-0010).

## Rationale

- Product credibility depends on the status page being up precisely when the platform is not.
- Architectural isolation is the only reliable way to hit 99.99% — instrumentation alone is insufficient.
- Cache-first rendering degrades gracefully and bounds worst-case outage impact.
- The 99.99%/99.9% split is consistent with industry norms (Atlassian Statuspage, AWS Health Dashboard).

## Alternatives Considered

- **Same 99.9% for both** — defeats the product purpose; any single zonal outage would take both down.
- **Externally hosted static status mirror** — handles catastrophic outage, but lacks real-time updates; considered as a *further* fallback layer.
- **Multi-region active/active status page** — eventual target; overkill for MVP given customer tolerance for the occasional minute of staleness.
- **99.999%** — cost/complexity explodes disproportionately for solo-dev operations.

## Consequences

**Positive:**
- Trustworthy status-page UX during incidents — the customer-facing promise holds.
- Architectural isolation also benefits security and blast-radius containment.
- Cache-first rendering improves baseline performance for public pages.

**Negative / Trade-offs:**
- Running a separate cluster/replica has real operational cost — justified by the SLA commitment.
- Cache invalidation on incident updates requires care (stale-while-revalidate, short TTLs).
- Monitoring must itself be externally hosted to avoid the "we can't page ourselves" problem.

**Neutral:**
- The 99.99% target is a contractual commitment; internal error budget may be tighter.

## References

- PRD §7.2
- Phase 4: Feature E-12.F-05 (feature-level)
- Related ADRs: ADR-0005, ADR-0010, ADR-0022, ADR-0023, ADR-0051, ADR-0052
