# Slice 8 — Team management + team-scoped permissions (deep review)

**Branch:** `feat/slice-8-team-management-design` (40 commits ahead of master; HEAD = `24afc31`)
**Status:** OPEN — pre-merge gate
**Reviewed against:**
- Spec: `docs/superpowers/specs/2026-05-25-slice-8-team-management-and-team-scoped-permissions-design.md`
- Plan: `docs/superpowers/plans/2026-05-25-slice-8-team-management-and-team-scoped-permissions-plan.md`
- ADR index: `docs/architecture/decisions/README.md`
- Testing ADR: ADR-0097 (supersedes ADR-0083)
- Mutation report: `mutation-report-surviving.md` (dated 2026-05-22, predates slice 8 — surviving items are slice-7 leftovers, not actionable here)
- DoD reference: `CLAUDE.md §Definition of Done`

---

### Overview

Adds the `Team` aggregate and `TeamMembership` entity to the existing Organization module (new `teams` + `team_members` tables with RLS + FK cascade), wires resource-based authorization for team-scoped Catalog mutations and Team admin operations via `IAuthorizationService.AuthorizeAsync(user, resource, policy)`, populates `ICurrentUser.TeamMemberships` inside `TenantScopeBeginMiddleware` after `SET LOCAL app.current_tenant_id`, and lands ADR-0098 (UUIDs as canonical identifier) together with the retroactive `Application.Name` drop. SPA gets `/teams` + `/teams/{id}` pages, dialogs, an `AssignTeamPicker` on the Application detail page, and updated `usePermissions().teamIds / teamAdminTeamIds`.

### Blocking-class issues

None.

The eight prior review passes (per-task ×34, slice-boundary, `/simplify`, and the smoke run) absorbed the bugs that would normally land here — FK cascade, the unassign-on-decommissioned carve-out, the canonical `AddedAt`, the cross-tenant 422 test, the stale matrix-row name, and the membership-population middleware ordering. Remaining findings are should-fix-class.

### Should-fix issues

**SF-1. `CursorListQueryParameterTransformer` doesn't register `ListTeams` — wire schema diverges from `ListApplications` and from ADR-0095 §4.3.**

- **Evidence:** `src/Kartova.Api/OpenApi/CursorListQueryParameterTransformer.cs:34-48` only contains `ListApplications` rows in `EnumByOperationParameter` and `OperationsWithLimitParameter`. Confirmed in `web/openapi-snapshot.json:721-749` — `ListTeams` ships `sortBy`/`sortOrder` as plain `type: string` (no enum) and `limit` as `type: string` (not bounded int32).
- **Cite:** ADR-0095 §4.3 ("?sortBy — per-resource string enum", "?limit — bounded integer in [1, 200]") and `src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationModule.cs:70-72` which claims in a comment "sortBy/sortOrder enum schemas + bounded-integer limit schema are emitted by the OpenAPI transformer wired in Program.cs (same path as catalog)" — but they aren't.
- **Impact:** The generated TypeScript client (a) loses the `"createdAt" | "displayName"` discriminated union for `sortBy` so misspellings only surface at runtime as 400, and (b) types `limit` as `string` which forced `web/src/features/teams/api/teams.ts:61` to add `String(params.limit ?? 50)` — the wrong correction direction (the wire contract is supposed to be int per ADR-0095). The contract for the new endpoint is silently weaker than for catalog.
- **Fix:** Add the two `(ListTeams, ...)` rows to `EnumByOperationParameter` (`typeof(TeamSortField)` + `typeof(SortOrder)`), add `"ListTeams"` to `OperationsWithLimitParameter`, regenerate `web/openapi-snapshot.json` and `web/src/generated/openapi.ts`, then drop the `String(...)` coercion in `teams.ts`. Test: add an assertion to the existing `OpenApiTests` (or a new sibling) that `ListTeams.parameters.sortBy.schema.enum` contains both `createdAt` and `displayName`.

**SF-2. Cross-team self-lockout: a Member can reassign an app from their team to a team they don't belong to, then lose access.**

