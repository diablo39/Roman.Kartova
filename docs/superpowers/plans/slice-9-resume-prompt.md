# Slice 9 — Resume Execution Prompt

Paste the prompt below into a fresh `/clear`'d Claude Code session at the project root (`C:\Projects\Private\Roman.Gig2`).

---

## Prompt to paste

I'm continuing execution of slice 9 (organization & people management). Context:

**Spec:** `docs/superpowers/specs/2026-05-27-slice-9-organization-people-management-design.md`
**Plan:** `docs/superpowers/plans/2026-05-27-slice-9-organization-people-management-plan.md`
**Branch:** `feat/slice-9-organization-people-management` (already checked out)

**Status: Phases A + B + C + D + E + F + G COMPLETE.** F1–F8 shipped before this checkpoint (commits `577ff02..16ad0b0`); G1 (ADR-0100) shipped in this checkpoint. Remaining work: **Phase H (verification + DoD)**. Below is the Phase E checkpoint reference (NOT re-verified at the F/G boundary — see Phase H section for outstanding verification work).

Full-solution build: 0 warnings, 0 errors. Full unit + architecture + Catalog integration pass at HEAD `b7530ac` (Phase E checkpoint reference, pre-Phase F):

| Suite | Passed | Notes |
|---|---|---|
| `Kartova.Organization.Tests` | 77 | unchanged from D-phase |
| `Kartova.Organization.Infrastructure.Tests` | 72 | +6 from E3 `GetTeamHandlerTests` |
| `Kartova.SharedKernel.Identity.Tests` | 8 | unchanged from D-phase |
| `Kartova.SharedKernel.AspNetCore.Tests` | 96 | +1 net from `8cab5a7` PostAuthHook fix (TenantScopeBeginMiddlewareTests + 3 new − 2 old fan-out tests on TenantClaimsTransformation) |
| `Kartova.SharedKernel.Tests` | 106 | unchanged |
| `Kartova.Catalog.Tests` | 84 | +8 E1 (ListApplicationsHandlerOwnerEnrichment 5 + GetApplicationByIdHandlerOwnerEnrichment 3) + 4 E2 (ListApplicationsHandlerOwnerFilter) |
| `Kartova.Catalog.Infrastructure.Tests` | 3 | unchanged |
| `Kartova.ArchitectureTests` | 63 | unchanged |
| **TOTAL unit + architecture** | **509** | unit + architecture, all green |
| `Kartova.Catalog.IntegrationTests` | 96/96 | +4 E1 `ApplicationOwnerEnrichmentTests` + 4 E2 `ListApplicationsOwnerFilterTests` over D-phase baseline |
| `Kartova.Organization.IntegrationTests` | 70/70 | +3 net E3 + H7 fix in `2bde836` |

**Known integration failures (do NOT regress in Phase H work):**

1. ~~`AuthErrorTests.Platform_admin_without_tenant_hits_missing_tenant_on_tenant_scoped_route`~~ ✅ **RESOLVED in `2bde836`** — test updated to assert 403 (current pipeline behavior; PlatformAdmin's empty permission set fails Authorization before TenantScopeBeginMiddleware's 401-missing-tenant path fires). Renamed to `Platform_admin_without_tenant_gets_403_on_tenant_scoped_route` with an inline comment documenting the pipeline-order race and the alternative `TenantClaimRequiredMiddleware` fix if the diagnostic 401 contract becomes important again.

2. `Kartova.Api.IntegrationTests` (5 tests) — fail host-startup because `KartovaIdentity__Keycloak__AdminClientSecret` env-var is not set in that project's bootstrap. The E1 fix `745917a` only patched `KartovaApiFixtureBase` in `tests/Kartova.Testing.Auth`. The Api.IntegrationTests project has its own host bootstrap that doesn't inherit from that fixture. **Fix scope: mirror the env-var assignment in the Api.IntegrationTests fixture.** Carry-forward to Phase H test triage (NOT slice-blocking).

