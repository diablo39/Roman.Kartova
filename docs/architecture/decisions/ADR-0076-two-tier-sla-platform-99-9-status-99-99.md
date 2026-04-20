# ADR-0076: Two-Tier SLA — Platform 99.9% / Status Page 99.99%

**Status:** Accepted
**Date:** 2026-04-20
**Deciders:** Roman Głogowski (solo developer)
**Category:** Scale & Performance / Availability
**Related:** ADR-0053 (status page 99.99% SLA), ADR-0005 (independent data replica), ADR-0023 (separate K8s cluster), ADR-0074 (scale targets), ADR-0075 (performance SLOs)

## Context

Kartova's customer-facing availability promises must be contractually explicit, architecturally achievable by a solo-dev ops model, and internally consistent with the product's distinguishing feature: the status page that stays up while the platform is down. A single blanket SLA either over-promises (unachievable for the main platform) or under-promises (defeats the status-page product thesis) (PRD §7.2).

## Decision

**Two-tier availability SLA:**

| Tier | Target | Downtime Budget | Covers |
|---|---|---|---|
| **Platform** | **99.9%** | ≤ ~8.7 hours/year | Main API, UI, catalog, search, notifications, agents, scans, integrations |
| **Status Page** | **99.99%** | ≤ ~52 minutes/year | Public status page, internal authenticated status page |

**Architectural commitments required to hit 99.99% on status page** (already decided in related ADRs):
- Deployed in a **separate Kubernetes cluster** (ADR-0023) with a different failure domain.
- Reads from an **independent data replica** (ADR-0005), insulated from main-platform DB incidents.
- Cache-first rendering of public pages — last-good render served with staleness indicator on backend failure.
- External monitoring (not self-hosted) to avoid the "can't page ourselves" problem.

**Platform 99.9% is achieved by:**
- Kubernetes-based orchestration with multi-replica deployments (ADR-0022).
- Automated CI/CD with staging gates (ADR-0025).
- Tenant-scoped blast radius (RLS, per-tenant rate limiting — ADR-0031).
- Performance SLOs (ADR-0075) providing early-warning signal before availability degrades.

**Measurement:** external synthetic probes (per tier) + Prometheus internal health. Monthly public SLA reporting is a stretch goal for v2.

## Rationale

- 99.9% is an honestly achievable target for a solo-dev-operated SaaS on Kubernetes — industry-standard for mid-market B2B platforms.
- 99.99% on the status page aligns with the product thesis (ADR-0053): a status page that fails with the platform has negative value.
- The one-nine gap (99.9 → 99.99) is precisely what a separate cluster + replica buys you; pushing to 99.999% would require multi-region active/active (disproportionate complexity).
- Consistent with industry norms: Atlassian Statuspage, AWS Health Dashboard, and most observability vendors carry the same or similar split.
- Published SLAs become enterprise sales collateral — unpublished ones become sales objections.

## Alternatives Considered

- **Same 99.9% for both tiers** — defeats the status-page product differentiation; any zonal outage takes both down.
- **Platform 99.95% / Status 99.99%** — tighter platform target, harder to achieve with solo ops; save for v2 when team scales.
- **Platform 99.9% / Status 99.999%** — marginal UX gain, multi-region complexity not justified pre-Series-A.
- **No contractual SLA (best-effort)** — unsuitable for a B2B SaaS product targeting enterprises.
- **Per-plan SLAs (free < paid < enterprise)** — adds legal/operational complexity; no free tier to discriminate.

## Consequences

**Positive:**
- Public, credible availability promises to customers and design partners.
- Clear engineering-budget allocation: status-page isolation investments are justified by the SLA commitment.
- Error-budget-based release gating becomes possible (monthly budget burn triggers deployment freezes).

**Negative / Trade-offs:**
- Running a separate cluster + replica carries real operational cost — accepted as the price of the status-page promise.
- Hitting 99.9% platform SLA requires on-call discipline even at solo-dev scale (consider external incident-response help).
- Breach disclosure and credit handling must be documented in Terms of Service.

**Neutral:**
- Internal SLO targets (the engineering team's own threshold) will be stricter than the contractual SLA to preserve error budget.

## References

- PRD §7.2 (Availability)
- Related ADRs: ADR-0053 (status page 99.99% SLA), ADR-0005 (independent data replica), ADR-0023 (separate K8s cluster), ADR-0074 (scale targets), ADR-0075 (performance SLOs), ADR-0022 (Kubernetes deployment), ADR-0025 (CI/CD)
