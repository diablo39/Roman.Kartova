# Slice 9 — Resume Execution Prompt

Paste the prompt below into a fresh `/clear`'d Claude Code session at the project root (`C:\Projects\Private\Roman.Gig2`).

---

## Prompt to paste

I'm continuing execution of slice 9 (organization & people management). Context:

**Spec:** `docs/superpowers/specs/2026-05-27-slice-9-organization-people-management-design.md`
**Plan:** `docs/superpowers/plans/2026-05-27-slice-9-organization-people-management-plan.md`
**Branch:** `feat/slice-9-organization-people-management` (already checked out)

**Status: Phases A + B + C + D COMPLETE (48 commits since branch start, 20 since C-phase end).** Full-solution build: 0 warnings, 0 errors. Full unit + architecture pass at HEAD `822d681`:

| Suite | Passed | Notes |
|---|---|---|
| `Kartova.Organization.Tests` | 77 | +10 from D3 logo validation tests |
| `Kartova.Organization.Infrastructure.Tests` | 66 | +57 from D1/D2/D4/D5/D6/D7/D8 handler tests |
| `Kartova.SharedKernel.Identity.Tests` | 8 | +3 from D9 KeycloakAdminOptions validator |
| `Kartova.SharedKernel.AspNetCore.Tests` | 95 | unchanged from C-phase |
| `Kartova.SharedKernel.Tests` | 106 | unchanged from C-phase |
| `Kartova.Catalog.Tests` | 72 | unchanged |
| `Kartova.Catalog.Infrastructure.Tests` | 3 | unchanged |
| `Kartova.ArchitectureTests` | 63 | unchanged |
| **TOTAL** | **490** | unit + architecture, all green |

Phase A's Docker-dependent A8 PostgresAdvisoryLock Testcontainers + Phase B's B4 migrator smoke-test were verified at end-of-Phase-B and **still hold** — D-phase added no new tables or migrations. D-phase added a hosted-service (`ExpireInvitationsHostedService`) registered in Program.cs that requires `AddPostgresDistributedLocks()` at runtime — Phase H integration tests will exercise the leader-elected loop against a real KC + Postgres stack.

**Phase D commit log (Phase C→D delta, 20 commits):**

| Commit | Task | Notes |
|---|---|---|
| `675a455` + `4c58ac0` | D1 | `OrganizationUserDirectory` + 4 tests + mutation-killing assertions |
| `7e30b92` + `50d8d65` | D2 | GET/PUT /me with `OrgProfileResponse` schema (replaces legacy `OrganizationDto`); If-Match parsing deferred until xmin token; 3-context persistence test pattern |
| `95c0435` + `adda5b6` | D3 | `LogoValidation` (magic-byte + Ganss SVG sanitizer); HtmlSanitizer 9.0.892 (security fix bump); span-only SVG prelude scan; named threshold; dropped `style` attribute |
| `8d66ec5` + `ecb1be0` + `cb42987` | D4 | PUT/DELETE/GET /me/logo with ETag/304; load-before-sanitize order; `EntityTagHeaderValue` parsing; CSP `sandbox` + `X-Content-Type-Options: nosniff` (security review finding addressed) |
| `e045fb5` + `e9fe010` | D5 | Invitation create/list/revoke + 3-way 409 model + compensation logic; `RevokeInvitationHandler.Upstream → 502` for consistency with Create |
| `211e5fc` + `b67889a` | D6 | GET /users typeahead + GET /users/{id}; trim before length-check; `[BoundedListResult]` attribute; `EF.Property<Guid>(t, "_id")` for unmapped Team.Id pattern |
| `0e68a1a` | D7 | POST /api/v1/auth/session bootstrap; spec field names (`Organization`, `OrgDisplayName`) over plan guesses; `KartovaRolePermissions.ForRole()` over `Map[role]`; CSP-style RequireTenantScope group at /api/v1/auth |
| `dc4405a` | D8 | `ExpireInvitationsHostedService` (hourly leader-elected sweep); placed in `Infrastructure.Admin` (Infrastructure → Admin would be circular); exposed `public Task ExpireDueAsync(IServiceProvider, ct)` for direct unit testing |
| `cbef744` + `822d681` | D9 | KeyCloak Admin client wiring + `kartova-admin` realm client + service-account-user; `ValidateDataAnnotations()` + `OVERRIDE_VIA_ENV` sentinel rejector (closes silent-misconfiguration footgun) |

