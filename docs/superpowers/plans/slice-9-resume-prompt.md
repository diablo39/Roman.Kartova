# Slice 9 — Resume Execution Prompt

Paste the prompt below into a fresh `/clear`'d Claude Code session at the project root (`C:\Projects\Private\Roman.Gig2`).

---

## Prompt to paste

I'm continuing execution of slice 9 (organization & people management). Context:

**Spec:** `docs/superpowers/specs/2026-05-27-slice-9-organization-people-management-design.md`
**Plan:** `docs/superpowers/plans/2026-05-27-slice-9-organization-people-management-plan.md`
**Branch:** `feat/slice-9-organization-people-management` (already checked out)

**Status: Phases A + B + C complete (28 commits since branch start).** Full-solution build: 0 warnings, 0 errors. Full unit + architecture pass: green across all suites (Kartova.Organization.Tests 67 + Kartova.Organization.Infrastructure.Tests 9 + Kartova.SharedKernel.Identity.Tests 5 + Kartova.SharedKernel.AspNetCore.Tests 95 + Kartova.Catalog.Infrastructure.Tests 3 + Kartova.Catalog.Tests 72 + Kartova.SharedKernel.Tests 106 + Kartova.ArchitectureTests 63 = 420/420 unit + arch passing). Phase A's Docker-dependent A8 PostgresAdvisoryLock Testcontainers integration tests + Phase B's B4 migrator smoke-test against local docker-compose postgres were both verified at end-of-Phase-B and **still hold** — no DB-layer changes in Phase C. Phase C is pure C#/EF code + tests, no Docker dependency.

**Phase C commit log:**

