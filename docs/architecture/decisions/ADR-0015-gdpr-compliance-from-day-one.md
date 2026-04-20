# ADR-0015: GDPR Compliance From Day One

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Compliance & Retention
**Related:** ADR-0016 (MiFID II), ADR-0019 (soft delete), ADR-0021 (residency), ADR-0078 (no secrets)

## Context

Kartova targets the EU market. GDPR compliance is a prerequisite for selling to any EU organization, not an optional feature. Bolting compliance on later requires invasive schema and process changes — it must be a Phase 0 foundation (PRD §7.3).

## Decision

Implement from MVP:

- Right to erasure across PostgreSQL, Elasticsearch, and blob storage
- Data portability export (JSON and CSV)
- Consent capture and audit
- DPA template accessible to every tenant
- 72-hour breach notification process
- DPO contact published
- Residency tracking per tenant (ADR-0021)

## Rationale

- EU market access requires it.
- Cheaper and cleaner to build in day one than retrofit.
- Design partners (ADR-0079) will demand it before piloting.

## Alternatives Considered

- **GDPR on-demand per tenant** — fragmented, higher risk of missed cascades.
- **Third-party compliance platform (OneTrust/Osano)** — useful for cookie consent but does not implement erasure in our data — still need the core mechanics.

## Consequences

**Positive:**
- EU-ready from MVP
- Forces discipline on data modeling (every PII touchpoint is known)

**Negative / Trade-offs:**
- Erasure cascades add implementation work across DB + search + blob (ADR-0004 pending)
- Soft-delete vs hard-delete tension with MiFID II (ADR-0019)

**Neutral:**
- Requires ongoing attention (records of processing, DPIAs for new features)

## References

- PRD §7.3
- Phase 0: Feature E-01.F-05
