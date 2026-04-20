# ADR-0075: Performance SLOs — Catalog p95 < 200 ms, Search p95 < 500 ms, Scan < 5 min/Repo

**Status:** Accepted
**Date:** 2026-04-20
**Deciders:** Roman Głogowski (solo developer)
**Category:** Scale & Performance
**Related:** ADR-0074 (scale targets), ADR-0076 (SLA tiers), ADR-0001 (PostgreSQL), ADR-0002 (Elasticsearch), ADR-0055 (scan timeouts)

## Context

Without explicit performance budgets, "fast enough" becomes a moving target dictated by the next bug report. Design decisions about caching, indexing, pagination, and scan parallelism need concrete latency ceilings to measure against. "Slow catalog" is the most common dev-portal complaint in the category (Backstage, ServiceNow); Kartova must not inherit that failure mode (PRD §7.2).

## Decision

**Platform performance SLOs (p95, steady-state, within scale envelope ADR-0074):**

| Operation | p95 Target | Applies to |
|---|---|---|
| Catalog reads (entity details, list views ≤100 items) | **< 200 ms** | Authenticated UI + API |
| Search queries (full-text, typed filters) | **< 500 ms** | Elasticsearch-backed endpoints |
| Repository deep scan | **< 5 min/repo** | Agent + server-side scan jobs (ADR-0055) |
| Graph queries (dependency view, depth ≤3) | **< 500 ms** | Entity graph navigation (ADR-0040) |
| Dashboard aggregations | **< 1 s** | Scorecards, lifecycle overviews |

**Measurement:**
- p95 measured over rolling 7-day windows per endpoint per tenant.
- Measured at the API edge (excludes client render time).
- Reported via Prometheus (ADR-0059); error budget burndown tracked.

**Enforcement:**
- SLO regressions block release promotion from staging.
- Endpoints that cannot meet budget must either be paginated/backgrounded, or documented as an exception with a target date.

## Rationale

- 200 ms is the human perceptual threshold for "instant" — catalog reads must land there for the product to feel like a portal, not a report.
- 500 ms for search allows for Elasticsearch query + aggregation overhead while remaining conversational.
- 5 min/repo scan budget matches developer patience for CI-like operations; longer than this requires visible progress UI.
- p95 (not p99) is the practical SLO — p99 is heavily influenced by cold caches and rare long-tail operations; tracking both but gating on p95.
- Per-tier SLOs (paid vs free) explicitly rejected — simplicity wins, and Kartova doesn't have a free tier worth discriminating against.

## Alternatives Considered

- **Looser SLOs (e.g., catalog p95 < 1 s)** — ships faster but loses the "feels snappy" product differentiator; developers are particularly intolerant of slow dev-tooling.
- **Tighter SLOs (catalog p95 < 50 ms)** — forces aggressive caching and pre-computation for marginal perceptual gain; premature for MVP scale.
- **Per-plan SLOs** — operational and communication complexity for little real-world benefit at MVP tiers.
- **No formal SLOs** — what every portal that felt slow also didn't have.

## Consequences

**Positive:**
- Every performance-sensitive design decision has a concrete target to aim at.
- Regressions are caught pre-production via release-gating.
- Public SLOs are a sales asset vs competitors with unstated (and usually worse) numbers.

**Negative / Trade-offs:**
- Some operations (e.g., complex cross-tenant analytics) can't hit these budgets and must be redesigned as async/backgrounded.
- Requires investment in load-testing infrastructure before revenue justifies it.
- Error-budget accounting adds operational overhead — acceptable given the solo-dev leverage it provides (auto-pause deploys on budget burn).

**Neutral:**
- Numbers will be revisited as load-test data arrives; directional targets set now, calibrated later.

## References

- PRD §7.2 (Performance targets)
- Related ADRs: ADR-0074 (scale targets), ADR-0076 (SLA tiers), ADR-0001 (PostgreSQL), ADR-0002 (Elasticsearch), ADR-0055 (scan timeouts), ADR-0059 (Prometheus metrics)