| Commit | Task | Notes |
|---|---|---|
| `f76dc68` | C1: 7 org permission constants + role map + matrix test + TS snapshot | Full two-stage review. **Plan claimed `KartovaPermissions.All` is "reflection-driven" — it is NOT.** `All` is a hand-maintained array; reflection enforcement lives in `KartovaPermissionsRules.All_collection_contains_every_public_string_constant` (arch test). Adding a constant requires editing the `All` array too. Order: catalog.* → team.* → org.* mirrors declaration order. Drift sentinel `Ts_snapshot_equals_csharp_KartovaPermissions_All` covered by updating `web/src/shared/auth/permissions.snapshot.json` (now 17 entries) in the same commit. |
| `44893c2` | fix: Viewer perm-count assertion broken by C1 | **Real C1 regression my spec reviewer missed.** `KartovaRolePermissionsTests.Viewer_can_read_catalog_and_teams` had `Assert.AreEqual(2, perms.Count)`; C1 raised Viewer to 4 perms. Replaced the brittle count assertion with contain-only style (matching the convention in adjacent role tests like `Member_can_read_register_edit_forward_lifecycle`) + a negative containment guard against Viewer gaining write perms. Hotfix committed separately, NOT amended into `f76dc68` (clean linear history for slice-boundary `requesting-code-review`). |
| `674463f` | C2: `JustAcceptedInvitationId` on `ICurrentUser` + `ITenantContext` + `TenantContextAccessor` + `HttpContextCurrentUser` + 4 new tests | Full two-stage review. **Plan said all three files were in `Kartova.SharedKernel.AspNetCore` — that was wrong.** `ITenantContext` + `TenantContextAccessor` actually live in `src/Kartova.SharedKernel/Multitenancy/`; only `ICurrentUser` is in AspNetCore. The mutator `SetJustAcceptedInvitation(Guid)` is the only call-site addition; `Clear()` resets to null. **Production stub plumbing flagged but unplanned:** `FakeTenantContext` in `tests/Kartova.SharedKernel.AspNetCore.Tests/ModuleRouteExtensionsTests.cs` and `StubCurrentUser` in `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/CrossTenantWriteTests.cs` both implement these interfaces in test code and must get the new member (build-break otherwise). 2 extra files beyond the plan's 6. |
| `120957f` | C3: `UserProjectionUpdater` + 3 unit tests | Full two-stage review. **Plan placed production code in `Kartova.Organization.Application` — Clean-Architecture violation.** The class directly takes `OrganizationDbContext` as a parameter; putting it in Application would force Application → Infrastructure. Moved to `Kartova.Organization.Infrastructure` (matches existing repo pattern: all handlers/queries that touch DbContext live in Infrastructure). Tests landed in existing `Kartova.Organization.Infrastructure.Tests` (NOT a new `Application.Tests` project — would have violated the resume-prompt-pinned no-new-test-projects rule). Csproj added `Microsoft.EntityFrameworkCore.InMemory` + `Microsoft.Extensions.TimeProvider.Testing` (both already in `Directory.Packages.props`, no version pin needed). |
| `4cc5fa7` | C4: `IPostAuthSyncHook` + `OrganizationPostAuthSyncHook` + `TenantClaimsTransformation` extension + `OrganizationModule` DI + 2 transformer tests + 3 hook tests | Full two-stage review. **Three plan-vs-reality compile bugs surfaced:** (1) `tenantContext.TenantId is not { } tenantId` — won't compile against the value-type `TenantId` struct; fixed to `if (!tenantContext.IsTenantScoped) return; var tenantId = tenantContext.Id;`. (2) Plan referenced `tenantContext.TenantId`; actual property is `tenantContext.Id`. (3) `pending.Id.Value` works as plan says — `InvitationId` is `readonly record struct InvitationId(Guid Value)`. **Architectural choice:** plan said "inject hooks via constructor" into `TenantClaimsTransformation`; kept the existing `IServiceProvider _services` lazy-resolution pattern (`_services.GetServices<IPostAuthSyncHook>()`). The existing pattern handles the singleton/scoped registration boundary correctly. **Tests beyond plan:** plan specified ZERO tests for C4; added 2 transformer tests (hook invoked + hook skipped for unauthenticated) and 3 hook tests (pending-accepted, no-pending-skip, expired-skip) per CLAUDE.md DoD requirements. `OrganizationPostAuthSyncHook` is `internal sealed`; reached from tests via new `<InternalsVisibleTo Include="Kartova.Organization.Infrastructure.Tests" />` on Infrastructure csproj. |
| `1b7834d` | docs: TenantClaimsTransformation XML doc note | One-line addition to the class summary noting the new `IPostAuthSyncHook` fan-out (code-quality reviewer flagged the stale doc as Should-fix). Separate doc commit, not amended. |

**Reconciliations made during Phase C (worth remembering for D/E/F):**