- **Evidence:** `src/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs:248-272` runs `LoadAndAuthorizeApplicationAsync` against the *current* team-A; on success it calls the handler which calls `app.AssignTeam(teamB)`. The resource gate doesn't inspect the *target* team. `src/Modules/Catalog/Kartova.Catalog.Infrastructure/AssignApplicationTeamHandler.cs:32-38` only verifies the target team exists.
- **Cite:** Spec §6.4 / Decision #9 doesn't prohibit this; the user-supplied callout calls it out as a deferred policy decision.
- **Impact:** A non-OrgAdmin Member in team A who carries `catalog.applications.edit-metadata` can knowingly or accidentally orphan themselves from the app — they reassign it to team B, lose `ApplicationTeamScoped` access, and now only an OrgAdmin (or member of team B) can move it back. No data integrity is harmed, but the SPA's `AssignTeamPicker` currently filters the visible-teams list to `perms.teamIds` for non-OrgAdmin (`web/src/features/teams/components/AssignTeamPicker.tsx:22-24`), so the SPA already hides team B. The backend doesn't enforce this filtering — a CLI / direct API caller can still trigger it.
- **Fix:** Pick one and document the choice in the spec:
  1. Add a second authorization step in `AssignApplicationTeamHandler` after the existence check: if non-OrgAdmin AND `cmd.TeamId.HasValue` AND `!currentUser.TeamIds.Contains(cmd.TeamId.Value)`, return a new `IsForbiddenTarget` result → 403; OR
  2. Document the irreversibility explicitly in the spec/ADR as deliberate ("we trust Members to not orphan themselves"), and add a SPA confirm-dialog when target team ∉ user's teams.
  Test (option 1): `AssignApplicationTeamTests.Member_in_team_A_reassigning_app_to_team_B_they_do_not_belong_to_returns_403`.

**SF-3. SPA `description` length cap (4096) vs C# domain validator (none) — pre-existing slice-3 divergence touched by slice 8.**

- **Evidence:** `web/src/features/catalog/schemas/registerApplication.ts:11` enforces `.max(4096)`; `src/Modules/Catalog/Kartova.Catalog.Domain/Application.cs:185-191` only checks `IsNullOrWhiteSpace`. `EfApplicationConfiguration.cs:50` declares the column as plain `text` (no `HasMaxLength`).
- **Cite:** User-supplied callout notes this as a deferred boundary mismatch. Slice 8 reworked `RegisterApplicationDialog` (`209eb02`) but kept the cap.
- **Impact:** A non-SPA client can write descriptions of arbitrary length; the column is unbounded `text`. The SPA validator gives a false sense of "the server validates this too."
- **Fix:** Pick one tier (the value): add `.HasMaxLength(4096)` on the EF mapping and a domain `Length > 4096` check in `ValidateDescription`, OR drop the SPA cap. The domain side is the canonical place per ADR-0082. Test: `ApplicationTests.Description_over_4096_chars_throws_ArgumentException`.

**SF-4. `Team.Description` empty-string vs null inconsistency between SPA dialog and existing tests.**

