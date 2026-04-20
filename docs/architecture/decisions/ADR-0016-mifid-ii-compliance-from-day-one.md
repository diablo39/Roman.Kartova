# ADR-0016: MiFID II Compliance From Day One

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Compliance & Retention
**Related:** ADR-0015 (GDPR), ADR-0017 (retention), ADR-0018 (audit log), ADR-0050 (notification retention)

## Context

Fintech is a primary design-partner target (PRD §12). MiFID II imposes tamper-evident record-keeping and 5-year retention of communications — requirements that shape storage, audit, and deletion behavior. These cannot be implemented late without schema changes.

## Decision

Every tenant carries a boolean `mifid_ii_enabled` flag. When enabled:

- Audit log entries are retained 5 years (ADR-0017, ADR-0018)
- Soft-deleted records are retained 5 years (ADR-0019)
- Notifications/communications log retained 5 years (ADR-0050)
- Audit log is append-only and tamper-evident

Non-MiFID tenants use the shorter defaults (ADR-0017).

## Rationale

- Design-partner differentiator vs Backstage and Port.
- Flag-based model avoids forcing the costs on non-financial tenants.
- Tamper-evidence is required regardless of retention length, so it is implemented globally (ADR-0018).

## Alternatives Considered

- **Defer to v2 / enterprise tier** — loses fintech design partners; misses a concrete differentiator.
- **Paid add-on only** — same effect as a flag; keep the flag but price it separately at billing layer.

## Consequences

**Positive:**
- Fintech market access from MVP
- Single codepath, flag-gated behavior

**Negative / Trade-offs:**
- Storage cost for 5-year retention must be modeled (cold storage per ADR-0020 helps)
- GDPR right-to-erasure conflicts must be resolved per record type (regulatory retention generally prevails for required records)

**Neutral:**
- Flag must be immutable-by-default once enabled to prevent audit-trail gaps

## References

- PRD §7.3, §7.4
- Phase 0: E-01.F-03.S-03, E-01.F-05.S-01, E-01.F-05.S-07
