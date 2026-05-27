# Slice 9 — Resume Execution Prompt

Paste the prompt below into a fresh `/clear`'d Claude Code session at the project root (`C:\Projects\Private\Roman.Gig2`).

---

## Prompt to paste

I'm continuing execution of slice 9 (organization & people management). Context:

**Spec:** `docs/superpowers/specs/2026-05-27-slice-9-organization-people-management-design.md`
**Plan:** `docs/superpowers/plans/2026-05-27-slice-9-organization-people-management-plan.md`
**Branch:** `feat/slice-9-organization-people-management` (already checked out)

**Status: Phase A complete (13 commits since branch start).** Full-solution build: 0 warnings, 0 errors. Unit + architecture tests: 379/379 passing (5 + 91 + 40 + 3 + 3 + 72 + 103 + 62 across the SharedKernel.Identity / SharedKernel.AspNetCore / Organization / Catalog.Infrastructure / Organization.Infrastructure / Catalog / SharedKernel / ArchitectureTests suites). One pending verification: Phase A8 (`PostgresAdvisoryLock` Testcontainers tests) is implementation-complete but tests not yet executed — Docker was not available on the controller's host. **Action for next session if Docker is available locally: `dotnet test tests/Kartova.SharedKernel.Postgres.IntegrationTests/` should pass 3/3 against `postgres:18-alpine`.** If still no Docker, defer to slice-final DoD gate H3.

**Phase A commit log:**

| Commit | Task | Notes |
|---|---|---|
| `9d95dd5` | A1: Scaffold `Kartova.SharedKernel.Identity` csproj + `UserDisplayInfo` | Full two-stage review |
| `abbc169` | A1 doc reconciliation | Initial spec/plan moved from fabricated `IdentityModel 12.0.0` to `Duende.IdentityModel 8.1.0`. Reconciliation note about the namespace turned out to be wrong — see `06b215e` for the correction. |
| `7127ee5` | A2: `IUserDirectory` interface | Self-verified (pure declaration) |
| `cc91a63` | resume prompt v1 | (now obsolete — this file is v2) |
| `230038e` | A3: `IKeycloakAdminClient` + DTOs + `KeycloakAdminException` | Self-verified (pure declaration) |
| `e86f06b` | A4: `KeycloakAdminClient` + DI + 5 unit tests | Full two-stage review |
| `06b215e` | docs: TokenClient namespace correction | A4 surfaced that the legacy `IdentityModel.Client` namespace was NOT preserved across the Duende rebrand — actual is `Duende.IdentityModel.Client`. Spec + plan + this resume-prompt all corrected. |
| `ba48a76` | A4 fix: STJ casing + safe Guid parse | Code-quality review caught a latent runtime bug: `JsonContent.Create<T>` + `ReadFromJsonAsync<T>` use STJ defaults (PascalCase, case-sensitive) but KeyCloak's REST API is camelCase. Now uses `JsonSerializerOptions(JsonSerializerDefaults.Web)` consistently. Also wrapped `Guid.Parse` on Location header in TryParse + typed exception. |
| `acd2a00` | A5: `IDistributedLock` interface | Self-verified (pure declaration) |
| `c8e1164` | A6: `PostgresAdvisoryLock` + `AddPostgresDistributedLocks` DI | Full two-stage review |
| `688d8f9` | A7: `LeaderElectedPeriodicService` base + 3 unit tests | Full two-stage review |
| `615e353` | A7 fix: test hygiene | `await StartAsync` instead of discarded task; `await using` ServiceProvider. |
| `7b25943` | A8: `PostgresAdvisoryLock` integration tests | Spec-compliant; tests pending Docker availability. |
| `bc89251` | A9: ADR-0099 + README index update | ADR count now 99. Platform Infrastructure category extended. |

**Reconciliations made during Phase A (worth remembering):**

