# ADR-0017: Default 180-Day Retention, 5-Year for MiFID II Tenants

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Compliance & Retention
**Related:** ADR-0016 (MiFID), ADR-0019 (soft delete), ADR-0020 (archival)

## Context

Operational history (uptime, deployments, audit, scans, incidents) accumulates quickly. Keeping everything forever is expensive; keeping too little hurts debugging, scorecards, and compliance. Different data types, and different tenant profiles, justify different retention (PRD §7.4).

## Decision

Default retention:

- Uptime / deployment / audit / scan / incident history: **180 days**
- Soft-deleted entities: **30 days** before purge

MiFID II tenants (ADR-0016):

- All retention windows above become **5 years**

Archival to cold storage kicks in after the active retention window (ADR-0020).

## Rationale

- 180 days is sufficient for trend analysis, scorecards, and debugging without paying enterprise-grade storage cost by default.
- 5 years satisfies MiFID II article 16 record-keeping minimums.
- Two-tier retention avoids penalizing non-regulated tenants.

## Alternatives Considered

- **90 days** — insufficient for quarterly trends.
- **365 days** — double the cost without much added utility.
- **Configurable only (no default)** — every tenant must choose; poor onboarding UX.
- **Tiered by data type** — useful refinement; can be added later without breaking the current defaults.

## Consequences

**Positive:**
- Predictable storage cost per tenant
- Single policy mental model

**Negative / Trade-offs:**
- Customers wanting 365-day trends on the free/default tier will be frustrated — must document clearly
- Migration of retention windows requires care not to break audit-log continuity

**Neutral:**
- Retention windows should be surfaced in the UI per data type

## References

- PRD §7.4, §11 (Resolved Decision #7)
- Phase 0: E-01.F-05.S-01
