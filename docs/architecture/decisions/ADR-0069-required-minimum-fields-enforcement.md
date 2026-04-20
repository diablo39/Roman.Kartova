# ADR-0069: Required Minimum Fields Enforced at Creation and Import

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Domain Model / Data Quality
**Related:** ADR-0070 (scorecards), ADR-0071 (maturity), ADR-0054 (deep scan)

## Context

The catalog is only valuable if its entries carry enough information to be useful — at minimum, an owner (who to ask), a name (how to refer to it), and a description (what it is) (PRD §4.2.4). Soft enforcement ("warnings only") consistently produces catalogs full of half-filled entries that erode trust. Hard enforcement at the creation path is the only reliable baseline.

## Decision

Every entity creation path — manual creation, API, CLI, import from scan — **rejects** entities missing any of the required minimum fields:

- `owner` (team or user reference, resolvable in RBAC — ADR-0008)
- `name` (non-empty, tenant-unique within type)
- `description` (non-empty, length bounds enforced)

Failures return actionable errors pointing at the missing fields. Bulk imports apply per-item validation and report partial success (ADR-0032): valid items are created, invalid items come back with field-specific errors. The required field set is **platform-fixed for MVP**; per-org configurable requirements (beyond these three) belong to the scorecard framework (ADR-0070), not to creation gating.

## Rationale

- Ownership unknown = no one to notify on incidents, no one accountable for quality.
- Name and description are the minimum for any catalog entry to be findable and understandable.
- Hard enforcement at creation prevents the "catalog entropy" problem where missing fields silently accumulate.
- Keeping the gated field set minimal avoids blocking imports of real repos that have partial metadata — anything beyond owner/name/description is measured by scorecards, not enforced.

## Alternatives Considered

- **Soft enforcement (warnings only)** — rejected: in practice produces low-quality catalogs.
- **Completely configurable required fields** — rejected for creation gating; moves to scorecards where per-org variation is correct.
- **Import-only exception** — rejected: lets bulk imports pollute the catalog in exactly the way we're trying to prevent.

## Consequences

**Positive:**
- Baseline data quality is guaranteed by construction, not by policy hope.
- Every entity is findable (name), understandable (description), and actionable (owner).
- Scorecards (ADR-0070) can focus on higher-order quality concerns.

**Negative / Trade-offs:**
- Initial imports may reject items lacking ownership metadata — remediation requires setting default-owner rules or pre-import enrichment.
- CI/CD pipelines adding entities must supply all three fields; missing fields surface as build failures.

**Neutral:**
- Future ADRs can extend the set if a new field rises to platform-wide essential status.

## References

- PRD §4.2.4
- Phase 1: E-02.F-01.S-05
- Related ADRs: ADR-0008, ADR-0032, ADR-0054, ADR-0070, ADR-0071