1. **`KartovaPermissions.All` is hand-maintained array, not reflection-driven.** Future D/E/F constants must edit both the constant block AND the `All` array; `KartovaPermissionsRules.All_collection_contains_every_public_string_constant` will fail otherwise.
2. **`web/src/shared/auth/permissions.snapshot.json` has 17 entries** (10 catalog/team + 7 org). The drift sentinel `Ts_snapshot_equals_csharp_KartovaPermissions_All` passes. **BUT** `web/src/shared/auth/permissions.ts` const object still has only 10 entries — the SPA's runtime drift-guard in `permissions.ts` will throw on boot when F1 lands and someone runs the SPA. **F1 must update `permissions.ts` to match `permissions.snapshot.json` (17 entries) in the same commit.** No SPA work in Phase C, so this is forward-flagged, not a current bug.
3. **`ITenantContext` / `TenantContextAccessor` live in `Kartova.SharedKernel/Multitenancy/`**, not in `Kartova.SharedKernel.AspNetCore` (where the plan claimed). Only `ICurrentUser` + `HttpContextCurrentUser` are in AspNetCore. D-phase work touching tenant-context mutators (e.g., D7 session bootstrap) — work from this corrected file layout.
4. **`UserProjectionUpdater` lives in `Kartova.Organization.Infrastructure`** (NOT Application). D-phase handlers can DI-inject it. It's `public sealed class UserProjectionUpdater(TimeProvider clock)` — `UpsertAsync(OrganizationDbContext db, Guid userId, string email, string? givenName, string? familyName, TenantId tenantId, CancellationToken ct)`. The update path **does not touch CreatedAt or TenantId** (load-bearing: the (TenantId, Email) unique index would corrupt if tenant changed).
5. **`OrganizationPostAuthSyncHook` is `internal sealed`** and registered as `AddScoped<IPostAuthSyncHook, OrganizationPostAuthSyncHook>()` in `OrganizationModule.RegisterServices`. D-phase handlers that need to know "did the caller just accept an invitation?" should read `ICurrentUser.JustAcceptedInvitationId` (one-shot value, populated by this hook, reset on next request via `TenantContextAccessor.Clear()`).
6. **`User.Id` does NOT call `.ValueGeneratedNever()`** (EF defaults to `ValueGeneratedOnAdd` for Guid PKs). It works in practice because `UserProjectionUpdater.UpsertAsync` always assigns `Id = userId` from JWT `sub` explicitly. **Defensive carryover:** any new D-phase code path that inserts `User` rows must also explicitly assign `Id`. Same applies to `Invitation._id` (assigned by `Invitation.Create` via `Guid.NewGuid()`). The C-phase nits list suggested adding `.ValueGeneratedNever()` defensively in a later cleanup — still on the table for Phase H if mutation testing flags it.
7. **`Microsoft.EntityFrameworkCore.InMemory` is now in `Kartova.Organization.Infrastructure.Tests.csproj`.** D-phase tests can reuse the `NewInMemory()` factory pattern from `UserProjectionUpdaterTests.cs` (each test gets a unique `Guid.NewGuid()`-suffixed DB name to avoid cross-test pollution).
8. **`<InternalsVisibleTo Include="Kartova.Organization.Infrastructure.Tests" />` is on the Infrastructure csproj.** D-phase handlers, endpoint delegates, and DI helpers in Infrastructure can stay `internal sealed` and still be unit-testable from the same project. Mirror this pattern when adding new internal types.
9. **C-phase verification:** all 420 unit + arch tests green at HEAD (`Kartova.Organization.Tests` 67 + `Kartova.Organization.Infrastructure.Tests` 9 + `Kartova.SharedKernel.Identity.Tests` 5 + `Kartova.SharedKernel.AspNetCore.Tests` 95 + `Kartova.Catalog.Infrastructure.Tests` 3 + `Kartova.Catalog.Tests` 72 + `Kartova.SharedKernel.Tests` 106 + `Kartova.ArchitectureTests` 63). No regressions introduced into other modules.
10. **CLAUDE.md DoD #2 was strictly followed in Phase C** — every task got per-task spec-compliance + code-quality subagent reviews; the C1 regression hotfix (`44893c2`) only surfaced because C2's verification run exposed it. **Lesson for D-phase:** when a subagent reports "DONE" after running only the focused-filter tests, also run the full module test suite (or at least the sibling test classes that share the SUT) — focused filters can hide hard-coded-count regressions in sibling tests.

**Next task: D1 (`OrganizationUserDirectory` impl + DI + 4 unit tests).** Plan file `docs/superpowers/plans/2026-05-27-slice-9-organization-people-management-plan.md` §"Task D1: `OrganizationUserDirectory` implementation + DI" starting at line 2375 has the verbatim text.

**Workflow:** Use `superpowers:subagent-driven-development`. One implementer subagent per task. Full two-stage review (spec compliance + code quality) for tasks with behavior/tests/handlers/migrations. For pure-declaration tasks (interfaces, DTO records, enum types, ADR docs) self-verify by reading the committed file. **After each implementer reports DONE on a focused-filter test pass, also run the full module test suite to catch sibling regressions** (C1→KartovaRolePermissionsTests was the C-phase example).

**Conventions worth re-loading from CLAUDE.md before dispatching:**

