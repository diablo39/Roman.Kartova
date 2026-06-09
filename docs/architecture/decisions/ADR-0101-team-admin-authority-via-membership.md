# ADR-0101: Team-Admin Authority Is Per-Team Membership, Not a Realm Role

**Status:** Accepted
**Date:** 2026-06-09
**Deciders:** Roman Głogowski (solo developer)
**Category:** Authentication & Authorization
**Related:** ADR-0008 (five fixed RBAC roles), ADR-0090 (tenant scope), ADR-0100 (identity scope)
**Supersedes:** the team-scoped permission model in `docs/superpowers/specs/2026-05-25-slice-8-team-management-and-team-scoped-permissions-design.md` §5

## Context

Slice 7 introduced a `TeamAdmin` KeyCloak realm role; slice 8 made it load-bearing by adding a team-scoped resource gate (`TeamAdminOfThis`). The combination requires **two independent keys** to mutate a team:

1. **Claim gate** — the route requires a `team.*` permission claim (`team.metadata.edit` / `team.delete` / `team.members.manage`), carried only by the `TeamAdmin` and `OrgAdmin` realm roles.
2. **Resource gate** — `TeamAdminOfThisHandler` requires an `Admin`-level membership on *that specific team* (`TeamMemberships.Any(m => m.TeamId == resource.TeamId && m.Role == TeamRoleKind.Admin)`), or `OrgAdmin`.

The two keys check unrelated things, producing a silent footgun: a user promoted to **Admin of team X** but holding the org-entry realm role `Member` is **silently 403'd** — the claim gate fails before the resource gate runs. An OrgAdmin doing people-management reasonably expects "made Bob admin of Team X → Bob manages Team X," which does not hold. Restoring it requires a second, unrelated action (changing Bob's realm role via re-invitation) in a different screen.

The realm role adds little beyond the redundancy: the SPA already gates per-team affordances on `teamAdminTeamIds` (membership-derived), not on the flat claim; OrgAdmin's authority comes from the resource-gate bypass, not the claim; and the slice-8 spec itself states the design intent — *"per-team role separates 'membership' from 'team-admin power'"* — making the per-team `Admin` membership the intended authority bearer.

## Decision

**Team-admin authority is conferred solely by an `Admin`-level per-team membership** (`TeamRoleKind.Admin`), enforced by the `TeamAdminOfThis` resource gate. The `TeamAdmin` realm role and its three team-mutation permission claims (`team.metadata.edit`, `team.delete`, `team.members.manage`) are **removed**.

- Realm roles become `Viewer` / `Member` / `OrgAdmin` (invitation-assignable), plus orthogonal `PlatformAdmin` and forward-compat `ServiceAccount` (ADR-0009).
- The five team-mutation routes drop their permission-claim gate (falling back to authenticated + tenant scope per ADR-0090); the inline `TeamAdminOfThis` resource gate is the **sole** authorization. `TeamAdminOfThisHandler` is unchanged.
- Team **creation** stays OrgAdmin-only (`team.create` retained).
- To make someone a team admin: invite/reuse them as `Member`, then add them to the team with role `Admin` via `POST /teams/{id}/members`. There is no one-step org-wide grant — by design, you cannot admin a team you do not belong to.

This amends ADR-0008's role taxonomy (which had already drifted from the implemented model) and supersedes the slice-8 design spec §5 permission model.

## Consequences

### Positive

- One coherent concept: *Admin of a team = the `Admin` membership row on that team*. Single source of truth; the silent-403 footgun is eliminated.
- An honest claim set — no permission a user holds but cannot exercise org-wide.
- Smaller realm-role surface; the invitation dropdown maps cleanly to org-entry roles.

### Negative / trade-offs

- The resource gate (`TeamAdminOfThisHandler`) becomes the *only* authorization layer for team mutations — the route-level defense-in-depth the claim gate provided is gone. Mitigated by expanded `TeamAdminOfThisHandlerTests` plus integration coverage that locks the gate's allow/deny behavior.
- "Make someone a team admin" is now a two-step action (join as `Member`, then add as `Admin`) rather than a single invitation role. Accepted: it reflects reality (you cannot admin a team you are not in) and removes the cross-screen coordination the old model silently required.
- Lateral spread: any team `Admin` can promote another tenant member to co-`Admin` of *their* team. Accepted — scoped to one team and expected for a self-service portal.

### Migration

Pre-production (no live tenants), so a clean break: remove the `TeamAdmin` realm role from `deploy/keycloak/kartova-realm.json`, `KartovaRoles`, the invitation role enum, and the role→permission map. Re-seed `team-admin@orga.kartova.local` as a `Member` realm role plus an `Admin` membership of a seeded demo team (via `Kartova.Migrator/DevSeed.cs`), so the dev stack exercises the new realm-`Member`-who-is-team-`Admin` model end-to-end. No back-compat shim.

### Upgrade path

If an org-wide "eligible to be a team admin anywhere" gate is ever genuinely needed, reintroduce it as an explicit capability claim plus the resource gate — not by reviving a realm role that doubles as both capability and (missing) scope.

## References

- Design spec: `docs/superpowers/specs/2026-06-09-team-admin-membership-authority-design.md`
- Superseded model: `docs/superpowers/specs/2026-05-25-slice-8-team-management-and-team-scoped-permissions-design.md` §5
- `TeamAdminOfThisHandler` (`src/Kartova.SharedKernel.AspNetCore/AuthorizationHandlers/`)
