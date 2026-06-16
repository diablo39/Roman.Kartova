# Deep PR Review — slice-10-member-lifecycle (post-ownership-realignment)

**Date:** 2026-06-11 **Target:** branch `slice-10-member-lifecycle` vs `master` (`git diff master...HEAD` + uncommitted working-tree edits) **Status:** OPEN (pre-merge gate)
**Read against:** `2026-06-10-slice-10-ownership-realignment-design.md` (D1–D7), `2026-06-09-slice-10-member-lifecycle-management-design.md`, the slice-10 plan, ADR-0095/0098/0101/0102/0103/0090, CLAUDE.md §DoD.
**Supersedes** the stale `2026-06-10-slice-10-member-lifecycle-review.md` (written before the realignment removed the reassignment subsystem).

## Overview

The slice ships member lifecycle — a cursor-paginated members directory (`GET /api/v1/organizations/users` with `role`/`q` filters), change-role (`PUT .../users/{id}/role`, write-through to KeyCloak + the `users.realm_role` projection, last-OrgAdmin guard), and offboard (`DELETE .../users/{id}`, hard-delete of the KeyCloak identity + cascade of memberships, ADR-0102). It also folds in the ownership realignment (ADR-0103): `Application` is team-owned with an immutable `CreatedByUserId` provenance, registration requires an existing team and is membership-gated, and the offboard reassignment subsystem (`IApplicationOwnerReassigner` / successor picker) is removed. Verified this session: build 0w/0e, full unit+arch+integration suites + 425 SPA vitest green, real-HTTP smoke (directory 200, no-token 401, viewer 403, OrgAdmin change-role 204).

## Blocking-class issues

