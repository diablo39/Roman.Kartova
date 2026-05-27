# Slice 9 — Resume Execution Prompt

Paste the prompt below into a fresh `/clear`'d Claude Code session at the project root (`C:\Projects\Private\Roman.Gig2`).

---

## Prompt to paste

I'm continuing execution of slice 9 (organization & people management). Context:

**Spec:** `docs/superpowers/specs/2026-05-27-slice-9-organization-people-management-design.md`
**Plan:** `docs/superpowers/plans/2026-05-27-slice-9-organization-people-management-plan.md`
**Branch:** `feat/slice-9-organization-people-management` (already checked out)

**Status: Phases A + B complete (22 commits since branch start).** Full-solution build: 0 warnings, 0 errors. Full unit + architecture pass: green across all suites (Kartova.Organization.Tests 67 + Kartova.Organization.Infrastructure.Tests 3 + Kartova.SharedKernel.Identity.Tests 5 + Kartova.SharedKernel.AspNetCore.Tests 91 + Kartova.Catalog.Infrastructure.Tests 3 + Kartova.Catalog.Tests 72 + Kartova.SharedKernel.Tests 103 + Kartova.ArchitectureTests 62 = 406/406 unit + arch passing). Both Phase A and Phase B Docker-dependent verifications **have been run and passed**:
- A8 `PostgresAdvisoryLock` Testcontainers integration tests → 3/3 PASS against `postgres:18-alpine` (4s).
- B4 migrator smoke-test → all 4 new migrations applied to local docker-compose postgres; DB state verified end-to-end (8 tables, 6 forced-RLS `tenant_isolation` policies, `pg_trgm 1.6` installed, GIN trigram + filtered-partial indexes present, `chk_logo_complete` check constraint on `organizations`).

**Phase B commit log:**

| Commit | Task | Notes |
|---|---|---|
| `d11b641` | B1: Organization aggregate ext (`Description`, `OrgLogo`, `DefaultTimeZone`) | Full two-stage review. Spec-mandated `Name`→`DisplayName` rename was applied as part of B1 (plan assumed it was already done from slice-2; it wasn't). Production callers in `OrganizationQueries.cs`, `AdminOrganizationCommands.cs`, and `OrganizationAggregateTests.cs` updated. DB column stays `"name"` (B4 didn't rename it either — spec §4.4 only adds new columns). |
| `0957344` | B1 fix-up: tighten `OrgLogo` VO invariant + descriptive validator param names + boundary tests | Code-quality review surfaced two should-fix items: (1) `OrgLogo.Create` aliased the caller's byte[] (defensive-copy added via `(byte[])bytes.Clone()`); (2) `ValidateDescription(string? s)` had `nameof(s)` leaking; renamed parameter to `description`. Also `ValidateDisplayName(string name)`→`(string displayName)` for symmetry. Boundary tests added at exactly `256*1024` bytes and exactly `1024` chars; `ParamName` assertions on rejection tests. |
| `1851158` | B2: `Invitation` aggregate (status machine: Pending/Accepted/Revoked/Expired, 7-day expiry, role validation) + 9 domain tests + boundary/ParamName tests | Full two-stage review. **`KartovaRoles.All` was added in this commit** — the plan assumed slice-7 added it but slice 7 never did. Contents: `{Viewer, Member, TeamAdmin, OrgAdmin}` only — `PlatformAdmin` excluded (orthogonal to tenants) and `ServiceAccount` excluded (no realm role yet per ADR-0009). `FrozenSet<string>` with `StringComparer.Ordinal`, mirroring `KartovaRolePermissions.Map` keying. |
| `0ba570d` | B3: `User` projection POCO | Self-verified pure declaration (`User` is a projection per spec §4.3 — pure read model, source-of-truth = KeyCloak). Includes the static `User.ComputeDisplayName(given, family, email)` helper that C3's `UserProjectionUpdater` will consume. `[ExcludeFromCodeCoverage]` per spec. |
| `fde7230` | B4: EF migrations (pg_trgm + organizations profile columns + users + invitations) + EF configs | Full two-stage review. 4 new migrations: `20260527182222_EnablePgTrgmExtension`, `20260527182257_AddOrganizationProfileColumns`, `20260527182349_AddUsersTable`, `20260527182445_AddInvitationsTable`. 2 new EF configs (`InvitationEntityTypeConfiguration.cs`, `UserEntityTypeConfiguration.cs`) named per Organization-module convention (NOT `Ef*Configuration.cs` — the plan's filename was wrong). `OrganizationEntityTypeConfiguration.cs` extended (B1's `builder.Ignore` placeholders removed; real wiring + `OwnsOne(OrgLogo)` added). Both `OrganizationDbContext` and `AdminOrganizationDbContext` got `DbSet<User>` + `DbSet<Invitation>` (spec §4.8). |
| `cc52d5a` | fix: grant CREATE on db to migrator role for pg_trgm extension | **Real prod-relevant bug surfaced during B4 smoke-test.** The migrator role lacked `CREATE ON DATABASE kartova` privilege; `CREATE EXTENSION pg_trgm` failed with SQLSTATE 42501. PG 13+ allows non-superusers to install trusted extensions if they hold CREATE on the DB. Fixed in `docker/postgres/init.sql`. **Production environments must mirror this via Helm Secrets.** Future onboarding gets the fix from init.sql; the running container also got the same GRANT manually for the smoke-test. |

