# Slice 10 — Member lifecycle management (design)

**Date:** 2026-06-09
**Author:** Roman Głogowski (AI-assisted)
**Status:** Draft, pending user review
**Stories opened:** E-03.F-01.S-05 (members directory), E-03.F-01.S-06 (change member role), E-03.F-01.S-07 (offboard member) — new stories appended to E-03.F-01.
**ADRs created in this slice:** ADR-0102 (user offboarding = hard delete, outside ADR-0019 catalog soft-delete scope; offboarding/role-change audit deferred).
**ADRs referenced:** ADR-0006 (KeyCloak as IdP), ADR-0011 (one Org = one tenant), ADR-0018 (append-only audit log — *not* built here), ADR-0019 (soft-delete + purge — scoped to catalog entities, not identities), ADR-0028/0080 (Wolverine in-process mediator, durability still deferred), ADR-0082 (modular monolith — cross-module via `IMessageBus`), ADR-0090 (tenant scope — one connection + tx per request), ADR-0091 (ProblemDetails), ADR-0092 (REST URL convention), ADR-0095 (cursor pagination), ADR-0100 (strict one-email-per-tenant), ADR-0101 (team-admin via per-team membership).

---

## 1. Why this slice

Slices 8–9 shipped team management and the invite/view/search half of people management. But once a user accepts an invitation there is **no way to manage them**: an OrgAdmin cannot list all members, cannot change a member's role, and cannot remove a member from the org. The only role assignment happens at invitation time; the only "list" is a typeahead. This slice closes the post-invitation lifecycle gap — the backbone of org governance.

Three capabilities, three new stories under E-03.F-01:

1. **Members directory** (S-05) — a cursor-paginated screen + endpoint listing every active member with role, team count, last-seen.
2. **Change member role** (S-06) — OrgAdmin reassigns a member's realm role (Viewer/Member/OrgAdmin) via the KeyCloak admin client.
3. **Offboard member** (S-07) — OrgAdmin removes a member: hard-delete the KeyCloak identity + local projection, reassigning any owned catalog applications to a chosen successor.

**Explicitly out of scope (each its own future slice):**

- The ADR-0018 append-only audit log (and therefore *any* persisted trail of offboarding/role-change — see §2 D2).
- The ADR-0019 30-day purge sweep (offboarding here is an immediate hard delete of an identity, not a catalog-entity soft-delete — see ADR-0102).
- User self-profile editing (name/email write-back to KeyCloak).
- Team-delete ownership transfer (E-03.F-05.S-04, part of Multi-Ownership).
- KeyCloak session revocation / forced logout (session-kill decision = *neither*; see §2 D3).

## 2. Decisions (resolved during brainstorming)

