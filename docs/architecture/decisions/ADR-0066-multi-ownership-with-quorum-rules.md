# ADR-0066: Multi-Ownership with Platform Designation and Quorum Rules

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Domain Model
**Related:** ADR-0065 (hybrid org), ADR-0008 (RBAC), ADR-0073 (lifecycle)

## Context

Shared platform components (e.g., a shared authentication service, shared CI library, shared data platform) are real — they are not owned by one team. A strict single-owner model forces misleading designations ("formally owned by Team X but really everyone owns it") and breaks notification/approval flows for changes that should require cross-team agreement (PRD §3.2).

## Decision

Components support **multiple owning teams** with two additional concepts:

- **Platform flag**: a component marked as "platform" signals it is shared/foundational. Platform components are first-class citizens for cross-team discovery.
- **Quorum rules for sensitive actions**:
  - **Edit** (non-lifecycle metadata): any single co-owner can edit.
  - **Lifecycle change** (deprecate, decommission — see ADR-0073): **requires consent from all co-owners** (or explicit override by an Org Admin, logged in the audit trail).
  - **Ownership changes** (add/remove co-owner): require approval from a majority of existing co-owners.

## Rationale

- Mirrors how shared components actually work in real orgs.
- Protects against unilateral deprecation of a component others depend on — a common source of incidents.
- Single-edit / consensus-deprecate is a pragmatic balance: routine work flows, blast-radius changes require alignment.
- Org Admin override ensures the platform is never deadlocked by an absent co-owner (with full audit).

## Alternatives Considered

- **Single-owner only** — forces artificial single-team designations; breaks for real shared components.
- **Primary + secondary owner** — improvement over single, still insufficient for genuinely shared platforms.
- **Role-based ownership (maintainers/reviewers)** — richer but heavier; deferred as possible future extension.
- **All-co-owners-must-approve-everything** — too high-friction for routine edits.

## Consequences

**Positive:**
- Honest modeling of shared ownership.
- Guards against "silent deprecation" incidents.
- Clear escalation via Org Admin for edge cases.

**Negative / Trade-offs:**
- Quorum UX must handle pending-approval states cleanly.
- Notifications must route to *all* co-owners for lifecycle events (adds to notification engine load — ADR-0047).
- Mis-set ownership can create unnecessary friction; tenants must learn to use co-ownership judiciously.

**Neutral:**
- Audit log (ADR-0018) captures every approval and override.

## References

- PRD §3.2
- Phase 1: Feature E-03.F-05 (feature-level)
- Related ADRs: ADR-0008, ADR-0018, ADR-0047, ADR-0065, ADR-0073