1. **Duende.IdentityModel namespace.** The legacy `IdentityModel` package was renamed to `Duende.IdentityModel` after v7, AND the namespace also moved — `TokenClient` now lives in `Duende.IdentityModel.Client`, not the legacy `IdentityModel.Client`. The A1 reconciliation note claimed the legacy namespace was preserved; A4 implementation surfaced the resolution failure; spec + plan + this file are now all consistent.
2. **DI extension file naming.** Plan templates used generic `ServiceCollectionExtensions.cs`; the repo convention (per `AddModuleDbContextExtensions.cs`) is one extension per file, named `Add<Subject>Extensions.cs`. Applied to A4 and A6; this is the established pattern for subsequent slices.
3. **JSON serialization defaults for KeyCloak interop.** `JsonContent.Create<T>` and `ReadFromJsonAsync<T>` default to PascalCase + case-sensitive — incompatible with KeyCloak's camelCase REST API. Use `new JsonSerializerOptions(JsonSerializerDefaults.Web)` and pass it to every call site. The `KeycloakAdminClient` already does this; future KeyCloak-adjacent code should too.
4. **MSTest.Sdk vs Microsoft.NET.Sdk.** Plan templates used `MSTest.Sdk/3.10.0`. Repo convention (per `tests/Kartova.SharedKernel.Tests/Kartova.SharedKernel.Tests.csproj`) is `Microsoft.NET.Sdk` + explicit MSTest 4.x packages + `coverlet.collector` + `Microsoft.NET.Test.Sdk`. No `EnableMSTestRunner`. Apply this idiom to every new test project.
5. **`InternalsVisibleTo` for testing `internal sealed` classes.** Both `KeycloakAdminClient` (A4) and `PostgresAdvisoryLock` (A8) are `internal sealed`; the test projects add `<InternalsVisibleTo Include="…Tests" />` on the SUT csproj. The plan templates didn't mention this — controller-level pragmatic addition documented per task.

**Next task: B1 (`Organization` aggregate ext — Description, OrgLogo, DefaultTimeZone).** See plan §Phase B Task B1 for the verbatim spec. Note: this is **the first task that touches existing domain code** — read `src/Modules/Organization/Kartova.Organization.Domain/Organization.cs` before lifting the task text to confirm the slice-2 baseline shape, then dispatch the implementer.

**Workflow:** Use `superpowers:subagent-driven-development`. One implementer subagent per task. Full two-stage review (spec compliance + code quality) for tasks with behavior/tests/handlers/migrations. For pure-declaration tasks (interfaces, DTO records, enum types, ADR docs) self-verify by reading the committed file is acceptable — controller-level pragmatic deviation on grounds that there's no behavior beyond spec compliance.

**Conventions worth re-loading from CLAUDE.md before dispatching:**

- **Central Package Management** — package versions live in `Directory.Packages.props`, not in csproj `Version=` attributes. Already-pinned (verified present after Phase A): `Duende.IdentityModel 8.1.0`, `Microsoft.Extensions.Http 10.0.0`, `Microsoft.Extensions.Options.ConfigurationExtensions 10.0.0`, `Microsoft.Extensions.Hosting 10.0.0`, `Microsoft.Extensions.TimeProvider.Testing 10.5.0`, `Microsoft.Extensions.DependencyInjection 10.0.2`, `NSubstitute 5.3.0`, `Testcontainers* 4.0.0`. Do **NOT** explicitly reference `System.Net.Http.Json` — `net10.0` shared framework, triggers NU1510 under `TreatWarningsAsErrors`.
- Solution file is `Kartova.slnx` (XML), not `.sln`. Register new csprojs with `dotnet sln Kartova.slnx add <path>`.
- `TreatWarningsAsErrors=true` everywhere; zero warnings.
- `[ExcludeFromCodeCoverage]` on Contracts assemblies + `*Dto`/`*Request`/`*Response` types + design-time factories + `IModule` composition classes (enforced by `ContractsCoverageRules` arch test). Not on interfaces or exception classes. Apply to record types in `Kartova.SharedKernel.Identity` by repo habit even though the rule doesn't fire there.
- New DI extensions live in their own file: `Add<Subject>Extensions.cs` (e.g. `AddPostgresDistributedLocksExtensions.cs`).
- Internal types tested directly via `<InternalsVisibleTo Include="…Tests" />` in the SUT csproj.
- Test csproj idiom: `Microsoft.NET.Sdk` + explicit MSTest 4.x packages + global `<Using Include="Microsoft.VisualStudio.TestTools.UnitTesting" />` for the integration-test projects.
- Windows PowerShell preferred; Git Bash lacks `grep -P`.
- Use the `roslyn-codelens` MCP for navigation/impact analysis on existing C# code before extending domain methods (the Organization aggregate is about to grow — `find_callers` / `find_references` on existing members matters here).

