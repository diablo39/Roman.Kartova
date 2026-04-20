# ADR-0067: Relationship Origin Tracking as First-Class Field

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Domain Model
**Related:** ADR-0045 (agent approval), ADR-0054 (deep scan), ADR-0056 (manual precedence), ADR-0068 (relationship types)

## Context

Relationships in the catalog originate from three sources: manual declarations by users, deep repository scans (ADR-0054), and agent runtime observations (ADR-0045). These sources have different trust levels, different update cadences, and different reasons to disagree. Treating them uniformly erodes trust and blocks the precedence rules (ADR-0056).

## Decision

Every relationship carries a first-class `origin` field with three values: `manual`, `scan`, `agent`. The field is:

- **Required** on every relationship record.
- **Surfaced visually** in the UI (icons, filters) so users always know how a relationship got there.
- **Used for precedence** per ADR-0056: `manual > agent > scan`.
- **Used for access control** on edit: manual relationships can only be edited by authorized users; scan/agent ones can be accepted, rejected, or promoted to manual.
- **Used for filtering**: graph and list views can show or hide by origin.

Additional provenance metadata is captured alongside `origin`:

- `discovered_at` / `last_confirmed_at` timestamps.
- `discovered_by` (scanner version, agent ID, or user ID).
- `source_ref` (file path + line for scan; pod/service UID for agent).

## Rationale

- Trust-by-source is a first-class product concern — users must know where the graph edge came from.
- Explicit origin enables the conflict-queue behavior (ADR-0056) without ambiguity.
- Provenance metadata makes relationships debuggable ("why does the platform think A calls B?").
- Filtering by origin turns into a useful curation workflow ("show me all unreviewed scan-discovered edges").

## Alternatives Considered

- **Single "verified" boolean** — collapses the three sources into two states; loses agent vs scan distinction and the precedence chain.
- **Per-field provenance** — relationships are small records; origin at record-level is sufficient.
- **No distinction (last-write-wins)** — directly rejected by ADR-0056; destroys user trust.

## Consequences

**Positive:**
- Graph trust is explicit and actionable.
- Enables the conflict-queue workflow and origin-based filters.
- Makes debugging relationship noise feasible (source_ref points to the exact evidence).

**Negative / Trade-offs:**
- Every creation path (manual API, scan, agent) must set `origin` correctly — easy to get wrong without enforced plumbing.
- UI must consistently surface origin without becoming visually noisy.

**Neutral:**
- Same pattern can be applied to entity metadata fields as a future enhancement.

## References

- PRD §3.3
- Phase 1: Feature E-04.F-01 (feature-level, shared with ADR-0056)
- Related ADRs: ADR-0045, ADR-0054, ADR-0056, ADR-0068