**Reconciliations made during Phase B (worth remembering for later phases):**

1. **Plan-spec drift on `Organization.Name` vs `DisplayName`.** Slice 2's baseline used `Name`; spec §1 + §13 explicitly call out `DisplayName` as canonical for slice 9. Rename applied in B1. **Future C/D/E/F code that references the org's display name should use `DisplayName`** (DB column is still `"name"`).
2. **Plan's `tests/Kartova.Organization.Domain.Tests/` path doesn't exist.** Org module's test project lives at `src/Modules/Organization/Kartova.Organization.Tests/` (namespace `Kartova.Organization.Tests`). All slice-9 domain tests land there; do **NOT** scaffold a new test project. Same applies to C3 if the plan tries to use a similar wrong path.
3. **EF config filename convention varies by module.** Organization module uses `*EntityTypeConfiguration.cs` (existing pattern: `Organization`, `Team`, `TeamMembership`). Catalog module uses `Ef*Configuration.cs` (`EfApplicationConfiguration`). Plan templates lean on the Catalog idiom. **In the Organization module, always use `*EntityTypeConfiguration.cs`.**
4. **`OrganizationDbContext.OnModelCreating` already calls `ApplyConfigurationsFromAssembly`** — new `IEntityTypeConfiguration<T>` types are auto-discovered. The plan's "add explicit `mb.ApplyConfiguration(new ...)` calls" instruction is unnecessary and would supersede the assembly scan. Same applies to `AdminOrganizationDbContext`.
5. **`KartovaRoles.All` is now defined** in `src/Kartova.SharedKernel/Multitenancy/KartovaRoles.cs` and contains the four tenant-scoped invitable roles. C1+ can reference it without re-defining.
6. **`OrgLogo` is mapped as `OwnsOne` on `Organization`** with column names `logo_bytes`, `logo_mime_type`, `logo_content_hash`. `MimeType` capped at 32 chars; `ContentHash` at 64 (SHA-256 hex). The DB check constraint `chk_logo_complete` enforces all-three-null-or-all-three-set.
7. **`Invitation` uses backing-field strategy** (`private Guid _id; public InvitationId Id => new(_id);`) mirroring slice-8 `Team`. EF config uses `b.Property<Guid>("_id").HasColumnName("id"); b.HasKey("_id");`. Future invitation handlers should be aware: `inv.Id` is a record-struct over `_id`, not a settable property.
8. **`Invitation.Email` is normalized at construction** (`Trim().ToLowerInvariant()`). Reading queries don't need to re-normalize.
9. **`Invitation.MarkExpired` does NOT set an `ExpiredAt` timestamp** — only `Status = Expired`. Per spec. If C-phase or D-phase tests expect a timestamp, they're wrong; the design omits it intentionally.
10. **`User.Id` does not call `.ValueGeneratedNever()`** (EF defaults to `ValueGeneratedOnAdd` for Guid PKs). Works correctly because `UserProjectionUpdater` (C3) assigns `Id = userId` from JWT `sub` before adding — non-default Guid means EF respects the assignment. If a future caller forgets to set `Id`, EF generates a random one and the projection drifts from KeyCloak. **C3 implementer: be explicit about assigning Id from the JWT `sub` claim.**