**Remaining task ledger** (mark each `[x]` as you ship):

### Phase A — Shared infrastructure foundation ✅ COMPLETE
- [x] A1: Scaffold Identity csproj + UserDisplayInfo (`9d95dd5`)
- [x] A2: IUserDirectory interface (`7127ee5`)
- [x] A3: IKeycloakAdminClient + DTOs + exception (`230038e`)
- [x] A4: KeycloakAdminClient impl + DI helper + 5 unit tests (`e86f06b` + `ba48a76`)
- [x] A5: IDistributedLock interface (`acd2a00`)
- [x] A6: PostgresAdvisoryLock impl + DI (`c8e1164`)
- [x] A7: LeaderElectedPeriodicService base + 3 unit tests (`688d8f9` + `615e353`)
- [x] A8: PostgresAdvisoryLock integration tests — staged, tests **pending Docker verification** (`7b25943`)
- [x] A9: ADR-0099 distributed locking (`bc89251`)

### Phase B — Database + domain
- [ ] B1: Organization aggregate ext (Description, OrgLogo, DefaultTimeZone) ← **START HERE**
- [ ] B2: Invitation aggregate + 9 domain tests
- [ ] B3: User projection POCO
- [ ] B4: EF migrations (pg_trgm, org profile cols, users, invitations) + EF configs

### Phase C — JWT-claim sync + permissions
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
- [ ] F1: permissions.snapshot.json (may already be done — verify)
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
- [ ] H1: Integration tests for Phase D endpoints (16 scenarios) — also re-run A8 Testcontainers tests here if Docker wasn't available earlier
- [ ] H2: Architecture tests for slice-9 boundaries
- [ ] H3: docker compose HTTP verification (happy + negative paths captured)
- [ ] H4: SPA E2E via Playwright MCP with screenshots
- [ ] H5: /simplify, /misc:mutation-sentinel, /misc:test-generator, /superpowers:requesting-code-review, /pr-review-toolkit:review-pr, /deep-review
- [ ] H6: Update CHECKLIST.md + push + open PR

**Please start by:**
1. Invoking `superpowers:subagent-driven-development` (the skill).
2. Reading the plan at the path above to lift the verbatim Task B1 text.
3. Reading `src/Modules/Organization/Kartova.Organization.Domain/Organization.cs` to confirm the slice-2 baseline shape before dispatching.
4. Dispatching the B1 implementer with the verbatim task text + the conventions block above + the baseline shape as scene-setting context.

Continue until you hit a natural checkpoint. End of Phase B (B4 — EF migrations) is the obvious next stop, but Phase B + C together (8 more tasks) is also feasible in one session. At your stopping point, update this resume file's progress ledger and write a new resume prompt for the next session.

---

## Notes for future sessions

- **Each fresh session can realistically complete ~7-10 tasks** before context budget gets tight (Phase A's 7 implementer tasks + 4 reconciliation/fix subagents + 2 review tasks each ran in ~one session). Plan accordingly.
- **Update this file at every checkpoint** so the next session sees the latest progress.
- **Spec/plan reconciliations** should always commit on the feature branch with message prefix `docs(slice-9):` — see `06b215e` (Duende namespace) and `abbc169` (Duende package rename).
- **If a subagent surfaces a real architectural question**, the controller (you) makes the call — do not push the question down into another subagent.
- **If `git show <SHA>` makes a deviation visible during review, fix it in a new commit** — never amend or rewrite (we have a clean linear history we want to preserve for `requesting-code-review` at slice boundary).
- **CLAUDE.md DoD #5 requires Docker happy + negative HTTP paths captured** for HTTP/auth/DB/middleware slices. This slice is all three. Do not skip H3. A8's integration tests should be re-verified at H1 / H3 anyway.
- **Mutation testing target 80%** per `stryker-config.json`. H5's mutation-sentinel + test-generator loop is non-optional.
- **Phase A's `KeycloakAdminClient` doesn't have an integration test against a real KeyCloak container yet** — the spec §11.3 mentions `Invitation_create_persists_keycloak_user_and_db_row` etc., but those land in Phase H (H1) alongside the endpoints in Phase D. Don't accidentally schedule them earlier.
