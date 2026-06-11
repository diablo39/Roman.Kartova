# Slice 10 amendment — Application ownership realignment (team-owned, user = created-by)

**Date:** 2026-06-10
**Author:** Roman Głogowski (AI-assisted)
**Status:** Approved (pre-implementation) — folded into PR #28 (branch `slice-10-member-lifecycle`)
**Amends:** `2026-06-09-slice-10-member-lifecycle-management-design.md` (D5/§7 reassignment) + the slice-3/9 Application ownership model.
**ADRs created:** ADR-0103 (Application ownership = required Team; individual = created-by provenance). **ADRs updated:** ADR-0102 (offboarding no longer reassigns owned apps).

## 1. Why

The slice-10 offboard flow reassigned a member's owned `Application`s to a successor because `Application.OwnerUserId` was a **required individual owner**. Design review surfaced that this conflates two concepts. The reference products this catalog models (Backstage, Compass — see CLAUDE.md) are unambiguous:

- **Backstage:** every entity has a **required** `spec.owner` resolving to a **Group (team)** by strong convention (User allowed only as a discouraged fallback); there is no ownerless entity. "Who created it" is audit/provenance (git history + `managed-by-location`), not an ownership field.
- **Compass:** a component's **Owner is an Atlassian Team**; authorship lives in the activity log.

So ownership belongs to the **team**; the individual is **provenance** ("created by"). This amendment aligns the model and, as a consequence, removes the slice-10 reassignment subsystem.

## 2. Decisions (locked)

| # | Decision |
|---|----------|
| D1 | `Application.OwnerUserId` → **`CreatedByUserId`**: immutable provenance, set once at creation, **never reassigned or nulled**. After a creator is offboarded the id stops resolving to a live user — display falls back gracefully ("former member"/Unknown, via `IUserDirectory` miss). |
| D2 | **`Application.TeamId` becomes required** (the owner). No ownerless apps. Compass-strict. |
| D3 | The slice-9 per-user facet is **reframed as "Created by"** (filter `?createdByUserId=`, catalog column/link, user-detail "Applications created" card) — kept, renamed, not deleted. |
| D4 | **Offboarding drops app reassignment.** Created-by is immutable history; offboard = guards → delete KeyCloak user → cascade team memberships → delete projection. The `IApplicationOwnerReassigner` port + impl + `Application.ReassignOwner` + the successor picker are removed. |
| D5 | **Backfill (pre-production, ADR-0101 — no live tenants):** migration makes `team_id` NOT NULL and backfills existing apps to the seeded demo team; `DevSeed` assigns the 120 seeded apps to the demo team. *Known forward note:* a NOT-NULL team means every tenant must have ≥1 team before it can own apps — the onboarding wizard (E-09) must guarantee a first team. Noted, not solved here. |
| D6 | **`AssignTeam` becomes reassign-only** (`Guid`, non-null). The slice-8 "unassign to null" carve-out is removed (would create an ownerless app). To delete a team that owns apps, reassign them to another team first (`DeleteTeam` already 409s while apps are assigned). |
| D7 | **Register requires `teamId`**, validated as "team exists in this tenant" (`IOrganizationTeamExistenceChecker`). Registration is **membership-gated**: OrgAdmin may register into any tenant team; Member may register only into teams they belong to. This gate was applied **this slice** (prompted by the security review closing an authz asymmetry vs. `assign-team`). Richer multi-team / co-owner registration rules remain E-03.F-05. |

## 3. Blast radius

### 3.1 Catalog domain + data
- `Application.cs`: `OwnerUserId`→`CreatedByUserId` (private setter, set only in ctor — drop `ReassignOwner`); `TeamId` non-null; `Create(displayName, description, createdByUserId, teamId, tenantId, clock)` validates `teamId != Guid.Empty`; `AssignTeam(Guid teamId)` (non-null, terminal-write guard unchanged). Update `Application` domain tests.
- `EfApplicationConfiguration.cs`: `owner_user_id`→`created_by_user_id`; `team_id` `.IsRequired()`.
- Migration `RealignApplicationOwnership` (Catalog): `RENAME COLUMN owner_user_id TO created_by_user_id`; backfill `team_id` = demo team for any NULL; `ALTER COLUMN team_id SET NOT NULL`. `Down` reverses.
- `DevSeed.cs`: seeded apps get `team_id` = demo team id; `created_by_user_id` unchanged (the seed owner).

