# ADR-0018: Append-Only Tamper-Evident Audit Log

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Compliance & Retention
**Related:** ADR-0016 (MiFID II), ADR-0017 (retention)

## Context

MiFID II requires tamper-evident records (PRD §7.3). Regulators expect that audit entries cannot be silently edited or deleted. Non-regulated tenants also benefit from a trustworthy audit trail for security investigations and change forensics.

## Decision

Maintain a dedicated insert-only audit table in PostgreSQL:

- No `UPDATE` or `DELETE` from application code; database role has insert-only grants on this table
- Covers entity CRUD, relationship changes, policy changes, config changes, RBAC changes
- Every entry captures: timestamp (UTC), tenant, actor (user or service account), action, target, diff payload
- Rows include a hash chain / row hash for tamper evidence (hash of previous row + current row payload)

## Rationale

- Database-enforced insert-only semantics beat application-enforced.
- Hash chaining detects silent modification by a compromised DBA or operator.
- PostgreSQL (ADR-0001) provides the granular grants needed.

## Alternatives Considered

- **Merkle-tree hash chain** — stronger, more complex; the per-row hash chain is sufficient for MiFID II tamper-evidence.
- **Immutable object store (S3 Object Lock)** — useful as a periodic archive export; not the primary store for query-latency reasons.
- **External append-only log (QLDB, Immudb)** — vendor / operational overhead.
- **Kafka compacted topic** — wrong tool (compaction loses entries).

## Consequences

**Positive:**
- MiFID II tamper-evidence satisfied
- Unified audit across all mutable entities

**Negative / Trade-offs:**
- Table grows large — partitioning by month and archival (ADR-0020) required
- Hash chain verification tooling must be built for regulators
- Writes must never fail silently — failure policy must be defined (fail-open vs fail-closed)

**Neutral:**
- Retention is governed by ADR-0017

## References

- PRD §7.3
- Phase 0: E-01.F-03.S-03