- **Central Package Management** — versions in `Directory.Packages.props`, not in csproj `Version=`. Phase A+B added: `Duende.IdentityModel 8.1.0`, `Microsoft.Extensions.Http 10.0.0`, `Microsoft.Extensions.Options.ConfigurationExtensions 10.0.0`, `Microsoft.Extensions.Hosting 10.0.0`, `Microsoft.Extensions.TimeProvider.Testing 10.5.0`, `Microsoft.Extensions.DependencyInjection 10.0.2`, `NSubstitute 5.3.0`, `Testcontainers* 4.0.0`. Phase C csproj edits used existing-pinned packages only (`Microsoft.EntityFrameworkCore.InMemory 10.0.7`, `Microsoft.Extensions.TimeProvider.Testing 10.5.0` already pinned). `System.Net.Http.Json` is **NOT** explicitly referenced (`net10.0` shared framework; NU1510 under `TreatWarningsAsErrors`).
- Solution: `Kartova.slnx` (XML). New csprojs via `dotnet sln Kartova.slnx add <path>`.
- `TreatWarningsAsErrors=true` everywhere; zero warnings.
- `[ExcludeFromCodeCoverage]` on Contracts assemblies + `*Dto`/`*Request`/`*Response` types + design-time factories + `IModule` composition classes (enforced by `ContractsCoverageRules`). NOT on interfaces/exceptions/aggregates/value types/enums.
- DI extensions live in `Add<Subject>Extensions.cs` (per `AddModuleDbContextExtensions.cs` convention).
- Internal types tested directly via `<InternalsVisibleTo Include="…Tests" />` on the SUT csproj. **Organization.Infrastructure now has this attribute** — internal types from D-phase work can stay internal.
- Test csproj idiom: `Microsoft.NET.Sdk` + explicit MSTest 4.x packages + `coverlet.collector` + `Microsoft.NET.Test.Sdk` (NOT `MSTest.Sdk/3.10.0` — plan templates lean on that, repo doesn't).
- Test files: `Kartova.SharedKernel.Tests` + `Kartova.SharedKernel.AspNetCore.Tests` + `Kartova.Organization.Tests` use **explicit per-file `using Microsoft.VisualStudio.TestTools.UnitTesting;`** (no GlobalUsings). `Kartova.Organization.Infrastructure.Tests` + `Kartova.ArchitectureTests` use **csproj-level `<Using Include="Microsoft.VisualStudio.TestTools.UnitTesting" />`** — no per-file using. Check the destination test project's csproj before deciding.
- Organization module's domain test project is `src/Modules/Organization/Kartova.Organization.Tests/`. Infrastructure-layer tests (including anything touching `OrganizationDbContext`, even via InMemory) go in `src/Modules/Organization/Kartova.Organization.Infrastructure.Tests/`. **Do NOT scaffold new test projects** for slice 9.
- Organization module's EF config filename convention is `*EntityTypeConfiguration.cs`.
- Windows PowerShell preferred for `dotnet`/`docker` commands; Git Bash lacks `grep -P` (use `-E` or `Select-String`).
- Use `roslyn-codelens` MCP for impact analysis before extending hot methods or renaming widely-used members.

**Docker availability:** Phase D tasks D1-D8 are pure C# (handlers + endpoints + DI). D9's appsettings + KeyCloak realm config is config-only. Docker is needed for Phase H integration tests (H1-H4) and DoD verification (H3). As of last session, Docker daemon was running and local containers (`romangig2-postgres-1` / `romangig2-keycloak-1` / `romangig2-keycloak-db-1` / `romangig2-api-1`) were healthy — re-verify with `docker ps` before Phase H.

**Remaining task ledger** (mark each `[x]` as you ship):

### Phase A — Shared infrastructure foundation ✅ COMPLETE (9 tasks, 11 commits)

### Phase B — Database + domain ✅ COMPLETE (4 tasks, 6 commits)

### Phase C — JWT-claim sync + permissions ✅ COMPLETE (4 tasks, 6 commits)
- [x] C1: 7 new permission constants + role map + 28-cell matrix test (`f76dc68` + `44893c2`)
- [x] C2: Extend `ICurrentUser` with `JustAcceptedInvitationId` (`674463f`)
- [x] C3: `UserProjectionUpdater` + 3 unit tests (`120957f`)
- [x] C4: `IPostAuthSyncHook` + `OrganizationPostAuthSyncHook` wiring into `TenantClaimsTransformation` + 5 tests (`4cc5fa7` + `1b7834d`)

### Phase D — Backend endpoints ← **START HERE**
- [ ] D1: `OrganizationUserDirectory` impl + DI + 4 unit tests
- [ ] D2: Org profile DTOs + GET/PUT /me endpoints
- [ ] D3: SVG sanitization + magic-byte helper + 7 unit tests
- [ ] D4: Logo upload/delete/serve endpoints with ETag/304
- [ ] D5: Invitation handlers + endpoints (create/list/revoke) + 5 unit tests
- [ ] D6: User search + detail endpoints
- [ ] D7: Session bootstrap endpoint POST /api/v1/auth/session
- [ ] D8: `ExpireInvitationsHostedService` + 2 unit tests
- [ ] D9: Program.cs wiring + appsettings + KeyCloak realm config

### Phase E — Catalog/Team integration
- [ ] E1: `ApplicationResponse.Owner` enrichment via `IUserDirectory`
- [ ] E2: `?ownerUserId=` filter on /catalog/applications + 422 validation
- [ ] E3: `TeamMemberResponse` display info enrichment

### Phase F — SPA
- [ ] F1: permissions.snapshot.json **AND `permissions.ts` const object** (C1 only updated the snapshot; the SPA-side const object lag is documented above — F1 must close it)
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
- [ ] H1: Integration tests for Phase D endpoints (16 scenarios) + `KeycloakAdminClient` Testcontainers integration test (spec §11.3) — A-phase deferred it to H1
- [ ] H2: Architecture tests for slice-9 boundaries
- [ ] H3: docker compose HTTP verification (happy + negative paths captured) — A8 + B4 smoke covered the DB layer; D-phase endpoints + happy-path JWT round-trip land here
- [ ] H4: SPA E2E via Playwright MCP with screenshots
- [ ] H5: /simplify, /misc:mutation-sentinel, /misc:test-generator, /superpowers:requesting-code-review, /pr-review-toolkit:review-pr, /deep-review
- [ ] H6: Update CHECKLIST.md + push + open PR

**Please start by:**
1. Invoking `superpowers:subagent-driven-development` (the skill).
2. Reading the plan at the path above to lift the verbatim Task D1 text (starts at line ~2375).
3. Reading `src/Kartova.SharedKernel.Identity/IUserDirectory.cs` to confirm the A2 contract D1 must implement.
4. Reading `src/Kartova.SharedKernel.Identity/IKeycloakAdminClient.cs` and the `SearchUsersAsync(string query, int limit, CancellationToken)` shape — D1's `OrganizationUserDirectory` will combine the local `users` projection (created by C3) with KeyCloak search results per spec §6.5.
5. Reading `src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationDbContext.cs` to confirm `DbSet<User> Users` is available + the `User` POCO shape from `src/Modules/Organization/Kartova.Organization.Domain/User.cs`.
6. Dispatching the D1 implementer with the verbatim task text + the conventions block above + the C-phase reconciliations as scene-setting context.

Continue until you hit a natural checkpoint. **End of Phase D is the obvious next stop, but D is 9 tasks** — fresh sessions can realistically complete 6-9 tasks before context budget gets tight. D5 (invitations: handler + 3 endpoints + 5 tests + DI) and D7 (session bootstrap) are the most complex; consider checkpointing after D5 if context is tight. D2 + D3 + D4 (logo upload — SVG sanitization + magic-byte sniffing + ETag serving) are tightly coupled and should land in the same session if possible. At your stopping point, update this resume file's progress ledger and write a new resume prompt for the next session.

---

## Notes for future sessions

- **Each fresh session can realistically complete ~7-10 tasks** before context budget gets tight. Phase A (9 tasks) + Phase B (4 tasks) + Phase C (4 tasks) each ran in one session. Phase D's 9 tasks are bigger on average (handlers + endpoints + tests + Wolverine wiring); plan for 1-2 sessions.
- **Update this file at every checkpoint** so the next session sees the latest progress.
- **Spec/plan reconciliations** should always commit on the feature branch with message prefix `docs(slice-9):` for spec/plan docs, or `fix(slice-9):` for infrastructure/code fixes. See `cc52d5a` (init.sql privilege), `06b215e` (Duende namespace), `44893c2` (Viewer count regression), `1b7834d` (TenantClaimsTransformation doc).
- **If a subagent surfaces a real architectural question**, the controller (you) makes the call — do not push the question down into another subagent. Examples this session: (a) C3 plan placed `UserProjectionUpdater` in Application (Clean-Arch violation) — controller redirected to Infrastructure before dispatching; (b) C4 plan used `is not { }` on a struct + wrong property name `TenantId` — controller surfaced the compile bugs in the dispatch prompt; (c) C4 plan said inject hooks via constructor — controller preserved the existing `IServiceProvider` lazy-resolution pattern.
- **If `git show <SHA>` makes a deviation visible during review, fix it in a new commit** — never amend or rewrite (clean linear history preserved for `requesting-code-review` at slice boundary). `44893c2` and `1b7834d` are good examples.
- **CLAUDE.md DoD #5 requires Docker happy + negative HTTP paths captured** for HTTP/auth/DB/middleware slices. This slice is all three. H3 (Phase H) is non-negotiable. A8 + B4 smoke-tests covered the DB layer; D-phase endpoints + happy-path JWT round-trip land in H3.
- **Mutation testing target 80%** per `stryker-config.json`. H5's mutation-sentinel + test-generator loop is non-optional. C-phase tasks added boundary tests + ParamName assertions preemptively to kill obvious mutants; B2 + B4 followed the same pattern. **D-phase tasks should continue this** — every new validator gets a boundary test + a ParamName assertion on the rejection path.
- **Phase A's `KeycloakAdminClient` doesn't have an integration test against a real KeyCloak container yet** — the spec §11.3 mentions `Invitation_create_persists_keycloak_user_and_db_row` etc., but those land in Phase H (H1) alongside the endpoints in Phase D. Don't accidentally schedule them earlier.
- **`OrganizationDbContextModelSnapshot.cs` is now sensitive** — adding new Domain entities in D will regenerate it. The B4 snapshot diff was bounded to the explicit B4 additions; any unexplained snapshot churn in subsequent migrations should be investigated before committing. D-phase doesn't add new tables (B4 already added users + invitations + organization profile columns), so the snapshot should NOT change in Phase D — if it does, investigate.
- **C-phase Nits carried forward** (consider in Phase H mutation-sentinel pass, not Phase D scope):
  1. Add `.ValueGeneratedNever()` on `User.Id` and `Invitation._id` defensively. C3 + C4 both rely on explicit Id assignment; mutation testing may not catch a future caller that forgets.
  2. Document the `WHERE status = 1` filter in `AddInvitationsTable` migration with a SQL-side comment (`/* InvitationStatus.Pending */`) — not worth a standalone commit, fold into the next migration touching that file.
  3. `OrganizationPostAuthSyncHook` claim names (`"email"`, `"sub"`, `"given_name"`, `"family_name"`) are hardcoded. `KartovaClaims` only defines `TenantId`/`RealmAccess`/`Permission`. If multiple D-phase callers ever read these OIDC standard claims, add `KartovaClaims.Sub`/`Email`/`GivenName`/`FamilyName` constants. No callers in scope today, so deferred.
  4. `OrganizationPostAuthSyncHook.cs` filename is the plan's `PostAuthHook.cs` (class is `OrganizationPostAuthSyncHook`). One-file-per-type convention would call for the filename to match. Cosmetic — defer unless Phase H touches the file for another reason.
