# ADR-0056: Re-Scan Never Overrides Manual Relationships (Conflict Queue Instead)

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Scan / Import Architecture / Data Model
**Related:** ADR-0045 (approval workflow), ADR-0054 (deep scan), ADR-0067 (relationship origin)

## Context

Relationships between entities may be declared manually by catalog owners or derived automatically by the scanner/agent (ADR-0054, ADR-0045). When these disagree — e.g., a user deletes a spurious auto-discovered dependency, then a rescan re-discovers it — the system needs a policy. Overwriting user intent erodes trust; silently dropping scanner findings erodes discovery value.

## Decision

**Manual data always wins over auto-discovered data.** Specifically:

- Manual relationships are never overwritten or deleted by a re-scan.
- When a re-scan produces a relationship that conflicts with a manual one (e.g., manual = "depends-on", scanner = "consumes-api-from" on the same pair), the conflict is routed to a **conflict review queue** rather than resolved automatically.
- User-rejected auto-discovered relationships remain rejected across subsequent scans (the rejection is persisted as a signal).
- Every relationship carries an `origin` field (ADR-0067): `manual`, `scan`, `agent`. Precedence follows `manual > agent > scan`.

## Rationale

- User-curated data represents ground truth the platform cannot derive.
- A single "my edit got overwritten" incident permanently damages trust in auto-discovery.
- A conflict queue makes disagreement visible rather than silently lost — the user gets to decide.
- Persisting rejections prevents the "whack-a-mole" experience where the same false-positive keeps reappearing.

## Alternatives Considered

- **Last-write-wins** — trivially simple; destroys user trust on first collision.
- **Merge rules (field-level)** — relationships are small enough that field-level merge has little benefit over whole-record precedence.
- **Prompt-on-conflict UI only** — works, but blocks the scan pipeline on UI interaction; a queue decouples the two.
- **Per-field provenance** — valuable for entity metadata, but relationships are edge objects where record-level origin suffices.

## Consequences

**Positive:**
- Tenants can confidently curate without fear of auto-sync clobbering their work.
- Conflict queue becomes a productive workflow surface — users can approve corrections.
- Persistent rejection stops repeated noise.

**Negative / Trade-offs:**
- Conflict queue requires UI, triage ergonomics, and notification plumbing (ADR-0047).
- A large initial import can create a substantial conflict backlog; bulk actions in the queue are needed.
- Determining "same relationship" requires canonicalization rules (same endpoints × same type).

**Neutral:**
- Same precedence model applies to entity metadata fields where feasible (future).

## References

- PRD §3.3
- Phase 1: Feature E-04.F-01 (feature-level)
- Phase 2: E-08.F-03.S-03, E-08.F-03.S-04
- Related ADRs: ADR-0045, ADR-0047, ADR-0054, ADR-0067