### 3.2 Catalog API
- `RegisterApplicationRequest(string DisplayName, string Description, Guid TeamId)`. Register handler/delegate: validate team exists (`IOrganizationTeamExistenceChecker`) → 422 `invalid-team` if not; membership-gated (OrgAdmin any team; Member only teams they belong to) → 403 if gate fails; `Application.Create(..., currentUser.UserId, request.TeamId, ...)`.
- `ApplicationResponse.Owner` → **`CreatedBy`** (`UserDisplayInfo?`); enrichment via `IUserDirectory` unchanged (resolves the created-by id; null → "former member").
- List filter `?ownerUserId=` → **`?createdByUserId=`**; `ProblemTypes.InvalidOwner` → `InvalidCreatedBy` (or keep `InvalidOwner` text — rename for clarity).
- `assign-team` (`PUT /applications/{id}/team`): body `teamId` now required (no null/unassign). Team-scoped auth (`ApplicationTeamScopedHandler`) unchanged.
- Catalog integration/unit tests: every `Application.Create` / register now needs a team — broad churn (seed a team in fixtures; `SeedCatalogApplication*` helpers take/assign a team id).

### 3.3 Organization + SharedKernel (remove reassignment)
- **Delete:** `Kartova.SharedKernel/Multitenancy/IApplicationOwnerReassigner.cs`; `Kartova.Catalog.Infrastructure/ApplicationOwnerReassigner.cs` + its `CatalogModule` registration.
- `OffboardMemberCommand(Guid UserId, Guid ActingUserId)` (drop `SuccessorUserId`); `OffboardMemberResult(bool Offboarded, bool NotFound, bool CannotOffboardSelf, bool LastOrgAdmin)` (drop `InvalidSuccessor`, `AppsReassigned`).
- `OffboardMemberHandler`: ctor `(IKeycloakAdminClient keycloak)` only; flow = NotFound → Self → LastOrgAdmin → `DeleteUserAsync` → remove memberships → remove user. (No reassign.)
- `OffboardMemberRequest`: empty body (or remove the body entirely — `DELETE /users/{id}` with no payload). Delegate drops `SuccessorUserId`/`InvalidSuccessor` mapping; `ICurrentUser.UserId` still supplies `ActingUserId`.
- `ProblemTypes.InvalidSuccessor`: removed.
- Tests: `OffboardMemberHandlerTests` (drop invalid-successor + reassign-received cases; keep the KC-failure-leaves-projection-intact test — now without a reassignment to check); `OffboardMemberTests` integration (drop the reassignment + invalid-successor cases; keep self/last-admin/403/happy-without-reassign). `OffboardMemberResultTests` updated.

### 3.4 SPA
- `OffboardMemberConfirmDialog.tsx`: plain confirm (remove `UserSearchCombobox` successor picker + `excludeUserId` + `successorUserId`); warning text → "Removes {name}; their 'created by' attribution stays as history." `members.ts` `useOffboardMember` drops `successorUserId`.
- Catalog SPA: `OwnerLink` → "Created by" rendering (rename component/usages); the owner column header → "Created by"; `ApplicationResponse.Owner` → `CreatedBy`; the `?ownerUserId=`/owner-filter → `createdByUserId`.
- `UserDetailPage`: "Owned applications" card → **"Applications created"** (`useApplications({ createdByUserId: id })`).
- **Register form: add a required team picker** (`<select>`/combobox of tenant teams; uses `useTeamsList`). New UI.
- Regenerate the OpenAPI snapshot (contract changed: `RegisterApplicationRequest.teamId`, `ApplicationResponse.createdBy`, `OffboardMemberRequest` no successor, `?createdByUserId`, assign-team body) — harvest from a host-run API as in the prior codegen step.

### 3.5 Docs
- **ADR-0103** (new): *Application ownership is a required Team; the individual is created-by provenance (Backstage/Compass-aligned).* Related: ADR-0066 (multi-ownership — future co-owning teams build on this), ADR-0102, ADR-0101. Indexed in `README.md`.
- **ADR-0102** updated: offboarding hard-deletes the identity + projection and **does not reassign** owned apps (created-by is immutable history); remove the successor/reassignment language + the dual-write-window note tied to reassignment.
- Story ACs: **E-02.F-01.S-01** (required fields → name, **owning team**, description; created-by recorded as provenance); **E-03.F-01.S-07** (offboard: no successor; owned apps unaffected because team owns them).

## 4. Verification
Full DoD ladder on the amended branch: build (TWAE) 0/0; full unit+arch+integration suites green (Catalog churn is the big one — every app-creation gains a team); SPA green + regenerated snapshot; real-HTTP re-check of register-with-team + offboard-without-successor. `/deep-review` + `/simplify` on the incremental diff. Mutation loop remains the deferred PR gate.