- **Evidence:** `web/src/features/teams/components/CreateTeamDialog.tsx:50` posts `description: values.description ?? ""` (empty string when user didn't type anything). `src/Modules/Organization/Kartova.Organization.Domain/Team.cs:50-54` accepts the empty string (only checks `Length > 512`). Integration tests seed teams with `null` (e.g., `UpdateTeamTests.cs:56`).
- **Cite:** Spec §2 Decision #2 — "Description (≤512, nullable, editable)". Decision #14 — UI gating; doesn't pin the wire payload.
- **Impact:** The DB ends up with mixed `NULL` and `''` rows depending on creation path. `team.description || <italic>No description</italic>` in `TeamDetailPage.tsx:62` collapses both visually, so UX is fine, but consumers comparing on `description IS NULL` will see drift.
- **Fix:** Either (a) in `CreateTeamDialog.tsx` / `RenameTeamDialog.tsx`, normalize empty-string to `null` before posting; or (b) normalize server-side in `Team.Create` / `Team.Rename` via `string.IsNullOrWhiteSpace(s) ? null : s`. Server-side is safer (also handles CLI / direct API). Test: `TeamTests.Create_with_empty_description_stores_null`.

**SF-5. `UpdateTeamMemberHandler` doc comment claims 200 status, but the endpoint returns 204 NoContent.**

- **Evidence:** `src/Modules/Organization/Kartova.Organization.Infrastructure/UpdateTeamMemberHandler.cs:9-12` — "The endpoint delegate maps these to 200 / 404 / 404." Actual code: `OrganizationEndpointDelegates.cs:284` returns `Results.NoContent()` and `OrganizationModule.cs:124` declares `Status204NoContent` in the produces metadata. `UpdateTeamMemberTests.cs:40` asserts `NoContent`.
- **Cite:** Spec §6.1 row: `PUT /teams/{id}/members/{userId}` body `{role}` — promote/demote, no body shape specified.
- **Impact:** Doc rot only. Spec doesn't pin the status; both 200 and 204 are RESTful. The disagreement is internal.
- **Fix:** Update the docstring to "200/204/404" or just "204/404/404". Two-character change.

### Nits

**N-1. `OrganizationModule.cs:70-73` claim is now misleading.**

- The comment says the catalog OpenAPI transformer handles enum + limit schemas for ListTeams. After SF-1 is fixed it'll be true; until then the comment promises behavior the code doesn't ship. Inline as part of the SF-1 fix.

**N-2. `JwtAuthenticationExtensions.cs:54` carries an "intentional mutation survivor" comment from slice 7.**

- Pre-existing; the line ships in slice 8 unchanged. Worth a separate cleanup PR — the `AddAuthorization()` call IS redundant with `AddAuthorizationBuilder()` (which already calls `AddAuthorizationCore` internally).

**N-3. `KartovaPermissions.cs:13-17` mixes camelCase-aligned spacing only for the new team block.**

- The existing five constants (lines 7-11) use single-space alignment; the new block at 13-17 has padded `=` alignment. Cosmetic.

**N-4. `AssignTeamPicker.tsx:18` calls `useTeamsList({ limit: 200 })` unconditionally even when the user lacks edit permission.**

- A Viewer with `team.read` will fire the query on every Application detail render. Wrap in `if (canEdit) ...`, OR pass `{ enabled: canEdit }` to the underlying query. Probably benign at slice-8 scale (≤200 teams). Pre-existing slice-7 pattern: same as `usePermissions` runs for every user.

**N-5. `TeamsListPage.tsx` and `TeamDetailPage.tsx` don't render pagination controls.**

- `useTeamsList` returns `goNext`/`goPrev` (via `useCursorList`) but neither page wires them. Mirrors `CatalogListPage.tsx` (no controls there either). Spec §7.6 says "cursor-paginated list" but doesn't require visible Next/Prev. Track as a follow-up if/when a tenant has >50 teams.

### Missing tests

- **MT-1.** Spec §10 row "ListTeamsTests — happy (any role with `team.read` → 200 paginated)". Current `ListTeamsTests.cs:23-26` exercises Viewer, Member, OrgAdmin but **not TeamAdmin**. Add a `[DataRow(KartovaRoles.TeamAdmin)]` row to the existing `[DataTestMethod]`. Trivial.

- **MT-2.** Spec §10 row "AddTeamMemberTests — 403 from non-admin-of-this". The existing `AddTeamMemberTests.Plain_member_not_admin_of_this_team_returns_403` proves the claim gate blocks a `Member` realm-role caller — but doesn't prove the *resource gate* blocks a `TeamAdmin` of team B from posting members to team A. Add `AddTeamMemberTests.TeamAdmin_of_other_team_returns_403`. Same gap for `RemoveTeamMemberTests` and `UpdateTeamMemberTests`. The pattern is already in `UpdateTeamTests.TeamAdmin_of_other_team_returns_403` — copy that shape.

- **MT-3.** Spec §10 "verify `teamMemberships` array in response per role + assignment scenario" (`GetMePermissionsTests`). I see no `GetMePermissionsTests.cs` updates in the diff — `GET /me/permissions` is tested only by `JwtAuthenticationExtensionsTests.cs:6` and the SPA `usePermissions.test.tsx`. Add an integration test that seeds memberships, mints a JWT for that sub, and asserts the response carries the right `teamMemberships[]`. Catches future regressions in the middleware-populates-into-`MePermissionsResponse` chain.

- **MT-4.** Spec Decision #9 / §5.6 second branch ("Member/TeamAdmin of any team can `read` but not mutate unassigned apps") — partially covered by `CatalogPermissionMatrixTests.Team_scope_matrix_for_metadata_edit` (PUT path only). Add the same matrix row for at least one lifecycle endpoint (e.g., `POST /deprecate` against unassigned app) so the four-cell coverage is uniform across mutation surfaces, not just `PUT`. Mutation-tests-style: would catch a future helper refactor that bypasses `LoadAndAuthorizeApplicationAsync` on one of the lifecycle delegates.

- **MT-5.** No test exists for `OrganizationTeamMembershipReader` returning the empty list when `userId == Guid.Empty` (early-out at `src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationTeamMembershipReader.cs:11`). Trivial unit test in `Kartova.Organization.Tests` or `Kartova.Organization.Infrastructure.Tests` — `Returns_empty_for_Guid_Empty_without_DB_hit` (use a substituted `OrganizationDbContext` or just an in-memory provider that throws if accessed).

- **MT-6.** Mutation report (`mutation-report-surviving.md`) was generated 2026-05-22, predates slice 8, so its survivors are all slice-7 code. Pending: re-run mutation testing against the slice-8 diff (DoD gate 7) and patch any logic-class survivors. Not a finding I can name file:line for yet; the report doesn't reflect this branch.

### What looks good

- **G-1. Two-layer authorization is consistently applied.** Every Catalog mutation delegate calls `LoadAndAuthorizeApplicationAsync` (`CatalogEndpointDelegates.cs:285-299`), and every Team mutation delegate calls `LoadAndAuthorizeTeamAsync` (`OrganizationEndpointDelegates.cs:297-311`). The helpers are small enough that the pattern reads at a glance, and the `/simplify` extraction (commits `cfaa828` + `24afc31`) was the right call.

- **G-2. Cross-module access is correctly inverted through SharedKernel ports.** `IApplicationCountByTeamReader` / `IApplicationIdsByTeamReader` / `IOrganizationTeamExistenceChecker` (`src/Kartova.SharedKernel/Multitenancy/*.cs`) keep Catalog ↔ Organization from referencing each other's domain assemblies, honoring ADR-0082. The impls (`ApplicationCountByTeamReader.cs:13-17`, `OrganizationTeamExistenceChecker.cs:11-16`) are 5-liners — exactly what cross-module ports should look like.

- **G-3. Membership population is wired with the right lifecycle semantics.** `TenantScopeBeginMiddleware.cs:82-105` resolves the reader from `RequestServices` (not the root provider), runs the query inside the try-block, and the finally's `handle.DisposeAsync()` covers the reader-throws path. `TenantScopeBeginMiddlewareTests.Middleware_disposes_handle_when_membership_reader_throws` pins that lifecycle.

- **G-4. The `ITeamScopedResource` / `ITeamOwnedResource` marker pattern.** The auth handlers (`ApplicationTeamScopedHandler.cs`, `TeamAdminOfThisHandler.cs`) take the interface as the resource type, not the concrete domain class — so SharedKernel.AspNetCore never references Catalog/Organization domain types. `ResourcePolicyIntegrationTests.AuthorizeAsync_resolves_handler_by_interface_match` proves the dispatch works. Slice 8 plan v2 §"verification spike" called this out and it paid off.

- **G-5. The FK cascade migration ships with a real SQL workaround for a non-obvious RLS-during-FK-validation problem.** `20260526142414_AddTeamMembersForeignKeyCascade.cs:34-39` sets a dummy `app.current_tenant_id` for the migration's transaction because PostgreSQL validates the new constraint *through* the RLS policy, which would otherwise fail with `42704: unrecognized configuration parameter`. That's the kind of fix that's invisible if it works and a 4-hour debug if it doesn't.

---

**Counts:** 0 blocking · 5 should-fix · 5 nits · 6 missing-tests · 5 good.