**Phase E commit log (Phase D→E delta, 9 commits):**

| Commit | Task | Notes |
|---|---|---|
| `745917a` | (Phase D fixup) | `fix(slice-9): Phase D test-infra fixups — PG GRANT CREATE for migrator + admin-secret env var`. Surfaced during E1; mirrors `docker/postgres/init.sql:19` + appsettings override path. NOT slice E1 work; split into its own commit per the slice-9 linear-history convention. |
| `9b24b57` | E1 | `feat(slice-9): enrich ApplicationResponse with Owner via IUserDirectory`. 4 production files: ApplicationResponse, ListApplicationsHandler + GetApplicationByIdHandler (primary-ctor IUserDirectory injection), Infrastructure csproj (+ ProjectReference SharedKernel.Identity). |
| `6573c05` | E1 | `test(slice-9): E1 Owner enrichment — unit + integration tests`. 8 unit tests + 4 integration tests + SeedUserInOrganizationAsync/DeleteUserInOrganizationAsync fixture helpers. |
| `e5aaf73` | E1 | `refactor(slice-9): E1 quality-review nits — HashSet ownerIds + safer cleanup order`. Distinct().ToList() → HashSet; user-row delete before app delete in finally blocks (users-table has no prefix-based bulk sweep — establishes the slice-9 cleanup-order convention). |
| `8cab5a7` | (Phase D fixup) | `fix(slice-9): move IPostAuthSyncHook fan-out from claims transform to tenant-scope middleware`. The PostAuthHook landed in D-phase but ran inside `UseAuthentication` (via `IClaimsTransformation`) while it touches `OrganizationDbContext` which needs the post-Auth `TenantScopeBeginMiddleware`. Surfaced when E1 ran the first integration test against the auth-touching path. Fix: relocate the fan-out to `TenantScopeBeginMiddleware` after `BeginAsync` + team-membership population. Single-fix commit; 95 unit tests pre-existing on TenantClaimsTransformation reshuffled (2 replaced by 1 regression test + 3 new ordering tests on the middleware). |
| `15395c9` | E2 | `feat(slice-9): add ?ownerUserId= filter to GET /catalog/applications`. ProblemTypes.InvalidOwner; ListApplicationsQuery.OwnerUserId (trailing optional positional); endpoint-level IUserDirectory validation (Option B chosen — Option A would have failed PaginationConventionRules arch rule); 4 unit + 4 integration tests. |
| `4715c87` | E2 | `refactor(slice-9): E2 quality-review fix — align cleanup order with E1 convention`. Code quality reviewer caught the E1 cleanup-order convention regression in two new finally blocks; trivial swap. |
| `4726371` | E3 | `feat(slice-9): enrich TeamMemberResponse with DisplayName + Email via IUserDirectory`. TeamMemberResponse gains 2 non-nullable string fields (Option A per spec §4.1 verbatim); GetTeamHandler modernized to primary-ctor + IUserDirectory injection + batch GetManyAsync; OrganizationEndpointDelegates.AddTeamMemberAsync gets single-shot GetAsync enrichment after handler success; KartovaApiFixture (Org-side) gets SeedUserInOrganizationAsync/DeleteUserInOrganizationAsync. 6 unit + 3 integration tests added. SPA explicitly deferred to F-phase per resume-prompt scope. |
| `b7530ac` | E3 | `refactor(slice-9): E3 quality-review nits — align fallback pattern + clarify test name`. GetTeamHandler aligned with OrganizationEndpointDelegates on the `info?.X ?? ""` pattern; test rename from "with_distinct_member_ids" → "once_with_all_member_ids" (the PK already guarantees uniqueness so the test never exercised dedup). |

**E-phase reconciliations learned (carry-forward for Phases F/G/H):**

