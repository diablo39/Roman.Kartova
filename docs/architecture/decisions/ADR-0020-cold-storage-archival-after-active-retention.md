# ADR-0020: Cold-Storage Archival After Active Retention

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Compliance & Retention
**Related:** ADR-0017 (retention), ADR-0018 (audit log)

## Context

Hot storage of 5-year MiFID II retention in PostgreSQL and Elasticsearch is expensive. Much of that data is rarely accessed but must remain retrievable for audits and legal requests. Tenants may also request data export before final purge (PRD §7.4).

## Decision

After the active retention window (180 days / 5 years per ADR-0017), data is automatically moved to cold storage (object storage with infrequent-access or archive tier — e.g., S3 Glacier Instant Retrieval, Azure Cool/Archive, GCS Archive). Cold-stored data remains retrievable on request, with an expected retrieval SLA measured in hours. Tenants can request a full export before any purge.

## Rationale

- Dramatic storage-cost reduction for long-tail regulatory data.
- Retrievability preserved for audits and legal holds.
- Aligns with the cloud-agnostic strategy (ADR-0022) since every major cloud has an archive tier.

## Alternatives Considered

- **No archival (hard delete)** — cannot satisfy MiFID II 5-year requirement.
- **On-demand export only** — doesn't reduce storage cost; still paying hot-tier prices.
- **Hot-only retention** — expensive and unnecessary.

## Consequences

**Positive:**
- Cost-controlled long-term retention
- Uniform archive format enables regulator export

**Negative / Trade-offs:**
- Restore jobs needed for audit queries that span archived periods
- Archive format (e.g., JSON + NDJSON + checksums) must be versioned and stable

**Neutral:**
- The archival pipeline is an internal batch job, not user-facing

## References

- PRD §7.4