| # | Decision | Rationale |
|---|----------|-----------|
| D1 | **Offboarding is a hard delete** of the KeyCloak user + the local `users` projection row. It is *not* an ADR-0019 soft-delete. | ADR-0019's soft-delete + purge governs **catalog entities** (applications, relationships) that users accidentally delete and need to recover. A member is a KeyCloak-owned **identity**, not a catalog entity; its lifecycle is governed by the IdP. Hard delete frees the email/seat (ADR-0100 one-email-per-tenant) and is erasure-aligned (ADR-0015). Captured as **ADR-0102**. |
| D2 | **Audit of offboarding/role-change is deferred** to the ADR-0018 audit-log slice. Until then, offboarding is intentionally **traceless** (no soft-delete row, no audit entry). | User-confirmed: skip audit-log infrastructure this slice. This is a conscious MVP carve-out, named as a known gap in ADR-0102 — *not* a silent omission. Command handlers are structured (single mutation method per action) so an `IAuditLog` emitter drops in later without reshaping them. |
| D3 | **No session revocation.** Role-change and offboard take effect on the member's next access-token refresh (token TTL, minutes). | User-confirmed (*neither*). Avoids adding KeyCloak session-management surface; the UI surfaces the staleness ("takes effect on next login"). Immediate revocation can be added with the audit/security-hardening slice if needed. |
| D4 | **`realm_role` is added to the `users` projection as a write-through read-cache.** KeyCloak remains the source of truth. | The directory must show a Role column for a page of N members. Reading role per-row from KeyCloak is N+1 (rejected by slice-9 Decision 6). The projection column makes the directory one indexed SELECT. Role-change writes through to KeyCloak **and** the projection; invitation sets it from the invited role; session-bootstrap re-syncs from the JWT. |
| D5 | **Owned applications reassign to a chosen successor** in one step, cross-module via a **DI port** `IApplicationOwnerReassigner` (Organization's offboard handler calls it; Catalog implements it against `CatalogDbContext`), within the request's tenant-scope transaction. | `Application.OwnerUserId` is a required non-empty Guid — apps cannot be left ownerless. Reassign-to-successor beats block-409 on UX and beats reassign-to-actor on predictability. ADR-0082 forbids direct cross-module DB access. The port pattern — **not** Wolverine `IMessageBus` — is the codebase's established cross-module mechanism (`IApplicationCountByTeamReader`, `IOrganizationTeamExistenceChecker`); per ADR-0093 the Wolverine bus opens its own DI scope that would not see the request's `ITenantScope`, so `IMessageBus` would break tenant isolation here. |
| D6 | **Two new granular permissions** — `org.users.role.change`, `org.users.remove` — both **OrgAdmin-only**. | Matches slice-9's granular split (invitations had separate `create`/`revoke`). Binding-level `.RequireAuthorization(...)` enforcement; Viewer/Member fail before any DB hit. |
| D7 | **Org must retain ≥ 1 active OrgAdmin.** Cannot demote or offboard the last OrgAdmin; cannot offboard yourself. | Prevents the org locking itself out of administration and the actor locking themselves out mid-operation. |
| D8 | **The directory is a new endpoint `GET /users`; the slice-9 typeahead relocates to `GET /users/search`.** | *Refinement vs the approved sketch* (which kept the typeahead at `GET /users?q=`): one path/verb must map to one response schema for the OpenAPI codegen pipeline (slice-7 chore). `GET /users` returns `CursorPage<MemberSummaryResponse>`; the bounded typeahead returns `IReadOnlyList<UserSummaryResponse>`. Co-locating both on `?q?`-presence would force two response schemas on one operation. Relocating the typeahead to `/users/search` keeps each contract clean; the combobox call + its tests are updated, behavior unchanged. **Flagged for review.** |

**Non-goals (YAGNI):** no bulk role-change; no bulk offboard; no re-invite-after-offboard convenience (hard delete frees the email, so the existing invite flow already covers it); no audit; no session kill.

## 3. Domain & data model

### 3.1 `User` projection — extended

`User` stays a **projection** (no aggregate behavior, source of truth = KeyCloak). One new column:

```csharp
public sealed class User : ITenantOwned
{
    // existing: Id (= KeyCloak sub), TenantId, Email, GivenName, FamilyName,
    //           DisplayName, LastSeenAt, CreatedAt
    public string RealmRole { get; set; } = KartovaRoles.Viewer;   // write-through cache of the KeyCloak realm role
}
```

`RealmRole` holds exactly one of `KartovaRoles.All` (`Viewer` / `Member` / `OrgAdmin`). Orthogonal roles (`PlatformAdmin`, `ServiceAccount`) are never stored here — they are not tenant-membership roles.

### 3.2 Tables

**`users`** — alter:

```sql
ALTER TABLE users
  ADD COLUMN realm_role varchar(32) NOT NULL DEFAULT 'Viewer';
-- partial index to make the "last active OrgAdmin" guard (D7) a cheap COUNT:
CREATE INDEX idx_users_orgadmins ON users(tenant_id) WHERE realm_role = 'OrgAdmin';
```

RLS unchanged (slice-9 policy already in force). No new table.

### 3.3 Migration

`AddUserRealmRoleColumn` (Organization). Pure DDL + index; no FORCE toggle. **Backfill:** existing rows default to `Viewer`; `DevSeed` and the next session-bootstrap of each user correct them from the JWT. (Acceptable: the directory shows `Viewer` for a not-yet-re-authenticated legacy member until their next login; dev/seed data is corrected explicitly in the seeder.)

### 3.4 EF configuration

`UserEntityTypeConfiguration` gains `RealmRole` (max length 32, required, default `Viewer`). No value converter (plain string).

## 4. Authorization

### 4.1 Permission constants

Append to `KartovaPermissions`:

```csharp
public const string OrgUsersRoleChange = "org.users.role.change";
public const string OrgUsersRemove     = "org.users.remove";
```

Slice-7's reflection-based `All` + drift snapshot pick them up automatically.

### 4.2 Role → permission map

`KartovaRolePermissions.Map`: both new permissions granted to **OrgAdmin only**; Viewer/Member/(team) get neither.

### 4.3 SPA permission constants

`web/src/shared/auth/permissions.ts` + `permissions.snapshot.json` gain the two strings. The C#↔TS drift sentinel (`Ts_snapshot_equals_csharp_KartovaPermissions_All`) keeps both sides in lockstep.

### 4.4 No resource handlers

Both mutations are org-wide OrgAdmin acts (binding-level `.RequireAuthorization`). No per-team resource gate. Tenant scoping is automatic via `TenantScopeBeginMiddleware` (ADR-0090).

## 5. Endpoints — under `/api/v1/organizations`

| Method | Path | Permission | Notes |
|--------|------|-----------|-------|
| `GET` | `/users` | `org.users.read` | **New.** Cursor-paginated members directory (ADR-0095). Returns `CursorPage<MemberSummaryResponse>`. `sortBy ∈ {displayName, role, createdAt}` default `displayName` (lastSeenAt is a display-only column — nullable keyset pagination avoided); `sortOrder`; `cursor`; `limit`. Optional filters: `role ∈ {viewer, member, orgAdmin, all}` default `all`; `q` (infix on display_name + email, reuses slice-9 trigram indexes). Filtered queries pass the ADR-0095 `expectedFilters` map to `ToCursorPagedAsync` so a cursor can't be replayed across a filter change. |
| `GET` | `/users/search` | `org.users.search` | **Relocated** slice-9 typeahead (was `GET /users?q=`). `q` (min 2 chars), `limit ≤ 20`. `IReadOnlyList<UserSummaryResponse>`. `[BoundedListResult]` — justification "typeahead capped at 20 results". |
| `GET` | `/users/{id:guid}` | `org.users.read` | Unchanged (slice-9 `UserDetailResponse`). |
| `PUT` | `/users/{id:guid}/role` | `org.users.role.change` | Body `UpdateMemberRoleRequest { role }`. 204. Guards: 404 if not found; 409 `last-orgadmin` if demoting the last OrgAdmin; 422 if role ∉ `KartovaRoles.All`. |
| `DELETE` | `/users/{id:guid}` | `org.users.remove` | Body `OffboardMemberRequest { successorUserId }`. 204. Guards: 404; 409 `cannot-offboard-self`; 409 `last-orgadmin`; 422 `invalid-successor` (successor missing / inactive / == target). |

### 5.1 New / changed DTOs (`Kartova.Organization.Contracts`, all `[ExcludeFromCodeCoverage]`)

```csharp
public sealed record MemberSummaryResponse(
    Guid Id, string DisplayName, string Email, string Role,
    int TeamCount, DateTimeOffset? LastSeenAt, DateTimeOffset CreatedAt);

public sealed record UpdateMemberRoleRequest(string Role);
public sealed record OffboardMemberRequest(Guid SuccessorUserId);
```

`UserSummaryResponse` (slice-9 typeahead) is unchanged.

### 5.2 Problem types

Three new constants in `ProblemTypes`:

```csharp
public const string LastOrgAdmin       = "https://kartova.io/problems/last-orgadmin";
public const string CannotOffboardSelf = "https://kartova.io/problems/cannot-offboard-self";
public const string InvalidSuccessor   = "https://kartova.io/problems/invalid-successor";
```

Reused: `ResourceNotFound`, `ValidationFailed`.

## 6. KeyCloak admin client additions (`Kartova.SharedKernel.Identity`)

`IKeycloakAdminClient` gains one method:

```csharp
Task ChangeRealmRoleAsync(Guid userId, string newRole, CancellationToken ct);
```

Implementation: fetch the user's current realm-role mappings, **remove** any Kartova business roles (`Viewer`/`Member`/`OrgAdmin`) the user holds, then **assign** `newRole`. This needs two admin REST calls not yet wired in slice-9 (`GET .../role-mappings/realm`, `DELETE .../role-mappings/realm`); add them inside the client. `DeleteUserAsync` already exists (slice-9). No session/logout calls (D3).

Error mapping unchanged (`KeycloakAdminException` → 502 from the calling endpoint on `Unauthorized`/`Unexpected`; `NotFound` on delete is idempotent-OK).

## 7. Critical runtime flows

### 7.1 Change member role (`PUT /users/{id}/role`)

```
1. 422 if role ∉ KartovaRoles.All.
2. Load users row by id (RLS-scoped). 404 if absent.
3. Guard D7: if target.RealmRole == OrgAdmin && role != OrgAdmin
   && COUNT(users WHERE realm_role = OrgAdmin) == 1  ->  409 last-orgadmin.
4. IKeycloakAdminClient.ChangeRealmRoleAsync(id, role).   // source of truth
5. user.RealmRole = role; SaveChanges.                    // write-through cache
6. 204.
```

If step 5 fails after step 4, KeyCloak holds the new role but the projection is stale — self-heals on the member's next session bootstrap. Non-fatal; logged.

### 7.2 Offboard member (`DELETE /users/{id}`)

```
1. Load target users row (RLS-scoped). 404 if absent.
2. 409 cannot-offboard-self if id == current user id.
3. 422 invalid-successor if successor missing / not in tenant / == target.
4. Guard D7: if target.RealmRole == OrgAdmin && only one OrgAdmin -> 409 last-orgadmin.
5. IApplicationOwnerReassigner.ReassignOwnerAsync(fromUserId: id, toUserId: successorUserId).
   // Catalog impl loads its Applications WHERE owner_user_id = @from (CatalogDbContext,
   // shared request tenant tx) and calls app.ReassignOwner(@to) on each; SaveChanges.
6. IKeycloakAdminClient.DeleteUserAsync(id);     // external, point of no return
7. Remove team_memberships WHERE user_id = id; remove users row.
8. SaveChanges (commits steps 5 + 7 in the request tenant-scope tx); 204.
```

**Ordering & consistency.** The reassignment (step 5, Catalog's `CatalogDbContext`) and the projection/membership delete (step 7, `OrganizationDbContext`) share the one request connection + transaction (ADR-0090 — both DbContexts enlist in the same `ITenantScope`) and commit atomically. The KeyCloak delete (step 6) is the non-transactional external call: if it fails, the endpoint returns 502 and the transaction rolls back (apps un-reassigned, projection intact — safe to retry). The residual edge — KeyCloak delete succeeds but the DB commit then fails — leaves a deleted KeyCloak identity with a stale projection row; this is the same dual-write reality slice-9 accepted (no durable outbox yet) and is resolved by manual re-run of the (now idempotent on `NotFound`) offboard. Documented limitation, not solved here.

**Team-membership cascade.** If removing the member empties a team's Admin set, no special handling: OrgAdmin retains authority over every team via the `TeamAdminOfThis` OrgAdmin bypass (ADR-0101).

### 7.3 Cross-module port

```csharp
// Kartova.SharedKernel.Multitenancy
public interface IApplicationOwnerReassigner
{
    Task<int> ReassignOwnerAsync(Guid fromUserId, Guid toUserId, CancellationToken ct);  // returns # reassigned
}
```

Implemented by `Kartova.Catalog.Infrastructure.ApplicationOwnerReassigner(CatalogDbContext db)` (`internal sealed`, registered `services.AddScoped<IApplicationOwnerReassigner, ApplicationOwnerReassigner>()` in `CatalogModule`), consumed by Organization's offboard handler. This mirrors the existing `IApplicationCountByTeamReader` (Catalog→Organization) and `IOrganizationTeamExistenceChecker` (Organization→Catalog) ports exactly. A new domain method `Application.ReassignOwner(Guid newOwnerUserId)` (guards `Guid.Empty`) performs the mutation; the reassigner loads each matching aggregate and calls it. Idempotent — a second run with no matching `owner_user_id = from` rows reassigns 0 and is a no-op. **Not** Wolverine `IMessageBus` (D4: would break tenant scope).

## 8. SPA

### 8.1 New route + files

```tsx
<Route element={<ProtectedShell />}>
  <Route path="/members" element={<MembersDirectoryPage />} />
</Route>
```

| Path | Purpose |
|------|---------|
| `web/src/features/users/pages/MembersDirectoryPage.tsx` | `useCursorList` + `useListUrlState` + a hand-rolled `<table>` (the `TeamsListPage`/`CatalogListPage` idiom — there is no `<DataTable>` component; the CLAUDE.md guardrail names it aspirationally). Columns: display name (→ `/users/:id`), email, **role** (badge), team count, last-seen. Role filter + search box. Per-row actions for OrgAdmin: Change role, Remove. |
| `web/src/features/users/api/members.ts` | `useMembersList`, `useChangeMemberRole`, `useOffboardMember`. |
| `web/src/features/users/components/ChangeMemberRoleDialog.tsx` | Org-role picker (Viewer/Member/OrgAdmin) — distinct from the team-role dialog. Shows the "takes effect on next login" note (D3). |
| `web/src/features/users/components/OffboardMemberConfirmDialog.tsx` | Confirm + owned-app count warning + successor picker (`<UserSearchCombobox>`). Surfaces `last-orgadmin` / `cannot-offboard-self` ProblemDetails inline. |
| `web/src/features/users/**/__tests__/*` | Hook + component tests. |

### 8.2 Modified files

| Path | Change |
|------|--------|
| `web/src/shared/auth/permissions.ts` + `permissions.snapshot.json` | +2 permission strings. |
| `web/src/app/router.tsx` | Add `/members`. |
| `web/src/components/layout/Sidebar.tsx` | Add a Members entry (under the existing people/settings grouping), gated by `org.users.read`. |
| `web/src/features/users/components/UserSearchCombobox.tsx` (+ callers) | Point at the relocated `GET /users/search` (D8). Behavior unchanged. |
| `web/src/features/users/api/users.ts` | `useUserSearch` URL → `/users/search`. |

### 8.3 Codegen

TS types regenerate (slice-7 chore) after the backend ships the new + relocated endpoints; SPA hooks use the typed `apiClient`.

## 9. Backlog & ADR changes

- **EPICS-AND-STORIES.md / CHECKLIST.md** — append three stories to E-03.F-01:
  - S-05 — As an OrgAdmin, I want a directory of all org members with their role and teams, so I can see who has access.
  - S-06 — As an OrgAdmin, I want to change a member's role, so access stays correct as responsibilities change.
  - S-07 — As an OrgAdmin, I want to remove a member and reassign their owned components, so offboarding is clean and nothing is orphaned.
- **ADR-0102** (preview approved during brainstorming; written + indexed in `decisions/README.md` as part of this slice):
  > **User offboarding = hard delete.** Offboarding hard-deletes the KeyCloak identity and the local `users` projection, reassigning owned catalog applications to a chosen successor first. This sits **outside ADR-0019** (whose soft-delete + purge governs catalog entities, not IdP-owned identities) and is erasure-aligned (ADR-0015). Auditing of offboarding and role changes is **deferred to the ADR-0018 audit-log slice**; until that lands, offboarding is intentionally traceless. The org must always retain ≥ 1 active OrgAdmin.

## 10. Testing & Definition of Done

Full DoD ladder (CLAUDE.md). This slice touches HTTP / auth / DB / cross-module messaging, so `docker compose up` evidence is **required**.

- **Unit:** role-change + offboard guard logic (last-OrgAdmin COUNT, self-offboard, invalid-successor); `ChangeRealmRoleAsync` role-set computation.
- **Architecture:** the 2 new permissions appear in `KartovaPermissions.All`; C#↔TS permission snapshot drift test stays green; OrgAdmin-only mapping asserted in `OrganizationPermissionMatrixTests`.
- **Integration (Testcontainers + KeyCloak):** role-change round-trips to KeyCloak and updates the projection; offboard reassigns owned apps (cross-module), deletes the KeyCloak user, deletes the projection + memberships; `last-orgadmin` 409 on the last admin; `cannot-offboard-self` 409; `invalid-successor` 422; directory list paginates + sorts + role-filters.
- **docker compose evidence:** OrgAdmin offboards a member who owns an app → app reassigned to successor + member gone (happy); attempt to offboard the last OrgAdmin → 409 (negative). Captured.
- `/simplify`; mutation feedback loop on changed files (≥ 80%); `/superpowers:requesting-code-review` on the branch diff; `/pr-review-toolkit:review-pr`; `/deep-review`.

## 11. Blast radius (files)

**Domain/data:** `User.cs`, `UserEntityTypeConfiguration.cs`, new migration `AddUserRealmRoleColumn`.
**Auth:** `KartovaPermissions.cs`, `KartovaRolePermissions.cs`.
**Identity client:** `IKeycloakAdminClient.cs`, `KeycloakAdminClient.cs` (+ `ChangeRealmRoleAsync`).
**Endpoints:** `UserEndpointDelegates.cs` (directory + role-change + offboard + typeahead relocation), routing.
**Cross-module:** `IApplicationOwnerReassigner` port (`Kartova.SharedKernel.Multitenancy`) + `ApplicationOwnerReassigner` impl (Catalog.Infrastructure) + `Application.ReassignOwner` domain method.
**Contracts:** `MemberSummaryResponse`, `UpdateMemberRoleRequest`, `OffboardMemberRequest`, `ProblemTypes` (+3).
**SPA:** `MembersDirectoryPage`, `members.ts`, `ChangeMemberRoleDialog`, `OffboardMemberConfirmDialog`, `permissions.ts` (+snapshot), `router.tsx`, `Sidebar.tsx`, `UserSearchCombobox`/`users.ts` (typeahead relocation).
**Seed:** `DevSeed.cs` (set `realm_role` on seeded users).
**Docs:** ADR-0102 + `decisions/README.md`; EPICS-AND-STORIES + CHECKLIST.
**Untouched:** `TeamAdminOfThisHandler`, `Invitation` aggregate, `Organization` aggregate, logo/profile endpoints.