1. **E1 reconciliation #1: Application.csproj does NOT need a SharedKernel.Identity reference** even though the plan said it would. Reason: the `ToResponse()` extension lives in Application but only uses types from SharedKernel (which is already referenced). The IUserDirectory injection happens in Infrastructure handlers via `with { Owner = ... }`. **Only Infrastructure needs the new ref.**

2. **E1 reconciliation #2: `[BoundedListResult]` and `PaginationConventionRules` block the "result record" pattern on List handlers.** E2 verified this — wrapping the handler return in a non-`CursorPage<T>` result record fails the arch rule. **Endpoint-level validation is the correct pattern for filter validation that needs a cross-module IUserDirectory call.** Mirrors slice-8 `AssignApplicationTeam` validation gate placement.

3. **E1 reconciliation #3 (CRITICAL — caught twice in code review): integration-test finally-block cleanup order is "user-row first, catalog/team row second".** Users-table rows have no prefix-based bulk sweep (Organization schema doesn't have a `DeleteUsersByPrefixAsync`), unlike catalog rows which can always be recovered by `DeleteApplicationsByPrefixAsync`. If catalog cleanup throws, the user-row leaks silently. Pattern established by `e5aaf73`; E2 caught regressing in `4715c87`; E3 honored from the start. **Phase F integration tests must follow this same order whenever they seed both `users` and any other-module rows.**

4. **E1 reconciliation #4: HashSet over Distinct().ToList() for batch-lookup ids.** Single allocation vs two; satisfies `IReadOnlyCollection<Guid>` interface contract. Pattern propagated from `ListApplicationsHandler` to `GetTeamHandler` in E3.

5. **E2 reconciliation: cursor codec extension for new filter dimensions is Phase H scope, not the slice that introduces the filter.** Slice-6 precedent encodes `IncludeDecommissioned`; E2 added `ownerUserId` filter but does NOT extend the cursor codec — documented in-code as a `// TODO: Phase H` comment on `ListApplicationsHandler`. No security implication (RLS bounds the result set); the SPA doesn't change filters mid-pagination.

6. **E3 reconciliation: when a Contracts record gains a new field, EVERY construction site needs updating — even ones not named in the plan.** Plan E3 named only `TeamDetail` (GET /teams/{id}), but `OrganizationEndpointDelegates.AddTeamMemberAsync` ALSO constructs `TeamMemberResponse` for the 201 body. Single-shot `IUserDirectory.GetAsync` (not batch) at write-path return sites; batch `GetManyAsync` at read-path materialization sites.

7. **E3 reconciliation: non-nullable strings with empty-string fallback per spec §4.1.** Diverges from E1's `UserDisplayInfo?` (nullable). Reason: spec §4.1 gives a verbatim record signature with `string DisplayName, string Email`; deviating would contradict the explicit non-nullable wire contract. Document the `""` fallback semantics in the record's XML doc so the SPA knows `""` means "missing display info / show the id."

8. **Phase D PostAuthHook ordering bug surfaced during E1.** Hooks needing a DbContext must run AFTER `TenantScopeBeginMiddleware` — not inside `IClaimsTransformation` (which fires during `UseAuthentication`). The fix in `8cab5a7` relocates the `IPostAuthSyncHook` fan-out into `TenantScopeBeginMiddleware` after `BeginAsync` + team-membership population. **Future modules registering `IPostAuthSyncHook` should expect the hook to run inside the tenant scope.** Documented in `IPostAuthSyncHook.cs` XML doc.

9. **Quality-review optional nits are worth applying when cheap.** E1 nit #1 (HashSet) and #2 (cleanup order) were applied; E1 nit #3 (rename `_` alias) was skipped because the rewrite would have added more code than it saved. E3 nit #1 (align fallback pattern) and #2 (rename test) were applied as small mechanical fixes. Pattern: spec/quality reviewer flags are debt — pay it if it costs less than the rationalization.

