# Slice 9 — Resume Execution Prompt

Paste the prompt below into a fresh `/clear`'d Claude Code session at the project root (`C:\Projects\Private\Roman.Gig2`).

---

## Prompt to paste

I'm continuing execution of slice 9 (organization & people management). Context:

**Spec:** `docs/superpowers/specs/2026-05-27-slice-9-organization-people-management-design.md`
**Plan:** `docs/superpowers/plans/2026-05-27-slice-9-organization-people-management-plan.md`
**Branch:** `feat/slice-9-organization-people-management` (already checked out)

**Progress so far (3 commits on the feature branch):**

| Commit | Task | Notes |
|---|---|---|
| `9d95dd5` | A1: Scaffold `Kartova.SharedKernel.Identity` csproj + `UserDisplayInfo` in base SharedKernel | Full two-stage review passed |
| `abbc169` | A1 doc reconciliation | Spec + plan updated to use `Duende.IdentityModel 8.1.0` (the original `IdentityModel 12.0.0` was a fabricated version — legacy package was rebranded to Duende after v7; `IdentityModel.Client.TokenClient` namespace preserved) |
| `7127ee5` | A2: `IUserDirectory` interface | Self-verified (pure declaration file, no behavior) |

**Next task: A3 (`IKeycloakAdminClient` + DTOs + `KeycloakAdminException`).** See plan §Phase A Task A3 for the verbatim spec — 3 new files in `src/Kartova.SharedKernel.Identity/`.

**Workflow:** Use `superpowers:subagent-driven-development`. One implementer subagent per task; full two-stage review (spec compliance + code quality) for tasks with behavior/tests/handlers/migrations. For pure-declaration tasks (interfaces, DTO records, enum types) self-verify by reading the committed file is acceptable — documented as a controller-level pragmatic deviation on grounds that there's no behavior to review beyond spec compliance.

**Conventions worth re-loading from CLAUDE.md before dispatching:**

- Repo uses **Central Package Management** — package versions live in `Directory.Packages.props`, not in csproj `Version=` attributes. Two packages already added there during A1: `Duende.IdentityModel 8.1.0`, `Microsoft.Extensions.Http 10.0.0`, `Microsoft.Extensions.Options.ConfigurationExtensions 10.0.0`. Do **NOT** explicitly reference `System.Net.Http.Json` — it's part of the `net10.0` shared framework and triggers NU1510 under `TreatWarningsAsErrors`.
- Solution file is `Kartova.slnx` (XML), not `.sln`.
- `TreatWarningsAsErrors=true` everywhere; zero warnings.
- `[ExcludeFromCodeCoverage]` on pure data carriers in Contracts assemblies (enforced by `ContractsCoverageRules` arch test). For DTOs in `Kartova.SharedKernel.Identity` (not a `*.Contracts` assembly), the rule doesn't fire — but apply the attribute anyway for data records (it's pre-existing project habit).
- `[ExcludeFromCodeCoverage]` does **not** apply to interfaces or exception classes.
- Windows PowerShell preferred for shell commands; Git Bash lacks `grep -P`.

**Remaining task ledger** (mark each `[x]` as you ship):

### Phase A — Shared infrastructure foundation
- [x] A1: Scaffold Identity csproj + UserDisplayInfo
- [x] A2: IUserDirectory interface
- [ ] A3: IKeycloakAdminClient + DTOs + exception
- [ ] A4: KeycloakAdminClient impl + DI helper + 4 unit tests
- [ ] A5: IDistributedLock interface
- [ ] A6: PostgresAdvisoryLock impl + DI
- [ ] A7: LeaderElectedPeriodicService base + 3 unit tests
- [ ] A8: PostgresAdvisoryLock integration tests
- [ ] A9: ADR-0099 distributed locking

### Phase B — Database + domain
- [ ] B1: Organization aggregate ext (Description, OrgLogo, DefaultTimeZone)
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
- [ ] H1: Integration tests for Phase D endpoints (16 scenarios)
- [ ] H2: Architecture tests for slice-9 boundaries
- [ ] H3: docker compose HTTP verification (happy + negative paths captured)
- [ ] H4: SPA E2E via Playwright MCP with screenshots
- [ ] H5: /simplify, /misc:mutation-sentinel, /misc:test-generator, /superpowers:requesting-code-review, /pr-review-toolkit:review-pr, /deep-review
- [ ] H6: Update CHECKLIST.md + push + open PR

**Please start by:**
1. Invoking `superpowers:subagent-driven-development` (the skill).
2. Reading the plan at the path above to lift the verbatim Task A3 text.
3. Dispatching the A3 implementer with the verbatim task text + the conventions block above as scene-setting context.

Continue until you hit a natural checkpoint (end of Phase A is a good one — that's `9d95dd5..A9` = 7 more tasks). At that checkpoint, update this resume file's progress ledger and write a new resume prompt for the next session.

---

## Notes for future sessions

- **Each fresh session can realistically complete ~10-15 tasks** before context budget gets tight. Plan accordingly.
- **Update this file at every checkpoint** so the next session sees the latest progress.
- **Spec/plan reconciliations** (like A1's Duende rename) should always commit on the feature branch with message prefix `docs(slice-9):`.
- **If a subagent surfaces a real architectural question**, the controller (you) makes the call — do not push the question down into another subagent.
- **CLAUDE.md DoD #5 requires Docker happy + negative HTTP paths captured** for HTTP/auth/DB/middleware slices. This slice is all three. Do not skip H3.
- **Mutation testing target 80%** per `stryker-config.json`. H5's mutation-sentinel + test-generator loop is non-optional.
