# Design — Team-admin authority via per-team membership (remove the `TeamAdmin` realm role)

**Date:** 2026-06-09
**Author:** Roman Głogowski (AI-assisted)
**Status:** Approved (pre-implementation)
**ADR:** ADR-0101 (companion decision record)
**Supersedes:** the team-scoped permission model in `docs/superpowers/specs/2026-05-25-slice-8-team-management-and-team-scoped-permissions-design.md` §5
**Module:** Organization (+ SharedKernel multitenancy, SPA auth)

## 1. Why this change

Slice 7 introduced a `TeamAdmin` *realm role*; slice 8 made it load-bearing with a team-scoped resource gate (`TeamAdminOfThis`). The result is a **two-key authorization model** for every team mutation:

1. **Claim gate** — the route requires a `team.*` permission claim (`team.metadata.edit` / `team.delete` / `team.members.manage`), which only the `TeamAdmin` and `OrgAdmin` realm roles carry.
2. **Resource gate** — `TeamAdminOfThisHandler` requires an *Admin-level membership row on that specific team* (`TeamMemberships.Any(m => m.TeamId == resource.TeamId && m.Role == TeamRoleKind.Admin)`), or `OrgAdmin`.

These two keys check different things, producing a silent footgun:

| Realm role | Membership on team X | Result today |
|---|---|---|
| `TeamAdmin` | `Admin` | ✅ manages X |
| `TeamAdmin` | `Member` | ❌ 403 |
| **`Member`** | **`Admin`** | ❌ **403 — silently** (claim gate fails before the resource gate runs) |
| `Member` | `Member` | ❌ 403 |

Row 3 is the defect: an OrgAdmin promotes a user to **Admin of Team X** and reasonably expects them to manage it, but they silently 403 because their org-entry realm role is `Member`. Fixing it requires a second, unrelated action in a different screen (changing the realm role via re-invitation).

The slice-8 spec decision table itself states the intent — *"Per-team role separates 'membership' from 'team-admin power'"* — so the per-team `Admin` membership is meant to be the authority bearer. The realm role layered on top is redundant and contradicts that intent. The SPA already gates per-team affordances on `teamAdminTeamIds` (derived from membership), not on the flat claim, so the realm role buys almost nothing on the UI side either.

**Decision:** collapse `TeamAdmin` into per-team `Admin` membership. The `TeamAdminOfThis` resource gate becomes the *sole* authorization for team mutations; the realm role and its three team-mutation permission claims are removed.

## 2. Decisions (resolved during brainstorming)

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | Team Admins self-manage their own team (add/remove members, mint other Admins of *that* team); OrgAdmin manages any team. | Matches today's `TeamAdminOfThis` behavior; teams stay self-service. Lateral spread (a team Admin can co-opt another tenant member into their team) is accepted — it is scoped to that one team and is the normal expectation for a self-service portal. |
| 2 | Team **creation** stays OrgAdmin-only (`team.create` unchanged). | Team creation is an org-structural act. No self-service team creation (YAGNI). |
| 3 | Clean break: remove the `TeamAdmin` realm role entirely (KeyCloak realm JSON, `KartovaRoles`, invitation enum, permission map). No back-compat shim. | Pre-production (no live tenants). A dead role would leave the footgun half-present. |
| 4 | Re-seed `team-admin@orga.kartova.local` as a `Member` realm role **+** an `Admin` membership of a seeded demo team. | Keeps the dev stack exercising the exact new model (realm-`Member` who is team-`Admin`) end-to-end in `docker compose up`. |

**Non-goals (YAGNI):** no auto-syncing a realm role to membership state; no self-service team creation.

## 3. Target role model

- **Realm roles, invitation-assignable** (`KartovaRoles.All`): `Viewer`, `Member`, `OrgAdmin`. The `TeamAdmin` constant is removed. `PlatformAdmin` (orthogonal) and `ServiceAccount` (forward-compat, ADR-0009) are unchanged.
- **"Team admin" is no longer a role.** It is the per-team membership role `TeamRoleKind.Admin` (enum unchanged). One concept, one source of truth.

## 4. Authorization changes

`src/Modules/Organization/Kartova.Organization.Infrastructure/TeamEndpointDelegates.cs` (`TeamRoutes.MapTo`):

