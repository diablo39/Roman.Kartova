# ADR-0103: Application Ownership Is a Required Team; the Individual Is Created-By Provenance

**Status:** Accepted
**Date:** 2026-06-10
**Deciders:** Roman Głogowski (solo developer)
**Category:** Domain Model
**Related:** ADR-0066 (multi-ownership/quorum — future co-owning teams extend this), ADR-0082 (modular monolith — cross-module team-existence port), ADR-0101 (team-admin authority via per-team membership), ADR-0102 (user offboarding — hard delete, no app reassignment)

## Context

Kartova models the same catalog space as Backstage and Compass. Both products are unambiguous about ownership:

- **Backstage:** every entity has a **required** `spec.owner` field that resolves to a **Group (team)** by strong convention (a User is an explicitly discouraged fallback). There is no ownerless entity. "Who created it" is provenance — captured in git history and `managed-by-location`, not in an ownership field.
- **Compass (Atlassian):** a component's **Owner is an Atlassian Team**; authorship lives in the activity log.

The model implemented through slice 9 diverged from both references: `Application.OwnerUserId` was a required **individual** owner. This conflated two distinct concepts:

- **Ownership** — accountability, discovery routing, team responsibility, scored by scorecards (E-03.F-05, ADR-0070).
- **Authorship / provenance** — who registered the entity; immutable audit context, not a governance concept.

The conflation surfaced a downstream problem in slice 10: offboarding a member who owned apps required a successor-reassignment subsystem — a keeper of a concept that should never have existed in the catalog domain model. Removing the conflation eliminates the subsystem.

## Decision

1. **`Application.TeamId` is required — the owning team.** No ownerless apps. A tenant must have at least one team before it can register applications (the onboarding wizard, E-09, must guarantee this first team; noted here, not solved here).

2. **The individual is `CreatedByUserId` — immutable provenance.** Set once at creation, never reassigned or nulled. When the creator is later offboarded, the id stops resolving to a live user; the UI displays "former member" or Unknown gracefully (via `IUserDirectory` miss). This is the same pattern Backstage uses for git-history provenance.

3. **Registration is membership-gated.** An OrgAdmin may register an application into any tenant team. A Member may register only into teams they belong to. This mirrors the `assign-team` resource gate (`ApplicationTeamScopedHandler`) — consistent authz, closes the asymmetry where assign-team was gated but register was not.

4. **Offboarding does NOT reassign owned apps.** The owning team retains them; the offboarded member's `CreatedByUserId` is immutable history. No successor picker, no `IApplicationOwnerReassigner` port. See ADR-0102.

## Consequences

### Positive

- Ownership matches both Backstage and Compass reference models — future catalog users familiar with either tool will find the semantics intuitive.
- No ownerless entities can exist; the `NOT NULL` constraint on `team_id` enforces the invariant at the DB layer.
- Offboarding is simplified: no reassignment subsystem (deleted `IApplicationOwnerReassigner` port, `Application.ReassignOwner`, the successor-picker dialog, `IApplicationOwnerReassigner` wiring).
- Register and assign-team authz are consistent: both gates use team membership, closing an authz asymmetry.
- `CreatedByUserId` is correct provenance semantics: immutable, survives the creator's departure, analogous to git authorship.

### Negative / Trade-offs

- Every tenant must have at least one team before it can own apps. The onboarding wizard (E-09) must guarantee a first team is created during org setup; until that slice ships, seed data covers the dev/staging case.
- `CreatedByUserId` values can dangle after an offboarding: the id references a deleted identity. The application resolves this gracefully ("former member") rather than by nulling or remapping the field; this is a deliberate choice (immutability > accuracy-of-live-resolution).
- The migration (`RealignApplicationOwnership`) makes `team_id NOT NULL` and must backfill any existing rows to a placeholder team. Pre-production only (no live tenants per ADR-0082); zero data-loss risk at this stage.

### Neutral

- Richer **co-ownership** (multiple owning teams + quorum rules) is the subject of ADR-0066 and E-03.F-05. This ADR establishes the required-single-team base that co-ownership will build on; ADR-0066 is not changed.
- The `CreatedByUserId` facet (renamed from the prior `OwnerUserId` filter) is retained for discovery (`GET /applications?createdByUserId=…`) and for the user-detail "Applications created" card.

## Alternatives Considered

**Keep individual required owner (rejected).** `Application.OwnerUserId` as a required individual owner conflates ownership with authorship. It contradicts both Backstage and Compass, produces the unnecessary reassignment-on-offboard subsystem, and models a concept ("individual owns an app") that is not meaningful at org scale — a team, not a person, is accountable for a service.

**Team optional; creator as fallback owner (rejected).** A "Backstage-light" option where the team is recommended but not required. Rejected in favor of Compass-strict required-team: optional ownership creates ownerless entities, confounds scorecards and notification routing, and hides onboarding gaps.

**Make `CreatedByUserId` mutable / reassign on offboard (rejected).** Rewriting the `CreatedByUserId` field on offboarding would rewrite history — analogous to force-pushing git authorship. Rejected: provenance should be immutable. The correct reaction to an offboarded creator is graceful resolution ("former member"), not data mutation.

## References

- Slice 10 amendment spec: `docs/superpowers/specs/2026-06-10-slice-10-ownership-realignment-design.md`
- Related stories: E-02.F-01.S-01 (register application — required owning team + created-by provenance), E-03.F-01.S-07 (offboard member — no app reassignment)
- ADR-0066: Multi-Ownership with Quorum Rules (future co-owning teams extend this base)
- ADR-0082: Modular Monolith Architecture (cross-module team-existence port `IOrganizationTeamExistenceChecker`)
- ADR-0101: Team-Admin Authority Is Per-Team Membership, Not a Realm Role
- ADR-0102: User Offboarding Is Hard Delete (offboarding does not reassign apps; see §Decision 4 and §Consequences above)