10. **E-phase verification at HEAD (`b7530ac`):** 509 unit + architecture tests green; full-solution build 0 warnings / 0 errors. Catalog integration 96/96 (including the new E1 + E2 wire-shape tests). Organization integration 69/70 (1 pre-existing failure tracked above, NOT slice-blocking).

**Next task: H1 (Integration tests for Phase D endpoints).** Plan file §"Task H1" lists the 16 scenarios from spec §11.3 with Testcontainers (KeyCloak + Postgres). Phase H is the verification ladder — H1 unit/integration, H2 architecture tests, H3 docker compose HTTP, H4 Playwright E2E (REQUIRED to close the F-phase verification gap), H5 simplify+mutation, H6 CHECKLIST + PR. Phase H is non-optional: CLAUDE.md DoD #5 mandates docker compose happy + negative HTTP paths for HTTP/auth/DB/middleware slices, and slice 9 is all three.

**Phase H preview (8 tasks, mostly cross-cutting):**

- **H1: integration tests for Phase D endpoints** (16 scenarios from spec §11.3 — invitation create/revoke/accept/expire, org profile/logo, user search, session bootstrap, cross-module owner enrichment) + `KeycloakAdminClient` Testcontainers integration test (spec §11.3 carry-forward).
- **H2: architecture tests for slice-9 boundaries** (`Kartova_SharedKernel_Identity_does_not_reference_AspNetCore`, `Organization_owns_users_and_invitations_tables`, `Catalog_does_not_reference_Organization_Domain`, `IDistributedLock_implementations_use_session_advisory_locks`).
- **H3: docker compose HTTP verification** — happy + negative paths captured per CLAUDE.md DoD #5.
- **H4: SPA E2E via Playwright MCP** — `Invitation_happy_path` + `Org_profile_logo_upload_visible_in_header` from spec §11.5. **Closes the F-phase verification gap.**
- **H5: /simplify, /misc:mutation-sentinel, /misc:test-generator, /superpowers:requesting-code-review, /pr-review-toolkit:review-pr, /deep-review**.
- **H6: Update CHECKLIST.md + push + open PR**.
- **H7: ✅ already shipped in `2bde836`**.
- **H8: Mirror `KartovaIdentity__Keycloak__AdminClientSecret` env-var into `Kartova.Api.IntegrationTests` host bootstrap** (D9-introduced ValidateOnStart blocks host startup; tracked since E-phase).

Workflow: still `superpowers:subagent-driven-development`. One implementer per task; two-stage review (spec + quality) per task. Phase H is heavily integration- and infrastructure-flavored — implementers will need Docker access for H1/H3/H4 (and likely H5 mutation work too).

**Docker availability:** REQUIRED for H1 (Testcontainers), H3 (docker compose up), H4 (Playwright against live stack). Confirm with the user before dispatching any of those if Docker is not running locally.

**Remaining task ledger** (mark each `[x]` as you ship):

### Phase A — Shared infrastructure foundation ✅ COMPLETE (9 tasks, 11 commits)

### Phase B — Database + domain ✅ COMPLETE (4 tasks, 6 commits)

### Phase C — JWT-claim sync + permissions ✅ COMPLETE (4 tasks, 6 commits)
- [x] C1: 7 new permission constants + role map + 28-cell matrix test (`f76dc68` + `44893c2`)
- [x] C2: Extend `ICurrentUser` with `JustAcceptedInvitationId` (`674463f`)
- [x] C3: `UserProjectionUpdater` + 3 unit tests (`120957f`)
- [x] C4: `IPostAuthSyncHook` + `OrganizationPostAuthSyncHook` wiring into `TenantClaimsTransformation` + 5 tests (`4cc5fa7` + `1b7834d`) — **ordering bug fixed in `8cab5a7`**