| Route | Before | After |
|---|---|---|
| `GET /teams`, `GET /teams/{id}` | `team.read` | `team.read` (unchanged) |
| `POST /teams` | `team.create` | `team.create` (unchanged) |
| `PUT /teams/{id}` | `RequireAuthorization(team.metadata.edit)` + inline `TeamAdminOfThis` | `RequireAuthorization()` + inline `TeamAdminOfThis` |
| `DELETE /teams/{id}` | `RequireAuthorization(team.delete)` + inline `TeamAdminOfThis` | `RequireAuthorization()` + inline `TeamAdminOfThis` |
| `POST/DELETE/PUT /teams/{id}/members[/{userId}]` | `RequireAuthorization(team.members.manage)` + inline `TeamAdminOfThis` | `RequireAuthorization()` + inline `TeamAdminOfThis` |

- The 5 mutation routes drop the permission-claim gate. `.RequireAuthorization()` with no policy applies ASP.NET Core's default policy (`RequireAuthenticatedUser`); tenant scope is still enforced by `TenantScopeBeginMiddleware` (ADR-0090), unchanged. The inline `LoadAndAuthorizeTeamAsync` → `TeamAdminOfThis` resource policy becomes the **sole** authorization.
- `TeamAdminOfThisHandler` is **unchanged** — OrgAdmin bypass + per-team `Admin` membership. It is the keeper.
- A realm-`Member` who holds `Admin` membership on team X can now manage X (the silent-403 is fixed); they still 403 on team Y.
- **Security note:** removing the claim gate means the resource gate is the *only* layer between an authenticated tenant member and a team mutation. This is the direct and intended consequence of "team Admins are realm-Members". `TeamAdminOfThisHandlerTests` is expanded (§8) to lock the gate's behavior, replacing the defense-in-depth the claim gate previously provided.

## 5. Permission constants & role→permission map

- **`src/Kartova.SharedKernel/Multitenancy/KartovaPermissions.cs`:** remove `TeamMetadataEdit`, `TeamDelete`, `TeamMembersManage`. Keep `TeamRead`, `TeamCreate`.
- **`src/Kartova.SharedKernel/Multitenancy/KartovaRolePermissions.cs`:** delete the `[TeamAdmin]` entry; remove the three deleted constants from the `OrgAdmin` set too (OrgAdmin's authority on teams is the resource-gate bypass, not a claim). Resulting sets:
  - **Viewer / Member:** unchanged.
  - **OrgAdmin:** loses the three team-mutation claims; keeps `team.read` + `team.create` + all catalog/org permissions. No functional loss — OrgAdmin bypasses the resource gate via `IsInRole(OrgAdmin)`.
- **`src/Kartova.SharedKernel/Multitenancy/KartovaRoles.cs`:** remove the `TeamAdmin` constant and drop it from `All`.

## 6. Invitation contract & SPA

- `CreateInvitationRequest.Role` is validated against the new `KartovaRoles.All` (`Viewer` / `Member` / `OrgAdmin`). Inviting `"TeamAdmin"` now returns **422 Unprocessable Entity** (`CreateInvitationError.Validation`, the existing mapping in `InvitationEndpointDelegates.cs`). (XML-doc on `CreateInvitationRequest` + the validation detail string updated to drop `TeamAdmin`.)
- **SPA:**
  - `web/src/features/organization/schemas/inviteUser.ts` — drop `TeamAdmin` from the role zod enum / dropdown options.
  - `web/src/shared/auth/permissions.ts` + `web/src/shared/auth/permissions.snapshot.json` — remove the three permission keys. The C#↔TS drift sentinel (`Ts_snapshot_equals_csharp_KartovaPermissions_All`) keeps both sides in lockstep; regenerate the snapshot.
  - Per-team button gating already uses `teamAdminTeamIds` (membership-derived) — **no change needed**.
- **New "make someone a team admin" flow** (documented in ADR-0101): invite (or reuse an existing) user as `Member`, then OrgAdmin (or an existing Admin of the team) adds them via `POST /teams/{id}/members` with role `Admin`. There is no longer a way to grant team-admin power org-wide in one step — by design, you cannot admin a team you do not belong to.

## 7. KeyCloak realm + dev DB seed

- **`deploy/keycloak/kartova-realm.json`:** remove the `TeamAdmin` realm role definition; reassign `team-admin@orga.kartova.local`'s realm role from `TeamAdmin` to `Member` (keep the username, email, and KeyCloak `id`/`sub`).
- **`src/Kartova.Migrator/DevSeed.cs`:** extend the seeder (same RLS-toggle pattern already used for `organizations` / `catalog_applications`) to insert, for Org A:
  - one demo `teams` row (deterministic id), and
  - one `team_members` row making `team-admin@orga.kartova.local` an `Admin` of it — pre-seeding that user's `users`-projection row (using the realm seed's fixed `sub`) if a membership FK requires it.

  This makes the dev stack demonstrate the new model directly: a realm-`Member` who is a team-`Admin` can manage the demo team but nothing else.
