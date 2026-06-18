# ADR-0106: Drop Regulatory-Compliance Scope — GDPR-Only

**Status:** Accepted
**Date:** 2026-06-18
**Deciders:** Roman Głogowski (solo developer)
**Category:** Compliance & Retention
**Supersedes:** ADR-0016 (MiFID II from day one), ADR-0020 (cold-storage archival), ADR-0050 (notification log as MiFID II record)
**Amends:** ADR-0017 (retention), ADR-0018 (audit log rationale), ADR-0019 (soft delete), ADR-0061 (pricing)
**Related:** ADR-0015 (GDPR — retained), ADR-0105 (audit chain checkpoints)

## Context

ADR-0016 committed Kartova to **MiFID II compliance "from day one"** as a fintech-market differentiator. That single decision rippled into a per-tenant `mifid_ii_enabled` flag (ADR-0016), 5-year retention tiers (ADR-0017, ADR-0019), notification-log-as-communication-record (ADR-0050), cold-storage archival to absorb the 5-year volume (ADR-0020), an Enterprise pricing feature + `mifid_ii_flag` column (ADR-0061), and supporting rationale threaded through a dozen other ADRs.

A pivot from MiFID II to **NIS2** was considered (Kartova as a regulated managed-service provider) and also rejected — it would add cybersecurity-posture and incident-reporting obligations without serving the product's core developer-portal value.

None of the MiFID-specific machinery was ever built. The only shipped artifact in this space is the append-only, tamper-evident audit log (ADR-0018, wired in the 2026-06-12 + 2026-06-17 slices), which ADR-0018 itself justified on dual grounds — regulatory tamper-evidence **and** general security/forensics.

## Decision

Drop all regulatory-compliance scope beyond **GDPR** (ADR-0015 stays in force). Concretely:

- **No `mifid_ii_enabled` tenant flag**, and no per-tenant compliance tiering of any kind.
- **Retention is a flat 180-day operational window** for all tenants (ADR-0017). The 5-year tier and the soft-delete 5-year variant (ADR-0019) are removed.
- **The notification log**, if/when built, is an **operational delivery record only** — not a 5-year communication record (ADR-0050 superseded).
- **Cold-storage archival (ADR-0020) is shelved** — there is no regulatory long-tail to archive. Revisit only as pure cost-control if data volumes ever warrant.
- **The append-only audit log + per-tenant hash chain (ADR-0018, ADR-0105) is kept**, re-anchored to **security/forensics + GDPR accountability**. The hash-chain/checkpoint layer is now treated as **security hardening** — a candidate for later simplification, **not** to be ripped out (working, tested code).
- **Pricing (ADR-0061):** the Enterprise "MiFID II compliance flag (5-year retention)" feature and the `mifid_ii_flag` column are removed; retention is no longer a tier differentiator.

## Rationale

- The MiFID-specific machinery was a tenant-facing *product* bet on the fintech market; the differentiator no longer justifies the recurring scope, storage cost, and design constraints — especially for a solo developer pre-MVP.
- Nothing built depends on it, so the cost of dropping it is documentation-only.
- GDPR is genuinely required and stays; the tamper-evident audit log is genuinely useful for security and survives on its own merits.

## Alternatives Considered

- **Keep MiFID II** — recurring storage cost (5-year retention), GDPR-erasure conflicts, and design constraints for a market segment not yet validated. Rejected.
- **Pivot to NIS2 (Kartova as regulated entity)** — adds cybersecurity-posture controls and 24h/72h/1-month incident-reporting obligations; not aligned with the product's core value at this stage. Rejected.
- **Rip out the audit hash chain too** — wasteful churn on working, tested code that has independent security value. Rejected; kept and re-anchored instead.

## Consequences

**Positive:**
- Scope shrinks: no compliance flag, no 5-year retention engine, no comms-record store, no cold-storage archival pipeline.
- GDPR right-to-erasure no longer conflicts with mandated regulatory retention — deletion logic is cleaner.
- Single, flat retention model — simpler mental model and storage cost.

**Negative / Trade-offs:**
- Loses the fintech-compliance go-to-market angle (ADR-0016's stated reason for existing); Kartova competes on developer-experience features, not compliance.
- The audit hash-chain layer now carries weaker justification (security hardening, not mandate) — accepted as low-cost retained hardening.

**Neutral:**
- Zero shipped-code changes required. Affected ADRs and product docs updated; two Phase-0 stories (E-01.F-05.S-02 compliance flag, S-07 comms-record retention) dropped.

## References

- Supersedes/amends: ADR-0016, ADR-0017, ADR-0018, ADR-0019, ADR-0020, ADR-0050, ADR-0061
- GDPR (retained): ADR-0015
- PRD §7.3, §7.4, §11 (Resolved Decision #8 — now GDPR-only)
- Phase 0: E-01.F-03.S-03 (audit log — retained), E-01.F-05 (compliance — GDPR-only)
