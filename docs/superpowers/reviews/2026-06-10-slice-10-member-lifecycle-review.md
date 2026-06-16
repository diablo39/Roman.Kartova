# Deep PR Review — slice-10-member-lifecycle

**Date:** 2026-06-10
**Target:** branch `slice-10-member-lifecycle` (`git diff master..HEAD`, 12 commits)
**Status:** OPEN (pre-merge gate)
**Spec:** `docs/superpowers/specs/2026-06-09-slice-10-member-lifecycle-management-design.md`
**Plan:** `docs/superpowers/plans/2026-06-09-slice-10-member-lifecycle-management-plan.md`
**Reviewer:** deep-review (opus), in-session

## Overview
Slice 10 closes the post-invitation member-lifecycle gap: a cursor-paginated members directory (`GET /users`, returning `CursorPage<MemberSummaryResponse>` with role/team-count/last-seen), a role-change endpoint (`PUT /users/{id}/role`) writing through KeyCloak + the new `users.realm_role` cache, and an offboard endpoint (`DELETE /users/{id}`) that reassigns owned catalog apps to a successor via the new `IApplicationOwnerReassigner` DI port, hard-deletes the KeyCloak identity (per new ADR-0102), and cascades the local projection + memberships. It relocates the slice-9 typeahead to `GET /users/search`, adds a `KeycloakAdminException→502` exception handler, two OrgAdmin-only permissions, an SPA `/members` page with two dialogs, and seeds/backfills `realm_role`.

## Blocking-class issues
**None.** The four `realm_role` write paths are coherent (migration default `AddUserRealmRoleColumn.cs:13-19`; JWT sync `UserProjectionUpdater.cs:38,48`; change-role write-through `ChangeMemberRoleHandler.cs:27`; invitation stub `CreateInvitationHandler.cs:117`). Offboard guards short-circuit before any side-effect (`OffboardMemberHandler.cs:39-51`, proven by `OffboardMemberHandlerTests` `AssertNoSideEffects`). `AspNetCore→Identity` reference is cycle-free + arch-test-guarded. The OrgAdmin-shows-as-Viewer bug is fully fixed + regression-pinned (`SessionBootstrapTests.cs:282-344`).

## Should-fix issues

**1. Role-filter contract mismatch: spec documents lowercase, code requires exact PascalCase**
- **Evidence:** Spec §5 (`...-design.md:112`) documents `role ∈ {viewer, member, orgAdmin, all}` (camelCase); handler does ordinal equality `ListMembersHandler.cs:40` `query.Where(u => u.RealmRole == q.Role)` against PascalCase stored values (`KartovaRoles.cs:8-10`). Only `all` is case-insensitive (`:38`). OpenAPI advertises `role` as bare string (`web/openapi-snapshot.json`).
- **Impact:** `?role=viewer` (the documented form) silently returns an empty page. SPA doesn't send `role` yet (issue 2), so no live break, but the published contract is wrong.
- **Fix:** Normalize incoming `viewer/member/orgAdmin` → canonical `KartovaRoles` constant, 422 on unknown (mirror `ChangeMemberRoleHandler.cs:14`). Test: `ListMembersTests` `?role=viewer` (lowercase) asserts it narrows.

**2. Members page omits the role filter + search box mandated by spec §8.1**
- **Evidence:** Spec §8.1 (`...-design.md:214`) requires "Role filter + search box"; `MembersListPage.tsx:21` calls `useMembersList({ sortBy, sortOrder })` only — no `role`/`q` UI; `useCursorList` `goNext/goPrev/hasNext` unused (no pagination controls).
- **Impact:** Backend filtering/pagination ships but is UI-unreachable; >50-member orgs see only page 1. (Next/Prev absence is a pre-existing app-wide pattern — `TeamsListPage` lacks it too — so only the role filter + search box are net-new spec items.)
- **Fix:** Wire a role `<select>` + debounced search `<input>` into `useListUrlState`/`useMembersList` (hook already accepts `role`/`q`). Test: `MembersListPage.test.tsx` asserts filter changes query args.

## Nits
1. **Stale comment** `ChangeMemberRoleTests.cs:245-247` says the row "defaults to Viewer until first login" — since the fix, `CreateInvitationHandler.cs:117` sets `RealmRole = request.Role` (Member) at invite. Assertion still holds; comment misdescribes. Fix the comment.
2. **`OffboardMemberConfirmDialog` allows picking the target as their own successor** (`OffboardMemberConfirmDialog.tsx:67` — combobox has no target exclusion). Yields a late backend 422. Fix: filter the target id out client-side (UX polish).

## Missing tests
1. **Offboard dual-write / KC-failure rollback (spec §7.2 headline limitation) has no test.** Add to `OffboardMemberTests` (`Kartova.Organization.IntegrationTests`): seed a target owning an app + a valid successor, make `IKeycloakAdminClient.DeleteUserAsync` throw `KeycloakAdminException`, DELETE, assert (a) 502 `ProblemTypes.ServiceUnavailable`, (b) the app's owner is STILL the target (reassignment rolled back by the ambient tx), (c) the target's `users` row still exists.
2. **Confirm `ChangeRealmRoleAsync` DELETE→POST sequence unit test** (`KeycloakAdminClientTests.cs`) pins the remove-then-assign ordering at the unit tier. (Landed in the Task-3 fix — verify present.)

## What looks good
1. **Guard-order rationale codified + tested** — `OffboardMemberHandler.cs:14-20` documents NotFound→Self→InvalidSuccessor→LastOrgAdmin; `AssertNoSideEffects` proves every guard short-circuits before reassign + KC delete — the property that makes the dual-write window safe.
2. **Cross-module port follows the established pattern + ADR-0093** — `IApplicationOwnerReassigner` mirrors `IApplicationCountByTeamReader`; DI port (not Wolverine bus) preserves the request `ITenantScope`; both DbContexts share the one request tx.
3. **`realm_role` engineered for directory-as-one-SELECT** — partial index `idx_users_orgadmins ... WHERE realm_role='OrgAdmin'` (`AddUserRealmRoleColumn.cs:21-22`) makes the last-OrgAdmin guard a cheap indexed COUNT; `q` filter lands on the slice-9 trigram indexes.
4. **502 handler correctly scoped + leak-safe** — returns false for non-KC exceptions, fixed generic detail, disjoint from other handlers + CreateInvitation's local catch; pass-through + leak-prevention both tested.
5. **ADR-0102 is a genuine decision record** — draws the IdP-identity-vs-catalog-entity distinction against ADR-0019, names the traceless + dual-write gaps as conscious carve-outs, ties hard-delete to ADR-0100/0015, records rejected alternatives; indexed in `README.md:231`.
