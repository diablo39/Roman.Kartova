# ADR-0019: Soft Delete With 30-Day Purge (5 Years for MiFID II)

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Compliance & Retention
**Related:** ADR-0015 (GDPR), ADR-0016 (MiFID), ADR-0017 (retention)

## Context

Accidental deletions happen. Users need a recovery window (PRD §7.4). Regulated tenants need deletions preserved longer. GDPR right-to-erasure on the other hand demands actual purge on request.

## Decision

Entity deletion is a two-phase operation:

1. **Soft delete** — row is marked `deleted_at`, hidden from UI/API; relationships are broken/queued for review.
2. **Purge** — after 30 days (default) or 5 years (MiFID II tenants), the row and dependent data are physically removed from PostgreSQL, Elasticsearch, and blob storage.

GDPR erasure requests bypass the 30-day window and purge immediately (with audit trail of the erasure act itself).

## Rationale

- 30 days balances recoverability with storage costs.
- 5-year window for MiFID II matches ADR-0016/ADR-0017.
- GDPR erasure taking precedence is required by law; must be clearly documented.

## Alternatives Considered

- **Hard delete immediate** — terrible UX, unrecoverable mistakes.
- **7-day window** — too short in practice for users who only notice missing data at month-end.
- **Tombstone forever** — storage cost and conflicts with GDPR.
- **Configurable** — later enhancement; default must exist.

## Consequences

**Positive:**
- Safety net for users
- Clear regulatory alignment

**Negative / Trade-offs:**
- Soft-deleted rows must be hidden in every query path — enforced via repository pattern and database views
- GDPR erasure path must cascade across all stores and be tested
- Relationship invariants during soft-delete limbo need careful handling

**Neutral:**
- Audit log records both the soft-delete and the eventual purge

## References

- PRD §7.4