**Next task: C1 (7 new permission constants + role map + 28-cell matrix test).** Plan file `docs/superpowers/plans/2026-05-27-slice-9-organization-people-management-plan.md` §"Task C1: New permission constants + role map update" has the verbatim text.

**Workflow:** Use `superpowers:subagent-driven-development`. One implementer subagent per task. Full two-stage review (spec compliance + code quality) for tasks with behavior/tests/handlers/migrations. For pure-declaration tasks (interfaces, DTO records, enum types, ADR docs) self-verify by reading the committed file.

**Conventions worth re-loading from CLAUDE.md before dispatching:**

- **Central Package Management** — versions in `Directory.Packages.props`, not in csproj `Version=`. Phase A+B added: `Duende.IdentityModel 8.1.0`, `Microsoft.Extensions.Http 10.0.0`, `Microsoft.Extensions.Options.ConfigurationExtensions 10.0.0`, `Microsoft.Extensions.Hosting 10.0.0`, `Microsoft.Extensions.TimeProvider.Testing 10.5.0`, `Microsoft.Extensions.DependencyInjection 10.0.2`, `NSubstitute 5.3.0`, `Testcontainers* 4.0.0`. `System.Net.Http.Json` is **NOT** explicitly referenced (`net10.0` shared framework; NU1510 under `TreatWarningsAsErrors`).
- Solution: `Kartova.slnx` (XML). New csprojs via `dotnet sln Kartova.slnx add <path>`.
- `TreatWarningsAsErrors=true` everywhere; zero warnings.
- `[ExcludeFromCodeCoverage]` on Contracts assemblies + `*Dto`/`*Request`/`*Response` types + design-time factories + `IModule` composition classes (enforced by `ContractsCoverageRules`). NOT on interfaces/exceptions/aggregates/value types/enums.
- DI extensions live in `Add<Subject>Extensions.cs` (per `AddModuleDbContextExtensions.cs` convention).
- Internal types tested directly via `<InternalsVisibleTo Include="…Tests" />` on the SUT csproj.
- Test csproj idiom: `Microsoft.NET.Sdk` + explicit MSTest 4.x packages + `coverlet.collector` + `Microsoft.NET.Test.Sdk` (NOT `MSTest.Sdk/3.10.0` — plan templates lean on that, repo doesn't).
- Test files import `Microsoft.VisualStudio.TestTools.UnitTesting` **explicitly** per file (no GlobalUsings.cs in `Kartova.Organization.Tests`).
- Organization module's domain test project is `src/Modules/Organization/Kartova.Organization.Tests/` (namespace `Kartova.Organization.Tests`) — not `tests/`.
- Organization module's EF config filename convention is `*EntityTypeConfiguration.cs`.
- Windows PowerShell preferred for `dotnet`/`docker` commands; Git Bash lacks `grep -P` (use `-E` or `Select-String`).
- Use `roslyn-codelens` MCP for impact analysis before extending hot methods or renaming widely-used members.

**Docker availability:** As of this session, Docker daemon is running and the local `romangig2-postgres-1` / `romangig2-keycloak-1` / `romangig2-keycloak-db-1` / `romangig2-api-1` containers are all healthy. Phase C tasks don't need Docker (pure C# + tests). Phase D's integration tests will need it (Testcontainers). Phase H's DoD verifications need it (docker compose HTTP).

**Remaining task ledger** (mark each `[x]` as you ship):

### Phase A — Shared infrastructure foundation ✅ COMPLETE
- [x] A1: Scaffold Identity csproj + UserDisplayInfo (`9d95dd5`)
- [x] A2: IUserDirectory interface (`7127ee5`)
- [x] A3: IKeycloakAdminClient + DTOs + exception (`230038e`)
- [x] A4: KeycloakAdminClient impl + DI helper + 5 unit tests (`e86f06b` + `ba48a76`)
- [x] A5: IDistributedLock interface (`acd2a00`)
- [x] A6: PostgresAdvisoryLock impl + DI (`c8e1164`)
- [x] A7: LeaderElectedPeriodicService base + 3 unit tests (`688d8f9` + `615e353`)
- [x] A8: PostgresAdvisoryLock integration tests — **verified PASS** (3/3) against docker postgres:18-alpine (`7b25943`)
- [x] A9: ADR-0099 distributed locking (`bc89251`)

### Phase B — Database + domain ✅ COMPLETE
- [x] B1: Organization aggregate ext (Description, OrgLogo, DefaultTimeZone) + Name→DisplayName rename (`d11b641` + `0957344`)
- [x] B2: Invitation aggregate + 9 domain tests + `KartovaRoles.All` (`1851158`)
- [x] B3: User projection POCO (`0ba570d`)
- [x] B4: EF migrations + EF configs — **migrator smoke-test verified PASS** end-to-end on local docker postgres (`fde7230` + `cc52d5a`)

### Phase C — JWT-claim sync + permissions ← **START HERE**
- [ ] C1: 7 new permission constants + role map + 28-cell matrix test
- [ ] C2: Extend ICurrentUser with `JustAcceptedInvitationId`
- [ ] C3: UserProjectionUpdater + 3 unit tests
- [ ] C4: IPostAuthSyncHook + OrganizationPostAuthSyncHook wiring into TenantClaimsTransformation

### Phase D — Backend endpoints
- [ ] D1: OrganizationUserDirectory impl + DI + 4 unit tests
- [ ] D2: Org profile DTOs + GET/PUT /me endpoints
- [ ] D3: SVG sanitization + magic-byte helper + 7 unit tests
- [ ] D4: Logo upload/delete/serve endpoints with ETag/304
- [ ] D5: Invitation handlers + endpoints (create/list/revoke) + 5 unit tests
- [ ] D6: User search + detail endpoints
- [ ] D7: Session bootstrap endpoint POST /api/v1/auth/session
- [ ] D8: ExpireInvitationsHostedService + 2 unit tests
- [ ] D9: Program.cs wiring + appsettings + KeyCloak realm config

### Phase E — Catalog/Team integration
- [ ] E1: ApplicationResponse.Owner enrichment via IUserDirectory
- [ ] E2: `?ownerUserId=` filter on /catalog/applications + 422 validation
- [ ] E3: TeamMemberResponse display info enrichment

### Phase F — SPA
- [ ] F1: permissions.snapshot.json (may already be done — verify; C1 also touches this)
- [ ] F2: organization API hooks
- [ ] F3: OrganizationSettingsPage + LogoUploader + zod schema
- [ ] F4: invitations API hooks + InvitationsPage + InviteUserDialog + CopyInviteLinkBox
- [ ] F5: users API hooks + UserDetailPage + UserSearchCombobox + OwnerLink
- [ ] F6: auth/session API + OidcCallbackHandler + WelcomePage
- [ ] F7: Router + Sidebar + Header updates
- [ ] F8: AddMemberDialog upgrade + Application table OwnerLink

### Phase G — ADR-0100
- [ ] G1: Write ADR-0100 (strict one-email-per-tenant)

### Phase H — Verification + DoD
- [ ] H1: Integration tests for Phase D endpoints (16 scenarios)
- [ ] H2: Architecture tests for slice-9 boundaries
- [ ] H3: docker compose HTTP verification (happy + negative paths captured) — A8 + B4 smoke already covered the DB layer; D-phase HTTP paths land here
- [ ] H4: SPA E2E via Playwright MCP with screenshots
- [ ] H5: /simplify, /misc:mutation-sentinel, /misc:test-generator, /superpowers:requesting-code-review, /pr-review-toolkit:review-pr, /deep-review
- [ ] H6: Update CHECKLIST.md + push + open PR

**Please start by:**
1. Invoking `superpowers:subagent-driven-development` (the skill).
2. Reading the plan at the path above to lift the verbatim Task C1 text.
3. Reading `src/Kartova.SharedKernel/Multitenancy/KartovaPermissions.cs` to confirm the slice-7 baseline shape of the constants + the data-driven `All` collection (slice 7 made it reflection-driven; new constants picked up automatically).
4. Also reading `src/Kartova.SharedKernel/Multitenancy/KartovaRolePermissions.cs` to confirm the existing map shape (you've already extended `KartovaRoles` in B2; the map structure uses `FrozenDictionary<string, FrozenSet<string>>` keyed on role name).
5. Checking whether `tests/Kartova.ArchitectureTests/` has a `Ts_snapshot_equals_csharp_KartovaPermissions_All` test that needs updating in lockstep with `web/src/shared/auth/permissions.snapshot.json` (C1 Step 4 mentions this; the implementer needs both edits to land in the same commit or the drift sentinel fails).
6. Dispatching the C1 implementer with the verbatim task text + the conventions block above + the slice-7 baseline shape as scene-setting context.

Continue until you hit a natural checkpoint. End of Phase C (after C4 — `OrganizationPostAuthSyncHook` wiring into `TenantClaimsTransformation`) is the obvious next stop. C4 is the most architecturally novel task in Phase C (it touches `SharedKernel.AspNetCore` middleware infrastructure that other modules will eventually consume), so it deserves attentive scene-setting. At your stopping point, update this resume file's progress ledger and write a new resume prompt for the next session.

---

## Notes for future sessions

- **Each fresh session can realistically complete ~7-10 tasks** before context budget gets tight. Plan A (9 tasks) + reconciliations ran in one session; Plan B (4 tasks but B4 was complex) also ran in one session with one reconciliation commit. Plan C is 4 tasks of varying complexity; doable in one session.
- **Update this file at every checkpoint** so the next session sees the latest progress.
- **Spec/plan reconciliations** should always commit on the feature branch with message prefix `docs(slice-9):` for spec/plan docs, or `fix(slice-9):` for infrastructure/code fixes. See `cc52d5a` (init.sql privilege) and `06b215e` (Duende namespace).
- **If a subagent surfaces a real architectural question**, the controller (you) makes the call — do not push the question down into another subagent.
- **If `git show <SHA>` makes a deviation visible during review, fix it in a new commit** — never amend or rewrite (clean linear history preserved for `requesting-code-review` at slice boundary).
- **CLAUDE.md DoD #5 requires Docker happy + negative HTTP paths captured** for HTTP/auth/DB/middleware slices. This slice is all three. H3 (Phase H) is non-negotiable. A8 + B4 smoke-tests already covered the DB layer; D-phase endpoints + happy-path JWT round-trip land in H3.
- **Mutation testing target 80%** per `stryker-config.json`. H5's mutation-sentinel + test-generator loop is non-optional. B2 + B4 added boundary tests + ParamName assertions preemptively to kill obvious mutants; B1's fix-up added defensive-copy + boundary tests for the same reason. C-phase tasks should continue this pattern (boundary tests + ParamName assertions on every new validator).
- **Phase A's `KeycloakAdminClient` doesn't have an integration test against a real KeyCloak container yet** — the spec §11.3 mentions `Invitation_create_persists_keycloak_user_and_db_row` etc., but those land in Phase H (H1) alongside the endpoints in Phase D. Don't accidentally schedule them earlier.
- **`OrganizationDbContextModelSnapshot.cs` is now sensitive** — adding new Domain entities in C/D will regenerate it. The B4 snapshot diff was bounded to the explicit B4 additions; any unexplained snapshot churn in subsequent migrations should be investigated before committing.
- **C-phase Nits carried forward:** (1) consider adding `.ValueGeneratedNever()` on `User.Id` and `Invitation._id` when C3 lands (defensive). (2) Document the `WHERE status = 1` filter in `AddInvitationsTable` with a SQL-side comment (`/* InvitationStatus.Pending */`) when the next migration touches that file — not worth a standalone commit.