- **`tests/Kartova.Testing.Auth/RealmSeedConstants.cs` / `SeededOrgs.cs`:** update any `TeamAdmin`-role expectations for the re-seeded user.

## 8. Testing

- **`tests/Kartova.SharedKernel.AspNetCore.Tests/TeamAdminOfThisHandlerTests.cs`** — now the sole gate; explicit cases: realm-`Member` + team-`Admin` → allow; realm-`Member` + team-`Member` → deny; non-member → deny; `OrgAdmin` (any team) → allow.
- **Integration (the proof the footgun is fixed)** — in the Organization integration suite: a realm-`Member`-but-team-`Admin` user mutates their own team over HTTP (was 403 pre-change) → 200/204; the same user gets 403 on a *different* team. Cover the member triplet + `PUT`/`DELETE /teams/{id}`.
- **`tests/Kartova.SharedKernel.Tests/KartovaRolePermissionsTests.cs`** — drop `TeamAdmin` rows; assert `OrgAdmin` no longer carries the three claims; assert the three constants are gone.
- **`tests/Kartova.ArchitectureTests/OrganizationPermissionMatrixTests.cs`** — rebuild the matrix for three realm roles.
- **`src/Modules/Catalog/Kartova.Catalog.IntegrationTests/CatalogPermissionMatrixTests.cs`** — update/remove `TeamAdmin` cells. Catalog app team-scoping (`ApplicationTeamScopedHandler`) is membership-based (`currentUser.TeamIds.Contains(app.TeamId)`), **not** realm-role-based, so app authorization is unaffected; keep the "Member in team A cannot mutate app in team B → 403" coverage.
- **`tests/Kartova.ArchitectureTests/KeycloakRealmSeedRules.cs`** — expect no `TeamAdmin` realm role.
- **`tests/Kartova.ArchitectureTests/KartovaPermissionsRules.cs`** + **`web/src/shared/auth/__tests__/usePermissions.test.tsx`** — stay green after both sides drop the three constants.
- **Invitation** — inviting `"TeamAdmin"` → 422 (unit + one integration assertion).

## 9. Blast radius (files)

**Core (SharedKernel):** `KartovaRoles.cs`, `KartovaRolePermissions.cs`, `KartovaPermissions.cs`.
**Routes:** `TeamEndpointDelegates.cs` (`TeamRoutes.MapTo`).
**Contract:** `CreateInvitationRequest.cs` (XML doc) + invitation role validation.
**Realm / seed:** `deploy/keycloak/kartova-realm.json`, `src/Kartova.Migrator/DevSeed.cs`, `tests/Kartova.Testing.Auth/RealmSeedConstants.cs` + `SeededOrgs.cs`.
**SPA:** `inviteUser.ts`, `permissions.ts`, `permissions.snapshot.json`.
**Tests:** `KartovaRolePermissionsTests`, `OrganizationPermissionMatrixTests`, `CatalogPermissionMatrixTests`, `KeycloakRealmSeedRules`, `KartovaPermissionsRules`, `TeamAdminOfThisHandlerTests`, `usePermissions.test.tsx`, and the Organization team integration tests (`AddTeamMemberTests`, `UpdateTeamTests`, `UpdateTeamMemberTests`, `RemoveTeamMemberTests`, `DeleteTeamTests`).
**Untouched:** `TeamAdminOfThisHandler.cs` (the keeper); `ApplicationTeamScopedHandler` (membership-based already); `TeamRoleKind` enum.
**Docs:** new ADR-0101 + `decisions/README.md` updates.

## 10. Verification (Definition of Done)

Follows the project DoD ladder (CLAUDE.md). Because this slice touches HTTP / auth / DB:
- Full-solution build (0 warnings, `TreatWarningsAsErrors`), full unit + architecture suite, Organization + Catalog integration (Testcontainers) green.
- `docker compose up` HTTP evidence: the realm-`Member`-but-team-`Admin` seeded user manages the demo team (happy) and 403s on a non-member team (negative), captured.
- `/simplify`, mutation feedback loop on changed files (≥80%), `/superpowers:requesting-code-review` on the branch diff, `/pr-review-toolkit:review-pr`, `/deep-review`.