**None.** Spec decisions D1–D7 are all implemented; the realignment rename is complete (no surviving `OwnerUserId`/`IApplicationOwnerReassigner`/`successorUserId`/`OwnerLink` in production code); DoD gates 1/4/5/6 are green with citable evidence. The mutation-feedback gate (DoD #7) is explicitly deferred per the realignment spec §4 ("Mutation loop remains the deferred PR gate") and the user's instruction this run — tracked, not silently skipped.

## Should-fix issues

1. **Target-team membership gate is hand-rolled, bypassing the shared `ApplicationTeamScoped` policy.**
   - Evidence: `CatalogEndpointDelegates.cs:59-63` (register) and the same `!caller.IsInRole(OrgAdmin) && !currentUser.TeamIds.Contains(request.TeamId)` shape in `AssignApplicationTeamAsync` (~:300). The *source*-team gate in the same file uses the canonical `IAuthorizationService.AuthorizeAsync(..., ApplicationTeamScoped)`.
   - Impact: the OrgAdmin-bypass + membership rule now lives in three places; a future policy change (e.g. admit TeamAdmin, or read `TeamMemberships` instead of `TeamIds`) silently diverges on the two hand-rolled copies. Behaviourally correct today.
   - Fix: wrap the target team in a tiny `ITeamScopedResource` (`Guid? TeamId => request.TeamId`) and run it through the existing `ApplicationTeamScoped` policy so the rule is defined once.

2. **Duplicated last-OrgAdmin invariant across two handlers.**
   - Evidence: `ChangeMemberRoleHandler.cs:20-24` and `OffboardMemberHandler.cs:41-45` — identical `RealmRole==OrgAdmin … CountAsync(OrgAdmin) <= 1` including the `<= 1` boundary.
   - Impact: the org-admin-floor business rule is scattered; a refinement (count only active admins, etc.) won't propagate to both sites.
   - Fix: extract one shared guard (e.g. `OrgAdminFloor.WouldRemoveLastAsync(db, userId, ct)`) called by both handlers.

3. **Offboard dual-write: a commit failure *after* the KeyCloak delete leaves a permanent orphaned projection row.**
   - Evidence: `OffboardMemberHandler.cs:47-52` — `DeleteUserAsync` (irreversible) runs, then `SaveChangesAsync` + the request-end commit remove the local row. The XML doc (`:21-30`) reasons carefully about the *KC-throws* direction (transaction rolls back, projection intact) but not the reverse: if the DB commit fails after the KC delete succeeds, the KeyCloak identity is gone while the local `users` row persists.
   - Impact: unlike change-role (whose stale projection self-heals via `SessionStartHandler` realm_role re-sync on next login), an offboarded user never logs in again — the orphaned row is permanent and points at a deleted identity. Low probability (pre-production, no live tenants).
   - Fix: document the residual window and emit a logged error on the post-delete failure path for manual reconciliation; longer-term, an outbox/reconciliation sweep. At minimum add the test in Missing tests below.

4. **Double KeyCloak token fetch per role change.**
   - Evidence: `KeycloakAdminClient.cs` — `ChangeRealmRoleAsync` calls `GetTokenAsync` (~:166) for the list/delete calls, then delegates to `AssignRealmRoleAsync` which calls `GetTokenAsync` again (~:75): two client-credentials round trips for one logical operation.
   - Impact: extra latency + token-endpoint load on every role change.
   - Fix: fetch the token once in `ChangeRealmRoleAsync` and pass it into a private `AssignRealmRoleCoreAsync(token, …)` shared by both methods. (Coordinate with the `KeycloakAdminClientTests` GET→DELETE→POST sequence assertions.)

5. **Members search predicate duplicated from `UserQueries.SearchAsync`.**
   - Evidence: `ListMembersHandler.cs:47-49` repeats the `DisplayName.ToLower().Contains(...) || Email.ToLower().Contains(...)` infix predicate (and its provider-portability rationale) verbatim.
   - Impact: a change to search columns or the Npgsql/in-memory workaround must be made twice.
   - Fix: extract a shared `IQueryable<User>` extension / `Expression<Func<User,bool>>` and call it from both. (Touches existing `UserQueries.cs` — keep the two distinct return shapes.)

## Nits

1. **Redundant creator re-fetch on the `?createdByUserId=` filtered path.** `CatalogEndpointDelegates.ListApplicationsAsync` validates the id via `directory.GetAsync` then `ListApplicationsHandler` batch-loads creators again — on a single-creator filtered page that re-reads the same `UserDisplayInfo`. Thread the validated value into the handler to seed the dictionary.
2. **`CreatedByLink` renders `null` (offboarded creator) and `undefined` (loading) identically as "Unknown user."** Spec D1 hinted a "former member" affordance for the offboarded case; current copy is acceptable but loses that distinction. (`CreatedByLink.tsx:30-33`)
3. **Leading success boolean on `ChangeMemberRoleCommand`/`OffboardMemberCommand` is derivable** (true iff all error flags false; read only by tests). A single terminal-state enum would be clearer — but it mirrors the existing `AssignApplicationTeamResult` multi-bool convention, so treat as a module-wide decision, not an isolated fix.
4. **Uncached client-credentials token on every admin call** (`KeycloakAdminClient.GetTokenAsync`). Pre-existing; this slice's new role-change path multiplies the cost. Worth a follow-up (expiry-aware token cache).

## Missing tests

- **Offboard post-KC-delete commit-failure path (should-fix #3).** No test covers "KeyCloak delete succeeds, then `SaveChangesAsync` throws." Add `OffboardMemberHandlerTests` case: stub `IKeycloakAdminClient.DeleteUserAsync` to succeed, force the DB save to throw, assert the resulting state/contract (and that the orphan is logged). Today only the KC-throws-→-rollback case exists.
- Otherwise coverage is strong and tier-appropriate (ADR-0097): result tests lock every terminal branch (`ChangeMemberRoleResultTests`, `OffboardMemberResultTests`); integration covers directory filters + cursor (`ListMembersTests`), change-role write-through + last-admin (`ChangeMemberRoleTests`), offboard self/last-admin/403/happy (`OffboardMemberTests`), register team-validation (`RegisterApplicationTests`); arch covers the permission matrix.

## What looks good

1. **Offboard guard ordering + explicit transaction reasoning.** `OffboardMemberHandler.cs:16-30` documents the NotFound→Self→LastOrgAdmin order and the ADR-0090 rollback semantics for the KC-throws case — deliberate, not accidental.
2. **`KeycloakAdminExceptionHandler` at the right altitude.** A global `IExceptionHandler` reusing `IProblemDetailsService` + `ProblemTypes.ServiceUnavailable`, registered alongside the existing handler family (`Program.cs`), rather than per-handler try/catch.
3. **Members directory honors the shared pagination contract.** `ListMembersHandler.cs:35-56` registers `role`/`q` in `expectedFilters` per ADR-0095 cursor-filter-replay, and batch-loads team counts with a single `GROUP BY` (no N+1).
4. **`KeycloakRoleChange.RolesToRemove`** is a pure, separately unit-tested helper — the role-removal decision is isolated from HTTP plumbing and kills boolean-flip mutants.
5. **Realignment correctness at the edges.** `CreatedByLink.tsx:30-33` degrades gracefully for deleted creators (D1), and register validates team-exists (422) *before* the membership gate (403) in the correct order (`CatalogEndpointDelegates.cs:43-63`).

---

# Addendum — `/pr-review-toolkit:review-pr` (silent-failure / type-design / test-thoroughness)

Three specialized agents run after the deep-review above; findings here are **additive** (deep-review items not restated).

## Silent failures (KeyCloak write-through)

The `KeycloakAdminClient` HTTP layer itself is clean (every method checks `IsSuccessStatusCode`, maps 401/403/404/409 to typed errors, null-checks bodies, no empty catches). The gaps are at the seams:

- **SF-1 (HIGH, should-fix) — `ChangeRealmRoleAsync` non-atomic DELETE→POST can silently strip ALL roles.** `KeycloakAdminClient.cs:163-185` removes the old role(s) then assigns the new one as two unbatched KC calls. If the DELETE succeeds and the POST fails, KC holds *zero* Kartova roles; the method throws → 502 "retry shortly" (reads as "nothing happened") → `ChangeMemberRoleHandler.cs:27` never updates the projection (still shows old role). On the victim's next login `SessionStartHandler.cs:83` resolves no realm role → silently drops them to **Viewer** and writes that into the projection, "healing" to the *wrong* value. **Fix:** assign-new-then-remove-old (POST→DELETE) so a mid-failure leaves the user over-privileged-but-present (detectable/recoverable) rather than silently stripped; log an Error on the post-DELETE-failure path; flip the `KeycloakAdminClientTests` sequence assertion consciously.
- **SF-2 (MEDIUM, should-fix) — KC 404 flattened to 502.** `KeycloakAdminExceptionHandler.cs:34-47` maps *every* `KeycloakAdminException` to 502 regardless of `.Error`. A `NotFound` from `DeleteUserAsync`/`ChangeRealmRoleAsync` (local row exists, KC identity already gone) becomes "retry shortly" forever. The invitation handlers already `catch when (ex.Error == KeycloakAdminError.NotFound)`; the two new lifecycle handlers don't. **Fix:** offboard should treat `DeleteUserAsync` NotFound as success-equivalent (idempotent, completes local cleanup — also resolves the orphan in should-fix #3); branch the handler on `.Error` so a permanent state isn't dressed as transient.
- **SF-3 (MEDIUM, should-fix) — `OneEmailPerTenantViolationException` unmapped.** `UserProjectionUpdater.cs:55-71` logs+rethrows it, but no `IExceptionHandler` in `Program.cs` maps it → generic 500 on the login hot path, and `OneEmailPerTenantViolationException.cs:34-37` builds a message embedding tenantId + email (PII-leak risk if developer details are ever on in a shared env). **Fix:** add an `IExceptionHandler` returning a typed RFC-7807 with a generic detail, keeping the rich content server-log-only (mirror the KeyCloak handler's discipline). *(Partly pre-existing; the slice puts it on the hot login path.)*

## Type design

- **TD-1 (should-fix, cross-cutting) — `role` is a raw `string` end-to-end** (`UpdateMemberRoleRequest`, `ChangeMemberRoleCommand`, `ListMembersQuery`, `MemberSummaryResponse`, `KeycloakRoleChange`) for a closed 3-value set already modeled as `KartovaRoles.All`. Precedent exists (`TeamRole : byte`). A closed `KartovaRole` enum would push validity to deserialization (the `ChangeMemberRoleHandler.cs:14` allowlist check becomes structural), publish the legal values in OpenAPI, and remove `ListMembersQuery.Role`'s hidden "must be pre-canonicalized, never `all`/lowercase" precondition.
- **TD-2 (should-fix) — `OffboardMemberCommand(Guid UserId, Guid ActingUserId)`: two adjacent same-typed Guids, transposition-unsafe.** The self-guard `cmd.UserId == cmd.ActingUserId` (`OffboardMemberHandler.cs:39`) silently inverts if the call site swaps them; compiles + type-checks. Distinct id wrappers or named-argument enforcement.
- **TD-3 (nit) — `KeycloakRoleChange` takes `(string Id, string Name)` tuple** because `KeycloakAdminClient.RealmRole` is `private`; the call site maps record→tuple lossily. Promote `RealmRole` to an `internal` record in `SharedKernel.Identity` and consume it in both.
- **Calibration:** all four contract DTOs correctly carry `[ExcludeFromCodeCoverage]`; `MemberSortField` is an enum the arch test exempts — no coverage-attr finding. `ApplicationResponse.TeamId` `Guid?`→`Guid` and the `CreatedByUserId`(non-null)+`CreatedBy`(nullable) split are genuine illegal-states-removed improvements.

## Missing tests (additive to the deep-review's offboard-commit-failure case)

- **MT-1 (CRITICAL) — members directory cursor-filter-mismatch replay is untested.** `ListMembersHandler` builds `expectedFilters` (ADR-0095) but no test asserts a cursor issued under `role`/`q` is rejected (400 `cursor-filter-mismatch`) when replayed with different filters. Catalog pins this (`ListApplicationsPaginationTests.cs:403,443`); a mutant passing `null` for `expectedFilters` survives every current test. Add `ListMembersTests`: page-1 with `role=Member&limit=2` → replay cursor with `role=OrgAdmin` → assert 400 + `ProblemTypes.CursorFilterMismatch` (+ filtered→filterless sibling).
- **MT-2 — register 403-vs-422 ordering not pinned.** No Member+unknown-team test. Add `RegisterApplicationTests.POST_Member_with_unknown_teamId_returns_422_not_403` (existence check precedes the gate).
- **MT-3 — no `ListMembersHandler` unit test; team-count >1 unverified** (mutant `g.Count()`→`1` survives). Add `ListMembersHandlerTests`: user in 2 teams → `TeamCount==2`; 0 teams → 0; dictionary keys by UserId.
- **MT-4 — sort fields `role`/`createdAt` + unknown-sortBy 400 never exercised** (`MemberSortSpecs`).
- **MT-5 — no multi-page cursor walk** on `/organizations/users` (disjoint/complete under the Id tiebreaker).
- **MT-6 — combined `role`+`q` filter (two-key expectedFilters) untested.**
- **MT-7 (quality) — offboard integration happy-path doesn't assert the KC delete fired** (unit-covered via `kc.Received(1)`, but the integration title overclaims).
- **MT-8 (quality) — no integration-tier 404** for change-role/offboard (unit-covered).

## What the agents confirmed sound

- `KeycloakAdminClient` HTTP fault detection (every non-2xx → typed throw); `UserProjectionUpdater` 23505 catch is correctly SqlState-specific + logs+rethrows; result-shape tests assert all four booleans (mutation-grade); handler guard tests assert `kc.DidNotReceive()`; SPA dialogs cover both ProblemDetails fallback branches.
