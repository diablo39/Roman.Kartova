# ADR-0018: Append-Only Tamper-Evident Audit Log

**Status:** Accepted — rationale re-anchored by [ADR-0106](ADR-0106-drop-regulatory-compliance-scope-gdpr-only.md) (2026-06-18): the audit log and its hash chain are **kept**, but justified by security/forensics and GDPR accountability rather than MiFID II tamper-evidence. The hash-chain layer is now security hardening (candidate for later simplification), not a regulatory requirement.
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Security & Compliance
**Related:** ADR-0017 (retention), ADR-0105 (chain checkpoints), ADR-0106 (re-anchors rationale)

## Context

A trustworthy audit trail is needed for security investigations, change forensics, and GDPR accountability (demonstrating who accessed/changed personal data). Audit entries must not be silently edited or deleted. (Originally motivated by MiFID II tamper-evidence requirements; that driver was dropped by ADR-0106, but the trail remains valuable on security grounds.)

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

- **Merkle-tree hash chain** — stronger, more complex; the per-row hash chain is sufficient for the security-hardening goal (ADR-0106).
- **Immutable object store (S3 Object Lock)** — useful as a periodic archive export; not the primary store for query-latency reasons.
- **External append-only log (QLDB, Immudb)** — vendor / operational overhead.
- **Kafka compacted topic** — wrong tool (compaction loses entries).

## Consequences

**Positive:**
- Trustworthy trail for security investigations + GDPR accountability
- Unified audit across all mutable entities

**Negative / Trade-offs:**
- Table grows large — partitioning by month; bounded by the 180-day retention (ADR-0017) now that there is no 5-year regulatory tier
- Hash-chain verification tooling is security hardening, not a regulator deliverable (ADR-0106)
- Writes must never fail silently — failure policy must be defined (fail-open vs fail-closed)

**Neutral:**
- Retention is governed by ADR-0017 (flat 180 days)

## References

- PRD §7.3
- Phase 0: E-01.F-03.S-03
- ADR-0106 (rationale re-anchored — compliance scope dropped)
