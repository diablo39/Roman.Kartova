# ADR-0021: Data Residency Tracking Per Tenant (No Enforcement Yet)

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Compliance & Retention
**Related:** ADR-0015 (GDPR), ADR-0022 (K8s cloud-agnostic)

## Context

GDPR requires disclosure of where customer data is stored. True multi-region enforcement (EU vs US vs APAC-resident clusters) is expensive and operationally complex for a solo developer pre-revenue. However, tenants need to know and document residency today (PRD §7.3, §7.5).

## Decision

Store a `residency_region` field on each tenant. For MVP, all tenants resolve to a single region (EU for the initial deployment). The field is used for disclosure in the DPA and in tenant settings. Actual multi-region routing and per-region infrastructure is a Phase 9 / v2.0+ concern (Epic E-28).

## Rationale

- Meets MVP GDPR disclosure obligations without the infrastructure cost of true multi-region.
- Lays groundwork — schema and API surface are in place when real multi-region arrives.

## Alternatives Considered

- **Multi-region from day one** — 3-5x infrastructure cost; not viable for solo-dev pre-revenue.
- **Single-region with disclosure only, no tracking field** — loses future-proofing; painful to retrofit once tenants exist.

## Consequences

**Positive:**
- GDPR disclosure requirement satisfied
- Schema ready for future enforcement

**Negative / Trade-offs:**
- Cannot win customers who require US or APAC residency at MVP
- Must be transparent that the field is declarative today, enforced later

**Neutral:**
- Residency field should be immutable once a tenant has data

## References

- PRD §7.3, §7.5
- Phase 0: E-01.F-05.S-08
- Phase 9: Epic E-28