### Phase D — Backend endpoints ✅ COMPLETE (9 tasks, 20 commits + 2 D-fixups during E-phase: `745917a`, `8cab5a7`)
- [x] D1: `OrganizationUserDirectory` impl + DI + 4 unit tests (`675a455` + `4c58ac0`)
- [x] D2: Org profile DTOs + GET/PUT /me endpoints + 6 unit tests (`7e30b92` + `50d8d65`)
- [x] D3: SVG sanitization + magic-byte helper + 10 unit tests (`95c0435` + `adda5b6`)
- [x] D4: Logo upload/delete/serve endpoints with ETag/304 + 9 unit tests + CSP sandbox (`8d66ec5` + `ecb1be0` + `cb42987`)
- [x] D5: Invitation handlers + endpoints (create/list/revoke) + 16 unit tests (`e045fb5` + `e9fe010`)
- [x] D6: User search + detail endpoints + 9 unit tests (`211e5fc` + `b67889a`)
- [x] D7: Session bootstrap endpoint POST /api/v1/auth/session + 8 unit tests (`0e68a1a`)
- [x] D8: `ExpireInvitationsHostedService` + 4 unit tests (`dc4405a`)
- [x] D9: Program.cs wiring + appsettings + KeyCloak realm config + KeycloakAdminOptions validator + 3 unit tests (`cbef744` + `822d681`)

### Phase E — Catalog/Team integration ✅ COMPLETE (3 tasks, 7 commits + 2 D-fixups)
- [x] E1: `ApplicationResponse.Owner` enrichment via `IUserDirectory` (`9b24b57` + `6573c05` + `e5aaf73`)
- [x] E2: `?ownerUserId=` filter on /catalog/applications + 422 validation (`15395c9` + `4715c87`)
- [x] E3: `TeamMemberResponse` display info enrichment (`4726371` + `b7530ac`)

### Phase F — SPA ✅ COMPLETE (8 tasks, 11 commits — `577ff02..16ad0b0` plus `e5339d7` docs)