Plus `77c85b3` + `1eb7163` (D1-D4 mid-checkpoint docs commits).

**Reconciliations learned across D-phase (carry-forward for Phases E/F/G/H):**

1. **Recurring Clean-Arch reconciliation: handlers taking `OrganizationDbContext` go in `Kartova.Organization.Infrastructure`, NEVER Application.** Application has no project-reference to Infrastructure. Adding one would be circular. Pattern established by slice-8 (`CreateTeamHandler` etc.), C3 (`UserProjectionUpdater`), D2 (`OrgProfileQueries`, `UpdateOrgProfileHandler`), D4 (`LogoCommands`), D5 (`CreateInvitationHandler`/`RevokeInvitationHandler`/`ListInvitationsHandler`/`InvitationSortSpecs`), D6 (`UserQueries`), D7 (`SessionStartHandler`). Application holds only the query *record* (`ListInvitationsQuery.cs`) — the *handler* goes in Infrastructure.

2. **Recurring placement reconciliation: classes that need `AdminOrganizationDbContext` go in `Kartova.Organization.Infrastructure.Admin`** (D8's `ExpireInvitationsHostedService`). Infrastructure → Infrastructure.Admin is also circular. Their DI registration lives in `Program.cs` directly (a future `AddOrganizationAdmin(IServiceCollection)` composition extension would consolidate this — deferred).

3. **`ITenantContext.Roles` is `IReadOnlyCollection<string>` — NOT `ICurrentUser.Role` (doesn't exist).** Pattern: `tenant.Roles.FirstOrDefault() ?? KartovaRoles.Viewer`. Spec §3 Decision #2 holds one role per principal, so `FirstOrDefault` is the correct accessor.

4. **`KartovaRolePermissions.ForRole(role)` instead of `Map[role]` for PlatformAdmin safety.** The `Map` dictionary has entries for Viewer/Member/TeamAdmin/OrgAdmin only — PlatformAdmin and ServiceAccount are deliberately absent. `Map[role]` throws `KeyNotFoundException`; `ForRole(role)` returns `EmptySet`.

5. **`EF.Property<Guid>(x, "_id")` for unmapped-PK shadow-property lookups.** Pattern verified in `TeamSortSpecs.IdSelector`, `InvitationSortSpecs.IdSelector`, `RevokeInvitationHandler`, and `SessionStartHandler` (via `InvitationSortSpecs.IdEquals`). `Team.Id` and `Invitation.Id` are computed properties that EF cannot translate; the private `_id` shadow is the only mapped path.

6. **`Team.Id` is `builder.Ignore`d** in `TeamEntityTypeConfiguration` — only the `_id` shadow is mapped. Joins against Teams need `t => EF.Property<Guid>(t, "_id")` on the Team side, not `t.Id` or `t.Id.Value`. This caught D6 unaware (the plan said `t.Id` would work) — the implementer found and fixed it.

7. **RFC 7807 envelope discipline:** every 4xx/5xx return uses `Results.Problem(type:, title:, detail:, statusCode:)`. NO bare `Results.NotFound()` / `Results.StatusCode(...)`. Title + detail tailored to context. Consistent with slice-8's `TeamNotFound()` helper.

8. **Three-context persistence test pattern** for write-path handlers: seed in one DbContext (dispose), act in a second (dispose), assert in a third (reload from store). Kills the "delete `SaveChangesAsync`" mutator that a single-context test misses. Canonical examples: `UpdateOrgProfileHandlerTests.HandleAsync_updates_aggregate_when_valid` (D2), `LogoCommandsTests.UploadAsync_accepts_valid_png_and_returns_etag` (D4), `RevokeInvitationHandlerTests.Happy_path_revokes_pending_invitation_and_deletes_kc_user` (D5).

9. **`Kartova.Organization.Tests` (domain test project) uses PER-FILE MSTest using; `Kartova.Organization.Infrastructure.Tests` uses csproj-level global Using.** Always check the destination csproj before deciding.

10. **HtmlSanitizer is pinned at 9.0.892** in `Directory.Packages.props` (NOT 9.0.886 — that version has known XSS GHSA-j92c-7v7g-gj3f and fails NU1902 under TreatWarningsAsErrors=true).

11. **`ProblemTypes` constants added in slice 9:** `UnsupportedLogoMedia` (D4 — used for 413/415/422 on logo endpoints); `EmailAlreadyInTenant`, `EmailAlreadyInvited`, `EmailAlreadyOnPlatform`, `InvitationNotPending` (D5 — invitation lifecycle 409s).

12. **`EntityTagHeaderValue.TryParse` for `If-None-Match` parsing, NOT manual `.Trim('"')`.** Reject weak validators via `etag.IsWeak`. Pattern: `GetLogoAsync` in `OrganizationEndpointDelegates.cs`. Future Phase E logo refs can mirror.

13. **`MediaTypeHeaderValue.TryParse(req.ContentType, out var mt)` + `mt.MediaType.Value?.ToLowerInvariant()`** for case-insensitive mime parsing with parameter stripping. Pattern: `UploadLogoAsync`.

14. **Endpoint delegate pattern: `internal static async Task<IResult>` methods on `OrganizationEndpointDelegates`, bound via method-group from `OrganizationModule.MapEndpoints` with `.RequireAuthorization(...)` + `.WithName(...)` + `.Produces<T>(...)` + `.ProducesProblem(...)` chains.** No inline lambdas. File at ~830 lines — Phase H is the right boundary to split into per-resource delegate files.

15. **Result records co-located with their handler** when tightly coupled (`UpdateOrgProfileResult`, `UploadLogoResult`, `CreateInvitationResult` + `CreateInvitationError` enum, `RevokeResult`). Infrastructure-tier — no `[ExcludeFromCodeCoverage]` needed.

16. **`MeTeamMembership(Guid TeamId, string Role)` lives in `Kartova.Organization.Contracts/MePermissionsResponse.cs`** — re-used by both `GetMePermissions` and `SessionStartResponse` (D7). Do NOT create a duplicate.

17. **D6: `EF.Functions.ILike` does NOT work on InMemory provider.** D6's `UserQueries.SearchAsync` uses `u.DisplayName.ToLower().Contains(qLower) || u.Email.ToLower().Contains(qLower)` instead. Postgres-side: Npgsql translates `ToLower()` to SQL `LOWER(...)` which can hit a functional index. InMemory-side: client-side evaluation, native string operations.

18. **D8: `LeaderElectedPeriodicService` already handles "skip-on-lock-unavailable" semantics.** Subclasses just override `ExecuteLeaderWorkAsync`. To unit-test the work without going through `PeriodicTimer`, expose a `public Task ExpireDueAsync(IServiceProvider, ct)` and have the override delegate to it.

19. **D9: `ValidateOnStart()` is a NO-OP without a registered validator.** `Microsoft.Extensions.Options.DataAnnotations 10.0.0` package + `.ValidateDataAnnotations()` activates `[Required, MinLength(1)]` annotations on options. Combine with `.Validate(o => !sentinel(o.Field), errorMessage)` for placeholder rejection. Pattern: `AddKeycloakAdminClient` in `Kartova.SharedKernel.Identity.ServiceCollectionExtensions`.

20. **D-phase verification at HEAD (`822d681`):** 490 unit + architecture tests green; full-solution build 0 warnings / 0 errors with `TreatWarningsAsErrors=true`. **Lesson reinforced:** after a subagent reports DONE on focused test filters, run the FULL solution build (`dotnet build .\Kartova.slnx`) to catch cross-project breakage.

**Next task: E1 (`ApplicationResponse.Owner` enrichment via `IUserDirectory`).** Plan file §"Task E1" starting at line ~3551 has the verbatim text. Phase E is **3 tasks** — small compared to Phase D. Realistic to complete E1 + E2 + E3 in one fresh session.

**Workflow:** Use `superpowers:subagent-driven-development`. One implementer subagent per task. Full two-stage review (spec compliance + code quality) for every task. **After each implementer reports DONE on a focused-filter test pass, also run the full module test suite and the full-solution build to catch sibling regressions.**

**Conventions worth re-loading from CLAUDE.md before dispatching:**

- **Central Package Management** — versions in `Directory.Packages.props`, not in csproj `Version=`. Phase A-D added: `Duende.IdentityModel 8.1.0`, `Microsoft.Extensions.Http 10.0.0`, `Microsoft.Extensions.Options.ConfigurationExtensions 10.0.0`, `Microsoft.Extensions.Options.DataAnnotations 10.0.0` (D9), `Microsoft.Extensions.Hosting 10.0.0`, `Microsoft.Extensions.TimeProvider.Testing 10.5.0`, `Microsoft.Extensions.DependencyInjection 10.0.2`, `NSubstitute 5.3.0`, `Testcontainers* 4.0.0`, `Microsoft.EntityFrameworkCore.InMemory 10.0.7`, `HtmlSanitizer 9.0.892`. **Phase E needs no new packages** — only existing `Kartova.SharedKernel` + `Kartova.SharedKernel.Identity` references on Catalog projects.
- Solution: `Kartova.slnx` (XML). New csprojs via `dotnet sln Kartova.slnx add <path>`.
- `TreatWarningsAsErrors=true` everywhere; zero warnings.
- `[ExcludeFromCodeCoverage]` on Contracts assemblies + `*Dto`/`*Request`/`*Response` types + design-time factories + `IModule` composition classes (enforced by `ContractsCoverageRules`). NOT on interfaces/exceptions/aggregates/value types/enums/Infrastructure handlers/Infrastructure result records.
- DI extensions live in `Add<Subject>Extensions.cs`.
- Internal types tested directly via `<InternalsVisibleTo Include="…Tests" />` on the SUT csproj.
- Test csproj idiom: `Microsoft.NET.Sdk` + explicit MSTest 4.x packages + `coverlet.collector` + `Microsoft.NET.Test.Sdk`.
- Test files: `Kartova.Organization.Tests` uses **explicit per-file `using Microsoft.VisualStudio.TestTools.UnitTesting;`**; `Kartova.Organization.Infrastructure.Tests` uses csproj-level `<Using>`. Check the destination test project's csproj before deciding.
- `[BoundedListResult]` attribute on classes named `^List.*Handler$` that don't return `Task<CursorPage<T>>` (arch rule `PaginationConventionRules`).

**Phase E preview (3 tasks):**

- **E1: `ApplicationResponse.Owner` enrichment via `IUserDirectory`.** Extend `ApplicationResponse` with nullable `Owner: UserDisplayInfo?`. Batch-fetch via `IUserDirectory.GetManyAsync` in the list path; single-fetch via `GetAsync` in the detail path. May require adding ProjectReferences from Catalog projects to `Kartova.SharedKernel.Identity` (D1's `OrganizationUserDirectory` is wired through DI; Catalog needs the interface visible at compile time). Update existing Catalog integration tests for the new shape.
- **E2: `?ownerUserId=` filter on `GET /catalog/applications` + 422 validation.** Add optional `ownerUserId: Guid?` to the list query. Filter via `WHERE a.OwnerUserId == ownerUserId` when set. Validate the supplied id resolves to a real user via `IUserDirectory.GetAsync` — 422 with `invalid-owner` problem type on null. **Need a new `ProblemTypes.InvalidOwner` constant** following the slice-9 pattern.
- **E3: `TeamMemberResponse` display info enrichment.** Add `DisplayName` + `Email` fields to `TeamMemberResponse`. Batch-fetch via `IUserDirectory` in the team-detail query. Update slice-8 integration + unit tests for the new shape.

**Docker availability:** Phase E tasks are pure C# (DTO extensions + EF query mods + endpoint param). Docker is needed for Phase H integration tests. Re-verify with `docker ps` before Phase H.

**Remaining task ledger** (mark each `[x]` as you ship):

### Phase A — Shared infrastructure foundation ✅ COMPLETE (9 tasks, 11 commits)

### Phase B — Database + domain ✅ COMPLETE (4 tasks, 6 commits)

### Phase C — JWT-claim sync + permissions ✅ COMPLETE (4 tasks, 6 commits)
- [x] C1: 7 new permission constants + role map + 28-cell matrix test (`f76dc68` + `44893c2`)
- [x] C2: Extend `ICurrentUser` with `JustAcceptedInvitationId` (`674463f`)
- [x] C3: `UserProjectionUpdater` + 3 unit tests (`120957f`)
- [x] C4: `IPostAuthSyncHook` + `OrganizationPostAuthSyncHook` wiring into `TenantClaimsTransformation` + 5 tests (`4cc5fa7` + `1b7834d`)

### Phase D — Backend endpoints ✅ COMPLETE (9 tasks, 20 commits)
- [x] D1: `OrganizationUserDirectory` impl + DI + 4 unit tests (`675a455` + `4c58ac0`)
- [x] D2: Org profile DTOs + GET/PUT /me endpoints + 6 unit tests (`7e30b92` + `50d8d65`)
- [x] D3: SVG sanitization + magic-byte helper + 10 unit tests (`95c0435` + `adda5b6`)
- [x] D4: Logo upload/delete/serve endpoints with ETag/304 + 9 unit tests + CSP sandbox (`8d66ec5` + `ecb1be0` + `cb42987`)
- [x] D5: Invitation handlers + endpoints (create/list/revoke) + 16 unit tests (`e045fb5` + `e9fe010`)
- [x] D6: User search + detail endpoints + 9 unit tests (`211e5fc` + `b67889a`)
- [x] D7: Session bootstrap endpoint POST /api/v1/auth/session + 8 unit tests (`0e68a1a`)
- [x] D8: `ExpireInvitationsHostedService` + 4 unit tests (`dc4405a`)
- [x] D9: Program.cs wiring + appsettings + KeyCloak realm config + KeycloakAdminOptions validator + 3 unit tests (`cbef744` + `822d681`)

### Phase E — Catalog/Team integration ← **START HERE**
- [ ] E1: `ApplicationResponse.Owner` enrichment via `IUserDirectory`
- [ ] E2: `?ownerUserId=` filter on /catalog/applications + 422 validation
- [ ] E3: `TeamMemberResponse` display info enrichment

### Phase F — SPA
- [ ] F1: permissions.snapshot.json **AND `permissions.ts` const object** (C1 only updated the snapshot; the SPA-side const object lag is documented — F1 must close it)
- [ ] F2: organization API hooks
- [ ] F3: OrganizationSettingsPage + LogoUploader + zod schema
- [ ] F4: invitations API hooks + InvitationsPage + InviteUserDialog + CopyInviteLinkBox
- [ ] F5: users API hooks + UserDetailPage + UserSearchCombobox + OwnerLink
- [ ] F6: auth/session API + OidcCallbackHandler + WelcomePage (reads `JustAcceptedInvitationId` from session-bootstrap response)
- [ ] F7: Router + Sidebar + Header updates
- [ ] F8: AddMemberDialog upgrade + Application table OwnerLink

### Phase G — ADR-0100
- [ ] G1: Write ADR-0100 (strict one-email-per-tenant)

### Phase H — Verification + DoD
- [ ] H1: Integration tests for Phase D endpoints (16 scenarios) + `KeycloakAdminClient` Testcontainers integration test (spec §11.3)
- [ ] H2: Architecture tests for slice-9 boundaries
- [ ] H3: docker compose HTTP verification (happy + negative paths captured)
- [ ] H4: SPA E2E via Playwright MCP with screenshots
- [ ] H5: /simplify, /misc:mutation-sentinel, /misc:test-generator, /superpowers:requesting-code-review, /pr-review-toolkit:review-pr, /deep-review
- [ ] H6: Update CHECKLIST.md + push + open PR

**Please start by:**
1. Invoking `superpowers:subagent-driven-development` (the skill).
2. Reading the plan at the path above to lift the verbatim Task E1 text (starts at line ~3551).
3. Reading `src/Modules/Catalog/Kartova.Catalog.Contracts/ApplicationResponse.cs` to see the current shape that needs the `Owner` field added.
4. Reading `src/Modules/Catalog/Kartova.Catalog.Application/ApplicationQueries.cs` (or equivalent) for the list+detail materialization sites.
5. Reading `src/Modules/Catalog/Kartova.Catalog.Contracts/Kartova.Catalog.Contracts.csproj` + `src/Modules/Catalog/Kartova.Catalog.Application/Kartova.Catalog.Application.csproj` + `src/Modules/Catalog/Kartova.Catalog.Infrastructure/Kartova.Catalog.Infrastructure.csproj` to determine which need new ProjectReferences to `Kartova.SharedKernel` and `Kartova.SharedKernel.Identity`.
6. Reading `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/` for the existing application-endpoint integration tests that need their response-shape assertions updated.
7. Dispatching the E1 implementer with the verbatim task text + the Phase D reconciliations above (especially #5 `EF.Property<Guid>(x, "_id")` — Catalog's Application.Id may also be unmapped depending on its EF config) + the conventions block.

**Phase E completion estimate:** all 3 tasks in one fresh session. E1 is the biggest (cross-module DTO change + integration test updates); E2 + E3 are smaller (filter param + DTO field).

---

## Notes for future sessions

- **Phase D D-task completion velocity:** D1-D4 in one session (logo trio coupled); D5-D9 in another (D5 alone consumed ~50% of session). Phase E is smaller — single session realistic.
- **Update this file at every checkpoint** so the next session sees the latest progress.
- **Spec/plan reconciliations** should always commit on the feature branch with message prefix `docs(slice-9):` for spec/plan docs, or `fix(slice-9):` / `refactor(slice-9):` / `test(slice-9):` for infrastructure/code fixes. Each D-phase task that produced quality-review fixes used a separate `refactor(slice-9):` commit for clean linear history.
- **If a subagent surfaces a real architectural question**, the controller (you) makes the call — do not push the question down into another subagent. D-phase examples: (a) D2's `IfMatchEndpointFilter` deferral; (b) D3's HtmlSanitizer security version bump; (c) D4's `ProblemTypes.UnsupportedLogoMedia` URI reuse; (d) D5's Upstream → 502 consistency; (e) D6's `EF.Property<Guid>("_id")` workaround for unmapped `Team.Id`; (f) D7's spec field-name priority over controller guesses; (g) D8's project placement in Infrastructure.Admin; (h) D9's `ValidateOnStart` no-op gap.
- **If `git show <SHA>` makes a deviation visible during review, fix it in a new commit** — never amend or rewrite (clean linear history preserved for `requesting-code-review` at slice boundary). Every D-phase fix-up was a NEW commit.
- **CLAUDE.md DoD #5 requires Docker happy + negative HTTP paths captured** for HTTP/auth/DB/middleware slices. This slice is all three. H3 (Phase H) is non-negotiable. A8 + B4 smoke-tests covered the DB layer; D-phase endpoints + happy-path JWT round-trip + invitation-acceptance flow land in H3.
- **Mutation testing target 80%** per `stryker-config.json`. H5's mutation-sentinel + test-generator loop is non-optional. D-phase added boundary tests + content-assertion tests + three-context persistence tests preemptively. **E-phase should continue this** — every new validator gets a boundary test; every new write-path handler gets a three-context persistence test.
- **Phase A's `KeycloakAdminClient` doesn't have an integration test against a real KeyCloak container yet** — the spec §11.3 mentions `Invitation_create_persists_keycloak_user_and_db_row` etc., but those land in Phase H (H1). Don't accidentally schedule them earlier.
- **D-phase Nits carried forward** (consider in Phase H mutation-sentinel + simplify passes, not Phase E scope):
  1. Carry-forward from C-phase: add `.ValueGeneratedNever()` on `User.Id` and `Invitation._id` defensively.
  2. Carry-forward from C-phase: document the `WHERE status = 1` filter in `AddInvitationsTable` migration with a SQL-side comment.
  3. Carry-forward from C-phase: `OrganizationPostAuthSyncHook` claim names hardcoded.
  4. Carry-forward from C-phase: `OrganizationPostAuthSyncHook.cs` filename cosmetic.
  5. **From D-phase:** `IOrganizationQueries.GetCurrentAsync` + `OrganizationQueries` are now dead code on the /me path (D2 swap). Cleanup candidate.
  6. **From D-phase:** the slice may want a separate `LogoTooLarge` problem-type URI rather than reusing `UnsupportedLogoMedia` for 413.
  7. **From D-phase:** `ProblemTypes.UnsupportedLogoMedia` URI is shared across 415/413/422. Status code disambiguates; if a future SPA caller wants to discriminate by `type` URI alone, split it then.
  8. ~~Add `Content-Security-Policy` header on the SVG serving path~~ ✅ **ADDRESSED in `cb42987`** — CSP `default-src 'none'; style-src 'unsafe-inline'; sandbox` + `X-Content-Type-Options: nosniff` set on both 304 and 200 paths of `GetLogoAsync`. If a future slice serves more binary endpoints, consider lifting these to a global middleware.
  9. **From D-phase:** D4 has no integration test for the 413 streaming guard, 415 mime parse, or `If-None-Match` round-trip — unit tests cover `LogoCommands` but not the endpoint delegates. H1 will cover this.
  10. **From D-phase D5:** Race-condition gap — `db.Users.AnyAsync` + `db.Invitations.FirstOrDefaultAsync` pre-checks are NOT transactional with the KC create. A concurrent invite of the same email could create two pending invitations. KC's own email uniqueness catches the 2nd, but a partial unique index `WHERE status='Pending'` on `invitations(tenant_id, email)` would close the race at the DB layer. Phase H.
  11. **From D-phase D5:** `CreateInvitationHandler` SaveChangesAsync failure leaks a KC user (compensation gap acknowledged in XML doc). Phase H.
  12. **From D-phase D8:** Broaden-the-catch mutation gap — no test for non-NotFound KC errors propagating during the expiry sweep. Phase H mutation work.
  13. **From D-phase D8:** Test `ServiceProvider` instances not disposed (minor resource hygiene). Phase H test cleanup.
  14. **From D-phase D9:** `KeycloakRealmSeedRules` architecture test doesn't cover the new `kartova-admin` client + service-account user. Add a mirror test to the existing `kartova-web` shape assertions. Phase H.
  15. **From D-phase D9:** `FrontendBaseUrl` in production `appsettings.json` defaults to `http://localhost:5173`. A forgotten env-var override would produce broken invitation emails. Consider extending the D9 sentinel-rejector to also reject `localhost` in non-Development environments. Phase H.
  16. **From D-phase reviews:** `OrganizationEndpointDelegates.cs` is now ~830 lines. Phase H is the right boundary to split into per-resource delegate files (`OrganizationProfileDelegates.cs`, `TeamDelegates.cs`, `InvitationDelegates.cs`, `UserDelegates.cs`, `AuthDelegates.cs`).
