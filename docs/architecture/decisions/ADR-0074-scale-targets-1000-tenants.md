# ADR-0074: Scale Targets — 1000+ Tenants, 10k Services/Tenant, 5k Users/Tenant

**Status:** Accepted
**Date:** 2026-04-20
**Deciders:** Roman Głogowski (solo developer)
**Category:** Scale & Performance
**Related:** ADR-0075 (performance SLOs), ADR-0076 (two-tier SLA), ADR-0012 (RLS for scale, pending), ADR-0001 (PostgreSQL), ADR-0002 (Elasticsearch)

## Context

Architectural choices that are reasonable at 10 tenants fail at 1000, and architectures designed for 100k tenants over-engineer the path to product-market fit. Kartova needs an explicit scale envelope so every component — DB schema, indexing, tenant isolation, pricing, ops runbooks — can be sized against a single, shared target (PRD §7.1).

## Decision

The platform's **design envelope** is:

- **1000+ tenants** (orgs) on a single production deployment.
- **10,000 services/components** per tenant (peak).
- **5,000 users** per tenant (peak).
- **Millions of entities** in aggregate across all tenants (services + components + relationships + events).
- **Mid-market SaaS envelope** — explicitly not Fortune-100 enterprise scale, explicitly not "two startups sharing a Postgres."

Every architectural decision must be defensible at these numbers:

- Tenant isolation strategy (RLS — ADR-0012 pending) must keep query cost sub-linear in tenant count.
- Indexing strategy (Elasticsearch — ADR-0002) must shard/partition to keep p95 latency budgets (ADR-0075).
- Background job fan-out (scans, notifications) must be tenant-throttled to prevent noisy-neighbor starvation.
- Pricing and metering (ADR-0063) must not have O(tenants × services) hot paths.

If a design exceeds these bounds, it is a feature for v2+ and should be called out explicitly rather than silently over-engineered.

## Rationale

- A published envelope makes "will this scale?" answerable with numbers rather than vibes.
- 1000 tenants × 5k users = 5M-user ceiling, which comfortably supports the revenue model at MVP prices without requiring exotic infrastructure.
- 10k services/tenant covers even large engineering orgs (most Fortune-500 orgs operate <5k services); headroom exists without over-targeting.
- Mid-market framing matches the go-to-market motion (ADR-0079) — design partners will be in this range.
- Explicit bounds protect against premature globalization/sharding complexity.

## Alternatives Considered

- **SMB-only envelope (100 tenants, 500 services)** — insufficient headroom; would force a re-architecture precisely when revenue starts ramping.
- **Enterprise envelope (10k+ tenants, 100k services/tenant)** — justifies sharded Postgres, global distribution, per-tenant clusters; wildly over-engineered for MVP, blows the solo-dev budget.
- **No explicit target** — every design discussion restarts from first principles; decisions drift.

## Consequences

**Positive:**
- Shared numerical target across all ADRs, PRD, and phase plans.
- Rejects over-engineering temptations ("but what if we have 100k tenants?").
- Provides a clear trigger for "v2 scale re-architecture" once approached.

**Negative / Trade-offs:**
- The ceiling is real — breaching it (e.g., a single tenant with 50k services) requires explicit architectural review.
- Some capacity planning requires conservative assumptions per-tenant, which slightly inflates per-tenant baseline cost.
- Public numbers create expectations; must be revisable with clear change control.

**Neutral:**
- Targets will be revisited at each major version; breaching them is a product/architecture event, not a support ticket.

## References

- PRD §7.1 (Non-Functional Requirements — Scale)
- Related ADRs: ADR-0075 (performance SLOs), ADR-0076 (SLA tiers), ADR-0012 pending (RLS), ADR-0001 (PostgreSQL), ADR-0002 (Elasticsearch)