**F1 status note:** F1 was verification-only at this checkpoint timeline. The SPA-side const-object update landed as code in `577ff02`; both drift sentinels (`Ts_snapshot_equals_csharp_KartovaPermissions_All` C# arch test + the `permissions.ts` runtime drift guard exercised via `usePermissions.test.tsx`) were independently verified green at HEAD `16ad0b0` (recorded in `e5339d7`).

- [x] F1: 7 org.* entries in SPA `KartovaPermissions` const object (`577ff02`) + drift sentinels verified green at `16ad0b0` (`e5339d7` docs checkpoint — C# arch `KartovaPermissionsRules` 10/10 incl. `Ts_snapshot_equals_csharp_KartovaPermissions_All`; SPA vitest `usePermissions.test.tsx` 7/7 with no drift error)
- [x] F2: organization API hooks — `useOrgProfile` + `useUpdateOrgProfile` + `useLogoUrl` + `useUploadOrgLogo` + `useDeleteOrgLogo` (`7faf4df`; consolidates `useCurrentOrganization` into `useOrgProfile`). Branch-precursor SPA-side fixes: `97580d0` (OpenAPI snapshot refresh + TopBar alignment to `OrganizationResponse.displayName`) + `d87bd4a` (Dockerfile fix: include `Kartova.SharedKernel.Identity` in API restore stage).
- [x] F3: `OrganizationSettingsPage` + `LogoUploader` + zod schema (`5b77063`)
- [x] F4: invitations — page + create dialog + copy-link + revoke + minimal `useUser` shim (`e3ab92b`) + fixes (`5d6672a`: move dialog reset out of effect; tighten 'Pending' test assertion)
- [x] F5: users feature — search hook + detail page + combobox + `<OwnerLink>` (`d145225`) + fixes (`76cbe8a`: wire 'Try again' refetch + add combobox keyboard nav)
- [x] F6: session bootstrap + `OidcCallbackHandler` + `WelcomePage` + `LoginErrorPage` (`e26b0cd`)
- [x] F7: Settings nav group + logo-or-name TopBar + new routes (`dc9dd3c`)
- [x] F8: `AddMemberDialog` combobox + `OwnerLink` in catalog + `TeamDetailPage` display names (`16ad0b0`)

**Phase F reconciliations / carry-forwards:**

1. **F-phase shipped without per-task Playwright cold-start verification.** The resume prompt mandated `cold-start dev server → navigate → interact → snapshot` per task (ADR-0084), but the actual F-phase commits are vitest-only. Phase H4 must close this gap with the two E2E scenarios from spec §11.5: `Invitation_happy_path` + `Org_profile_logo_upload_visible_in_header`. **Do NOT claim Phase F "verified" until H4 is done.**
2. **No fresh full-solution build after F-phase.** The Phase E checkpoint (above) was at `b7530ac`; the F-phase added SPA code + one Dockerfile fix (`d87bd4a`) + one OpenAPI snapshot refresh (`97580d0`). Phase H must run a fresh `dotnet build` on the full solution + `npm run build` on the SPA + `npm test` to confirm no regression.
3. **OpenAPI snapshot drift fix landed in F-phase, not D-phase.** `97580d0` refreshed `web/src/shared/api/schema.ts` (or equivalent) after D-phase endpoint additions. This means the SPA wasn't truly green between D-phase end and F2; Phase H must verify the snapshot is now in sync with the running API (consider running the snapshot regen one more time as a sentinel check).
4. **F2 consolidated two organization hooks.** `useCurrentOrganization` was merged into `useOrgProfile`. Any external imports of `useCurrentOrganization` would have broken; the F2 commit must have updated callers. Phase H code-review pass should grep for stale references.

### Phase G — ADR-0100 ✅ COMPLETE (1 task, 1 commit)
- [x] G1: ADR-0100 (strict one-email-per-tenant) — file + README (Index table row, Authentication & Authorization category, Identity & auth topic tag, Keyword Index entries for KeyCloak/Identity scope/Cross-tenant duplicate email handling/duplicateEmailsAllowed, History row, frontmatter `adr_count: 99 → 100` + `last_updated: 2026-05-27 → 2026-05-29`)

### Phase H — Verification + DoD ← **START HERE**
- [ ] H1: Integration tests for Phase D endpoints (16 scenarios) + `KeycloakAdminClient` Testcontainers integration test (spec §11.3)
- [ ] H2: Architecture tests for slice-9 boundaries
- [ ] H3: docker compose HTTP verification (happy + negative paths captured)
- [ ] H4: SPA E2E via Playwright MCP with screenshots
- [ ] H5: /simplify, /misc:mutation-sentinel, /misc:test-generator, /superpowers:requesting-code-review, /pr-review-toolkit:review-pr, /deep-review
- [ ] H6: Update CHECKLIST.md + push + open PR
- [x] H7: ~~Update `AuthErrorTests.Platform_admin_without_tenant_hits_missing_tenant_on_tenant_scoped_route` to assert 403~~ ✅ shipped in `2bde836` as `fix(slice-9)` directly after the Phase E checkpoint
- [ ] H8: **Mirror `KartovaIdentity__Keycloak__AdminClientSecret` env-var into `Kartova.Api.IntegrationTests` host bootstrap** (D9-introduced ValidateOnStart blocks host startup in this project; not patched by E1's `745917a` which only covered `KartovaApiFixtureBase`)

**Please start by:**
1. Invoking `superpowers:subagent-driven-development` (the skill).
2. Reading the plan at the path above to lift the verbatim Task H1 text (and the 16-scenario list from spec §11.3 it points at).
3. Reading `tests/Kartova.Organization.IntegrationTests/` to inventory which scenarios already exist vs which need to be added — there's a real chance some H1 scenarios were added incrementally during D/E/F and only the missing ones need to be filled in.
4. Confirming Docker availability with the user before dispatching the H1 implementer (Testcontainers needs Docker to start KeyCloak + Postgres).
5. Dispatching H1 in batches per the plan's commit cadence (one commit per feature area — invitations, org profile + logo, user search, session bootstrap, cross-module owner enrichment).

**Frontend verification reminder:** Per ADR-0084, frontend changes need a Playwright MCP cold-start before claiming done — HMR cache can mask config errors. F-phase shipped vitest-only; **H4 is REQUIRED** to close the Playwright cold-start verification gap with the two E2E scenarios from spec §11.5.

**Phase H completion estimate:** H1 likely 1-2 sessions (16 integration scenarios + Testcontainers). H2 + H8 small. H3 + H4 require Docker stack up. H5 (mutation + simplify + review) is the largest single-session chunk historically — budget accordingly. H6 closes the slice.

---

## Notes for future sessions

- **Phase E task velocity:** All 3 E-tasks + Phase D PostAuthHook fix in one session. Two-stage review (spec + quality) ran 3 times; both reviewers approved all tasks (E2 needed one cleanup-order fix, E3 needed two cosmetic nits, all small).
- **Update this file at every checkpoint** so the next session sees the latest progress.
- **Spec/plan reconciliations** should always commit on the feature branch with message prefix `docs(slice-9):` for spec/plan docs, or `fix(slice-9):` / `refactor(slice-9):` / `test(slice-9):` for infrastructure/code fixes. Each E-phase task that produced quality-review fixes used a separate `refactor(slice-9):` commit for clean linear history.
- **If a subagent surfaces a real architectural question**, the controller (you) makes the call — do not push the question down into another subagent. E-phase examples: (a) E1's option B vs option A for Application.csproj refs; (b) E1's quality-review nit selection (apply 2 of 3, skip the third with justification); (c) E2's endpoint-level vs handler-level validation (option B chosen for arch-rule compatibility); (d) E2's cursor-codec defer to Phase H; (e) E3's nullable-vs-non-nullable string choice (option A per spec §4.1 verbatim); (f) Phase D PostAuthHook scope fix approach selection (option a — relocate fan-out — over options b/c).
- **If `git show <SHA>` makes a deviation visible during review, fix it in a new commit** — never amend or rewrite (clean linear history preserved for `requesting-code-review` at slice boundary). Every E-phase fix-up was a NEW commit.
- **CLAUDE.md DoD #5 requires Docker happy + negative HTTP paths captured** for HTTP/auth/DB/middleware slices. This slice is all three. H3 (Phase H) is non-negotiable. Phase E exercised the integration surface enough to surface and fix the Phase D PostAuthHook ordering bug — H3 will run the full docker compose curl flow against a real KeyCloak.
- **Mutation testing target 80%** per `stryker-config.json`. H5's mutation-sentinel + test-generator loop is non-optional. E-phase added MC/DC-conscious unit tests on every new branch (Owner populated / null / orphan; OwnerUserId filter set / unset / no-match; TeamMember display populated / fallback). **F-phase should continue this** — every new SPA hook gets a happy + error test; every new component gets a render test and a permission-gated test.
- **Phase A's `KeycloakAdminClient` doesn't have an integration test against a real KeyCloak container yet** — H1 scope.
- **E-phase Nits carried forward** (consider in Phase H mutation-sentinel + simplify passes, not Phase F scope):
  1. Carry-forward from C-phase: add `.ValueGeneratedNever()` on `User.Id` and `Invitation._id` defensively.
  2. Carry-forward from C-phase: document the `WHERE status = 1` filter in `AddInvitationsTable` migration with a SQL-side comment.
  3. Carry-forward from C-phase: `OrganizationPostAuthSyncHook` claim names hardcoded.
  4. Carry-forward from C-phase: `OrganizationPostAuthSyncHook.cs` filename cosmetic.
  5. **Carry-forward from D-phase:** `IOrganizationQueries.GetCurrentAsync` + `OrganizationQueries` are now dead code on the /me path (D2 swap). Cleanup candidate.
  6. **Carry-forward from D-phase:** separate `LogoTooLarge` problem-type URI rather than reusing `UnsupportedLogoMedia` for 413.
  7. **Carry-forward from D-phase:** `ProblemTypes.UnsupportedLogoMedia` URI shared across 415/413/422. Status code disambiguates; split if a future SPA caller wants to discriminate by `type` URI alone.
  8. ~~Add `Content-Security-Policy` header on the SVG serving path~~ ✅ **ADDRESSED in `cb42987`**.
  9. **Carry-forward from D-phase:** D4 has no integration test for the 413 streaming guard, 415 mime parse, or `If-None-Match` round-trip. H1 will cover this.
  10. **Carry-forward from D-phase D5:** Race-condition gap — `db.Users.AnyAsync` + `db.Invitations.FirstOrDefaultAsync` pre-checks are NOT transactional with the KC create. Phase H.
  11. **Carry-forward from D-phase D5:** `CreateInvitationHandler` SaveChangesAsync failure leaks a KC user (compensation gap acknowledged in XML doc). Phase H.
  12. **Carry-forward from D-phase D8:** Broaden-the-catch mutation gap — no test for non-NotFound KC errors propagating during the expiry sweep. Phase H mutation work.
  13. **Carry-forward from D-phase D8:** Test `ServiceProvider` instances not disposed (minor resource hygiene). Phase H test cleanup.
  14. **Carry-forward from D-phase D9:** `KeycloakRealmSeedRules` architecture test doesn't cover the new `kartova-admin` client + service-account user. Phase H.
  15. **Carry-forward from D-phase D9:** `FrontendBaseUrl` in production `appsettings.json` defaults to `http://localhost:5173`. Consider extending the D9 sentinel-rejector to also reject `localhost` in non-Development environments. Phase H.
  16. **Carry-forward from D-phase reviews:** `OrganizationEndpointDelegates.cs` is now ~860 lines (E3 added another ~10 lines for the AddTeamMember IUserDirectory enrichment). Phase H is the right boundary to split into per-resource delegate files (`OrganizationProfileDelegates.cs`, `TeamDelegates.cs`, `InvitationDelegates.cs`, `UserDelegates.cs`, `AuthDelegates.cs`).
  17. **From E2:** Cursor codec does NOT encode `ownerUserId`. Cursor-replay under a different `ownerUserId` produces inconsistent results without an error. TODO comment in `ListApplicationsHandler.cs`. Phase H if the SPA ever changes filters mid-pagination (it doesn't today).
  18. **From E1+E2+E3 finally-block convention:** `users` row delete must always run before any other module's row delete in integration test cleanup. Codify as a `KartovaApiFixture` helper (`SeedAndAutoDisposeUserAsync`?) if it gets used more than 5 times in Phase F or H.
  19. **From E3:** `KartovaApiFixture` on the Organization side now has `SeedUserInOrganizationAsync(Guid tenantId, ...)` while the Catalog side helper added in E1 takes `TenantId tenantId` (strong-typed). Cross-module helper-API drift — align on `TenantId` in Phase H cleanup pass.
  20. **From E-phase verification:** `Kartova.Api.IntegrationTests` 5 tests fail host-startup because the admin-secret env-var isn't set in that project (D9 ValidateOnStart). Tracked as H8 above. Pre-existing on bare HEAD; NOT slice-blocking.
  21. ~~**From Phase D PostAuthHook fix:** `AuthErrorTests.Platform_admin_without_tenant_hits_missing_tenant_on_tenant_scoped_route` asserts 401 but the post-fix pipeline returns 403.~~ ✅ **RESOLVED in `2bde836`.** Note for future maintainers: if the diagnostic 401 missing-tenant contract becomes important again (e.g., a platform-admin SDK that wants to distinguish "select a tenant first" from generic 403s), the fix is a small `TenantClaimRequiredMiddleware` between `UseAuthentication` and `UseAuthorization` that short-circuits with 401 for endpoints carrying `RequireTenantScopeMarker`.
