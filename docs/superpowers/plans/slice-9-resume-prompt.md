# Slice 9 — Resume Execution Prompt

Paste the prompt below into a fresh `/clear`'d Claude Code session at the project root (`C:\Projects\Private\Roman.Gig2`).

---

## Prompt to paste

I'm continuing execution of slice 9 (organization & people management). Context:

**Spec:** `docs/superpowers/specs/2026-05-27-slice-9-organization-people-management-design.md`
**Plan:** `docs/superpowers/plans/2026-05-27-slice-9-organization-people-management-plan.md`
**Branch:** `feat/slice-9-organization-people-management` (already checked out)

**Status: Phases A + B + C + D1–D4 complete (36 commits since branch start).** Full-solution build: 0 warnings, 0 errors. Full unit + architecture pass: green across all suites (Kartova.Organization.Tests 77 + Kartova.Organization.Infrastructure.Tests 28 + Kartova.SharedKernel.Identity.Tests 5 + Kartova.SharedKernel.AspNetCore.Tests 95 + Kartova.Catalog.Infrastructure.Tests 3 + Kartova.Catalog.Tests 72 + Kartova.SharedKernel.Tests 106 + Kartova.ArchitectureTests 63 = 449/449 unit + arch passing). Phase A's Docker-dependent A8 PostgresAdvisoryLock Testcontainers integration tests + Phase B's B4 migrator smoke-test against local docker-compose postgres were both verified at end-of-Phase-B and **still hold** — no DB-layer changes in C or D1–D4. D1–D4 is pure C#/EF code + tests, no Docker dependency.

**Phase D commit log (8 new commits, all green at HEAD):**

| Commit | Task | Notes |
|---|---|---|
| `675a455` | D1: `OrganizationUserDirectory` + DI + 4 tests | Full two-stage review. **Plan placed `OrganizationUserDirectoryTests.cs` at `tests/Kartova.Organization.Infrastructure.Tests/` — that path does not exist; correct path is `src/Modules/Organization/Kartova.Organization.Infrastructure.Tests/`.** Implementation is pure local-projection lookup (`GetAsync` + `GetManyAsync`) — NO Keycloak coupling (resume-prompt over-stated D1's scope; KeyCloak search is part of D6). Added `<ProjectReference>` to `Kartova.SharedKernel.Identity.csproj` from Infrastructure csproj. |
| `4c58ac0` | D1 follow-up: test strengthening | Code-quality reviewer flagged 2 mutation-survivor gaps: `GetManyAsync_returns_only_matched_ids` only asserted count + ContainsKey (didn't assert content), and `GetManyAsync_returns_empty_for_empty_input` couldn't distinguish short-circuit from empty DB. Fixed with content assertions on returned `DisplayName`/`Email` and by seeding a noise user before the empty-input call. |
| `7e30b92` | D2: Org profile DTOs + GET/PUT /me + 6 tests | Full two-stage review. **Major plan-vs-reality finding: GET /me already existed** (slice-2/8 `OrganizationDto(Id, TenantId, Name, CreatedAt)`). Replaced the existing delegate with `OrgProfileQueries.GetMyOrgAsync` returning the new `OrgProfileResponse(Id, DisplayName, Description, DefaultTimeZone, LogoEtag, LogoMimeType, CreatedAt)`. Old `OrganizationDto` stays for the Admin path (`POST /admin/organizations`). `IOrganizationQueries` is now dead-code on the /me path — cleanup deferred. **Plan placed `OrgProfileQueries` + `UpdateOrgProfileHandler` in Application — Clean-Arch violation** (Application has NO ref to Infrastructure where DbContext lives). Moved both to Infrastructure (same as C3 + slice-8 handlers). Added `.RequireAuthorization(KartovaPermissions.OrgProfileRead)` to existing GET /me. Two integration tests broke on the schema swap (`OrganizationEndpointHappyPathTests.cs` + `TenantIsolationTests.cs` — both read `OrganizationDto.Name`); updated to read `OrgProfileResponse.DisplayName`. Used full RFC 7807 envelopes (title + detail) on 404/412, not the plan's bare `Results.NotFound()` / `Results.Problem(type:, statusCode: 412)`. |
| `50d8d65` | D2 follow-up: If-Match parsing deferred + persistence test strengthened | Code-quality review flagged that the half-implemented manual `Convert.FromHexString` parsing in `UpdateMeAsync` (a) silently swallowed `FormatException` (200 on malformed header), and (b) duplicated the existing `IfMatchEndpointFilter` in `SharedKernel.AspNetCore` that Catalog already uses. **Resolution: removed manual parsing entirely** — Organization aggregate has no concurrency token, so If-Match is currently no-op. Added comment naming the future `IfMatchEndpointFilter` adoption when xmin mapping lands. Handler signature still accepts `byte[]? ifMatch` (forward-compat) with `IDE0060` suppression. Also strengthened `HandleAsync_updates_aggregate_when_valid` to use three DbContext instances (seed/act/assert) sharing the same `DbContextOptions` so a "delete `SaveChangesAsync`" mutator surfaces — this becomes a recurring pattern for write-path tests. |
| `95c0435` | D3: `LogoValidation` static class + 10 tests | Full two-stage review. **HtmlSanitizer plan version 9.0.886 has a known XSS advisory (GHSA-j92c-7v7g-gj3f)** that fails NU1902 under `TreatWarningsAsErrors=true`. Bumped pin to 9.0.892 in `Directory.Packages.props` (CPM); csproj uses versionless reference. Tests landed in `Kartova.Organization.Tests` (existing domain-tier test project — per resume-prompt rule "Do NOT scaffold new test projects for slice 9"). The plan's `tests/Kartova.Organization.Application.Tests/` directory does not exist. Used `static readonly byte[] PngMagic` for allocation-free hot-path SequenceEqual. Added 3 boundary mutation-killers beyond plan's 7 (exact-8-byte PNG, 7-byte PNG, unsupported-mime with valid PNG bytes). Also moved `Duende.IdentityModel` to alphabetical position in `Directory.Packages.props` (same version, cosmetic only). |
| `adda5b6` | D3 follow-up: tightened sanitizer + perf + named const | Code-quality reviewer flagged: (1) `"style"` attribute on the allow-list inherited Ganss's broad default `AllowedCssProperties` (hundreds of properties — defence-in-depth gap); (2) `IsSvgText` allocated full UTF-16 string of the entire payload just to check first ~5 chars (~512 KiB allocation for max-size SVG); (3) bare `0.20` literal not named. Fixes: (1) removed `"style"` from `AllowedAttributes` — SVG visual control is fully covered by explicit attributes (fill/stroke/opacity/transform); (2) replaced `IsSvgText` with allocation-free span scan using `"<?xml"u8`/`"<svg"u8` UTF-8 string literals + BOM strip + ASCII-case-insensitive prefix compare; (3) extracted `private const double MaterialChangeThreshold = 0.20;` with XML doc. Tightened test assertion from `Contains("script")` to `Contains("<script")` for tag-opener specificity. |
| `8d66ec5` | D4: PUT/DELETE/GET /me/logo + 9 tests | Full two-stage review. Many plan-vs-reality fixes: (1) `LogoCommands` in **Infrastructure** (not Application — same Clean-Arch issue as D2); (2) added `ProblemTypes.UnsupportedLogoMedia` constant (didn't exist); (3) typed `UploadLogoResponse(LogoEtag, MimeType)` DTO in Contracts with `[ExcludeFromCodeCoverage]` instead of plan's anonymous `new { logoEtag, mimeType }` (anonymous breaks OpenAPI codegen); (4) extracted endpoint logic to `OrganizationEndpointDelegates.UploadLogoAsync/DeleteLogoAsync/GetLogoAsync` (plan inlined lambdas — inconsistent with module pattern); (5) RFC 7807 envelopes on 404 + 413; (6) `MediaTypeHeaderValue.TryParse` + `.ToLowerInvariant()` mime parsing (plan was case-sensitive and didn't strip `; charset=...` parameters); (7) `LogoMaxBytes = 256*1024` constant with cross-reference comment to `OrgLogo.cs`; comparison is `> LogoMaxBytes` so 262144 exactly passes (matches OrgLogo invariant); (8) DoD #2 tests beyond plan: 9 unit tests including three persistence-path tests using the three-context pattern from D2. |
| `ecb1be0` | D4 follow-up: load-before-sanitize + EntityTag parsing + Cache-Control on 304 | Code-quality reviewer flagged 3 Important issues: (1) `UploadAsync` ran the Ganss sanitizer on attacker bytes BEFORE checking if the org row exists (defence-in-depth — wasted CPU + larger attack surface on misconfigured tenants); (2) `If-None-Match` used `.Trim('"')` which mishandled weak validators (`W/"hash"` → `W/"hash`, doesn't match) and accepted malformed input; (3) 304 response emitted only `ETag`, not `Cache-Control` — RFC 7232 §4.1 wants the freshness directive echoed so downstream caches can refresh. Fixes: reordered `UploadAsync` to magic-byte → load-org → 404 → sanitize → SetLogo → save; replaced `.Trim('"')` with `EntityTagHeaderValue.TryParse` + explicit `!etag.IsWeak` rejection; moved `ETag` + `Cache-Control` assignment BEFORE the 304-vs-200 branch so both responses set both headers. |

**Reconciliations made during D1–D4 (worth remembering for D5+ / E / F):**

1. **`OrganizationUserDirectory` lives in `Kartova.Organization.Infrastructure`** (registered scoped in `OrganizationModule.RegisterServices`). Cross-module port — Catalog/Team responses can DI-inject `IUserDirectory` for display-name enrichment (this is exactly what E1/E3 will consume). **Concrete contract:** `internal sealed class OrganizationUserDirectory(OrganizationDbContext db) : IUserDirectory` with two methods: `GetAsync(Guid, CancellationToken) → UserDisplayInfo?` and `GetManyAsync(IReadOnlyCollection<Guid>, CancellationToken) → IReadOnlyDictionary<Guid, UserDisplayInfo>`. Empty-input short-circuit on `GetManyAsync`. No manual tenant filtering — RLS handles it.

2. **Recurring Clean-Arch reconciliation: handlers/queries that take `OrganizationDbContext` go in `Kartova.Organization.Infrastructure`, NOT `Kartova.Organization.Application`.** `Application` does not (and cannot — circular ref) reference `Infrastructure`. Pattern established by slice-8 (`CreateTeamHandler` etc.), C3 (`UserProjectionUpdater`), D2 (`OrgProfileQueries`, `UpdateOrgProfileHandler`), D4 (`LogoCommands`). D5's `CreateInvitationHandler`/`RevokeInvitationHandler`/`ListInvitationsQuery` will hit this same issue — **always put DbContext-coupled handlers in Infrastructure**.

3. **GET /me schema swap:** the old `OrganizationDto(Id, TenantId, Name, CreatedAt)` is **only** used by the Admin path (`POST /api/v1/admin/organizations`) now. The /me path returns `OrgProfileResponse(Id, DisplayName, Description, DefaultTimeZone, LogoEtag, LogoMimeType, CreatedAt)`. `IOrganizationQueries.GetCurrentAsync` is dead code on the /me path; leave it for now (Phase H cleanup candidate). Slice-9 integration tests (`OrganizationEndpointHappyPathTests`, `TenantIsolationTests`) read `OrgProfileResponse.DisplayName`, not `OrganizationDto.Name` — any D-phase integration test that hits `/me` must use the new shape.

4. **GET /me now requires `KartovaPermissions.OrgProfileRead`; PUT /me requires `OrgProfileEdit`.** Existing OrgAdmin-token tests pass because the role grants both. Make sure any new D-phase tokens are issued with the right role.

5. **Concurrency token on Organization aggregate does NOT exist.** `Organization.cs` has no `RowVersion` byte[] or xmin mapping. D2's `UpdateMeAsync` has been simplified to not parse `If-Match` at all (manual parsing was a half-baked silent-swallow). The handler signature still accepts `byte[]? ifMatch` for forward-compat. When xmin mapping is added later (Phase H or follow-up slice), wire the existing `Kartova.SharedKernel.AspNetCore.IfMatchEndpointFilter` (already used by `CatalogModule`) onto PUT /me — DO NOT re-implement manual parsing.

6. **RFC 7807 envelope discipline:** all 4xx and 5xx responses in the Organization module use full `Results.Problem(type:, title:, detail:, statusCode:)` envelopes (never bare `Results.NotFound()` / `Results.StatusCode(413)`). Title + detail tailored to context. Consistent with sibling team endpoints + slice-8's `TeamNotFound()` helper. D5+ should continue this — every new endpoint's 404/409/412/422 path is an RFC 7807 envelope.

7. **Three-context persistence test pattern:** for any write-path test (handler that calls `SaveChangesAsync`), use three DbContext instances against a shared `DbContextOptions`:
   - `seedDb` — seed state, `SaveChangesAsync`, dispose
   - `actDb` — construct handler, invoke, dispose
   - `assertDb` — reload from store and assert
   
   This kills the "delete `SaveChangesAsync`" mutator that the single-context style misses. See `UpdateOrgProfileHandlerTests.HandleAsync_updates_aggregate_when_valid` (D2) and `LogoCommandsTests.UploadAsync_accepts_valid_png_and_returns_etag` (D4) for canonical examples.

8. **`Kartova.Organization.Tests` (domain test project) uses PER-FILE MSTest using; `Kartova.Organization.Infrastructure.Tests` uses csproj-level global Using.** Always check the destination csproj before deciding. D5+ tests of `CreateInvitationHandler` etc. (which take `OrganizationDbContext`) go in Infrastructure.Tests → no per-file using needed.

9. **HtmlSanitizer is pinned at 9.0.892 in `Directory.Packages.props`** (NOT 9.0.886 — that version has known XSS GHSA-j92c-7v7g-gj3f and fails NU1902 under TreatWarningsAsErrors=true). Any subsequent slice that touches HtmlSanitizer reference must keep ≥9.0.892.

10. **`ProblemTypes` constants added in slice 9:** `UnsupportedLogoMedia` (D4, used for 413/415/422 on logo endpoints). When D5 needs `EmailAlreadyInTenant` / `EmailAlreadyInvited` / `EmailAlreadyOnPlatform` etc., add them with a slice-comment block in `ProblemTypes.cs` like:
    ```csharp
    // Invitation conflicts — slice 9 (spec §6.7).
    public const string EmailAlreadyInTenant   = Base + "email-already-in-tenant";
    public const string EmailAlreadyInvited    = Base + "email-already-invited";
    public const string EmailAlreadyOnPlatform = Base + "email-already-on-platform";
    ```

11. **EntityTag/cache-validation parsing — use `Microsoft.Net.Http.Headers.EntityTagHeaderValue.TryParse`**, NOT manual `.Trim('"')`. Reject weak validators explicitly via `etag.IsWeak`. Emit both `ETag` and `Cache-Control` on 304 AND 200 paths. Pattern: `GetLogoAsync` in `OrganizationEndpointDelegates.cs`.

12. **`MediaTypeHeaderValue.TryParse(req.ContentType, out var mt)` + `mt.MediaType.Value?.ToLowerInvariant()`** is the correct mime parse — handles `image/PNG` and `image/png; charset=utf-8` both. Don't use raw `req.ContentType` equality checks.

13. **Endpoint delegate pattern in this module:** `internal static async Task<IResult>` methods on `OrganizationEndpointDelegates`, bound via method-group in `OrganizationModule.MapEndpoints` with full `.RequireAuthorization(...)` + `.WithName(...)` + `.Produces<T>(...)` + `.ProducesProblem(...)` chains. Inline lambdas are NOT the pattern — extract to a named delegate even for small handlers (DELETE bodies are 2 lines but still extracted).

14. **`UpdateOrgProfileResult` / `UploadLogoResult` co-located with their handler** in the same file. Co-location of tightly-coupled result records with their handler is the slice-9 convention (matches slice-8 `AddTeamMemberResult` precedent). Each result record type does NOT need `[ExcludeFromCodeCoverage]` because they're in Infrastructure, not Contracts.

15. **D-phase verification at HEAD (`ecb1be0`):** Organization.Tests 77 + Organization.Infrastructure.Tests 28 + ArchitectureTests 63 = 168 directly-touched; full slice 9 suite 449 unit+arch passing. Full-solution build 0 warnings / 0 errors with `TreatWarningsAsErrors=true`. **Lesson reinforced (from C-phase carry-forward):** after a subagent reports DONE on the focused test filter, also run the full module suite to catch sibling regressions.

**Next task: D5 (`CreateInvitationHandler` + `RevokeInvitationHandler` + `ListInvitationsQuery` + 3 endpoints + 5+ unit tests + DI).** Plan file `docs/superpowers/plans/2026-05-27-slice-9-organization-people-management-plan.md` §"Task D5: Invitation contracts + handlers + endpoints" starting at line ~2946 has the verbatim text. Per resume-prompt note, **D5 is the most complex D-task** — handler + 3 endpoints (POST/GET/DELETE) + NSubstitute-mocked Keycloak interactions + compensation logic for KC-user-creation failure + at least 5 unit tests. Budget ~50% of a fresh session for D5 alone. D7 (session bootstrap) is the second-most-complex; plan another big chunk for it.

**Workflow:** Use `superpowers:subagent-driven-development`. One implementer subagent per task. Full two-stage review (spec compliance + code quality) for every task. **After each implementer reports DONE on a focused-filter test pass, also run the full module test suite to catch sibling regressions** (D-phase consistently surfaced quality-review issues; expect at least one fix-up commit per task).

**Conventions worth re-loading from CLAUDE.md before dispatching:**

- **Central Package Management** — versions in `Directory.Packages.props`, not in csproj `Version=`. Phase A+B+C+D added: `Duende.IdentityModel 8.1.0`, `Microsoft.Extensions.Http 10.0.0`, `Microsoft.Extensions.Options.ConfigurationExtensions 10.0.0`, `Microsoft.Extensions.Hosting 10.0.0`, `Microsoft.Extensions.TimeProvider.Testing 10.5.0`, `Microsoft.Extensions.DependencyInjection 10.0.2`, `NSubstitute 5.3.0`, `Testcontainers* 4.0.0`, `Microsoft.EntityFrameworkCore.InMemory 10.0.7`, `HtmlSanitizer 9.0.892`. D5 may need to pin nothing new (NSubstitute 5.3.0 already pinned for Keycloak mocks).
- Solution: `Kartova.slnx` (XML). New csprojs via `dotnet sln Kartova.slnx add <path>`.
- `TreatWarningsAsErrors=true` everywhere; zero warnings.
- `[ExcludeFromCodeCoverage]` on Contracts assemblies + `*Dto`/`*Request`/`*Response` types + design-time factories + `IModule` composition classes (enforced by `ContractsCoverageRules`). NOT on interfaces/exceptions/aggregates/value types/enums/Infrastructure handlers/Infrastructure result records.
- DI extensions live in `Add<Subject>Extensions.cs` (per `AddModuleDbContextExtensions.cs` convention).
- Internal types tested directly via `<InternalsVisibleTo Include="…Tests" />` on the SUT csproj. **Organization.Infrastructure has this attribute** — internal types from D-phase work can stay internal.
- Test csproj idiom: `Microsoft.NET.Sdk` + explicit MSTest 4.x packages + `coverlet.collector` + `Microsoft.NET.Test.Sdk` (NOT `MSTest.Sdk/3.10.0` — plan templates lean on that, repo doesn't).
- Test files: `Kartova.SharedKernel.Tests` + `Kartova.SharedKernel.AspNetCore.Tests` + `Kartova.Organization.Tests` use **explicit per-file `using Microsoft.VisualStudio.TestTools.UnitTesting;`** (no GlobalUsings). `Kartova.Organization.Infrastructure.Tests` + `Kartova.ArchitectureTests` use **csproj-level `<Using Include="Microsoft.VisualStudio.TestTools.UnitTesting" />`** — no per-file using. Check the destination test project's csproj before deciding.
- Organization module's domain test project is `src/Modules/Organization/Kartova.Organization.Tests/`. Infrastructure-layer tests (including anything touching `OrganizationDbContext`, even via InMemory) go in `src/Modules/Organization/Kartova.Organization.Infrastructure.Tests/`. **Do NOT scaffold new test projects** for slice 9.
- Organization module's EF config filename convention is `*EntityTypeConfiguration.cs`.
- Windows PowerShell preferred for `dotnet`/`docker` commands; Git Bash lacks `grep -P` (use `-E` or `Select-String`).
- Use `roslyn-codelens` MCP for impact analysis before extending hot methods or renaming widely-used members.

**Docker availability:** Phase D5-D8 are pure C# (handlers + endpoints + DI + hosted service). D9's appsettings + KeyCloak realm config is config-only. Docker is needed for Phase H integration tests (H1-H4) and DoD verification (H3). Re-verify with `docker ps` before Phase H.

**Remaining task ledger** (mark each `[x]` as you ship):

### Phase A — Shared infrastructure foundation ✅ COMPLETE (9 tasks, 11 commits)

### Phase B — Database + domain ✅ COMPLETE (4 tasks, 6 commits)

### Phase C — JWT-claim sync + permissions ✅ COMPLETE (4 tasks, 6 commits)
- [x] C1: 7 new permission constants + role map + 28-cell matrix test (`f76dc68` + `44893c2`)
- [x] C2: Extend `ICurrentUser` with `JustAcceptedInvitationId` (`674463f`)
- [x] C3: `UserProjectionUpdater` + 3 unit tests (`120957f`)
- [x] C4: `IPostAuthSyncHook` + `OrganizationPostAuthSyncHook` wiring into `TenantClaimsTransformation` + 5 tests (`4cc5fa7` + `1b7834d`)

### Phase D — Backend endpoints (4 of 9 complete, 8 commits)
- [x] D1: `OrganizationUserDirectory` impl + DI + 4 unit tests (`675a455` + `4c58ac0`)
- [x] D2: Org profile DTOs + GET/PUT /me endpoints + 6 unit tests (`7e30b92` + `50d8d65`)
- [x] D3: SVG sanitization + magic-byte helper + 10 unit tests (`95c0435` + `adda5b6`)
- [x] D4: Logo upload/delete/serve endpoints with ETag/304 + 9 unit tests (`8d66ec5` + `ecb1be0`)
- [ ] **D5: Invitation handlers + endpoints (create/list/revoke) + 5+ unit tests** ← **START HERE**
- [ ] D6: User search + detail endpoints
- [ ] D7: Session bootstrap endpoint POST /api/v1/auth/session
- [ ] D8: `ExpireInvitationsHostedService` + 2 unit tests
- [ ] D9: Program.cs wiring + appsettings + KeyCloak realm config

### Phase E — Catalog/Team integration
- [ ] E1: `ApplicationResponse.Owner` enrichment via `IUserDirectory` (D1 already provides the production-side implementation; E1 wires Catalog to inject `IUserDirectory` and decorate responses)
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
2. Reading the plan at the path above to lift the verbatim Task D5 text (starts at line ~2946; the plan section is long — read enough to capture all 3 handlers + the test source).
3. Reading `src/Modules/Organization/Kartova.Organization.Domain/Invitation.cs` + `InvitationId.cs` + `InvitationStatus.cs` to confirm the aggregate's API surface (`Invitation.Create(...)`, `Invitation.Accept(...)`, `Invitation.Revoke(...)`, `Invitation.MarkExpired(...)`, status enum values).
4. Reading `src/Kartova.SharedKernel.Identity/IKeycloakAdminClient.cs` + `KeycloakAdminException.cs` + `KeycloakAdminError.cs` (or equivalent — discover via Glob) to confirm the mock surface for `CreateInvitationHandlerTests`.
5. Reading `src/Kartova.SharedKernel/Multitenancy/KartovaRoles.cs` to confirm the role-name constants (`KartovaRoles.Member`, etc.).
6. Reading `src/Modules/Organization/Kartova.Organization.Infrastructure/CreateTeamHandler.cs` (slice-8) as the canonical handler-pattern reference for D5.
7. Reading `src/Modules/Organization/Kartova.Organization.Infrastructure/OrgProfileQueries.cs` + `UpdateOrgProfileHandler.cs` (D2) as the more recent sibling pattern.
8. Reading `src/Kartova.SharedKernel.AspNetCore/ProblemTypes.cs` to know where the new invitation-conflict constants will live (per reconciliation #10 above).
9. Dispatching the D5 implementer with the verbatim task text + Phase D reconciliations 1–15 + the conventions block above. **Expect 4–5 plan-vs-reality corrections in the dispatch prompt** (Application→Infrastructure placement; `Invitation.Create` signature must be verified before assuming; the test file's `KartovaIdentityOptionsForTest` stub is a placeholder — locate the real options type; `KartovaRoles.Member` must exist as a string constant; the InviteUrl format may need adjustment based on actual `KeycloakAdminOptions` shape).

Continue until you hit a natural checkpoint. **D5 alone is large** — budget about half a session. D6 (search + detail endpoints, KC integration) is medium-complex; D7 (session bootstrap, reads `JustAcceptedInvitationId`) is medium-complex; D8 (hosted service) is small; D9 (wiring + config) is medium. A realistic Phase D completion would be D5 + D6 + D7 in one fresh session; D8 + D9 in another. At your stopping point, update this resume file's progress ledger and write a new resume prompt for the next session.

---

## Notes for future sessions

- **Each fresh session can realistically complete ~4–6 D-phase tasks** before context budget gets tight. The per-task overhead (dispatch + spec review + quality review + fix-up commit) is higher in D-phase than in C-phase because D-phase tasks have richer reconciliations (existing code to integrate with, plan-vs-reality bugs, etc.). D1–D4 ran in one session at ~4 tasks; D5 alone may eat 50–60% of a fresh session.
- **Update this file at every checkpoint** so the next session sees the latest progress.
- **Spec/plan reconciliations** should always commit on the feature branch with message prefix `docs(slice-9):` for spec/plan docs, or `fix(slice-9):` / `refactor(slice-9):` / `test(slice-9):` for infrastructure/code fixes. Each D-phase task that produced quality-review fixes used a separate `refactor(slice-9):` commit (clean linear history preserved for `requesting-code-review` at slice boundary).
- **If a subagent surfaces a real architectural question**, the controller (you) makes the call — do not push the question down into another subagent. D-phase examples: (a) D2's `IfMatchEndpointFilter` adoption question — controller deferred to "when xmin lands" + named the future adoption point in code; (b) D3's HtmlSanitizer version bump — controller accepted the security-driven deviation from plan; (c) D4's `ProblemTypes.UnsupportedLogoMedia` shared across 415/413/422 — controller accepted the spec-driven URI reuse despite reviewer's suggestion to split.
- **If `git show <SHA>` makes a deviation visible during review, fix it in a new commit** — never amend or rewrite (clean linear history preserved). Every D-phase task's fix-up was a NEW commit; the slice-boundary `requesting-code-review` will read the full diff cleanly.
- **CLAUDE.md DoD #5 requires Docker happy + negative HTTP paths captured** for HTTP/auth/DB/middleware slices. This slice is all three. H3 (Phase H) is non-negotiable. A8 + B4 smoke-tests covered the DB layer; D-phase endpoints + happy-path JWT round-trip land in H3.
- **Mutation testing target 80%** per `stryker-config.json`. H5's mutation-sentinel + test-generator loop is non-optional. D-phase tasks added boundary tests + content-assertion tests + three-context persistence tests preemptively to kill obvious mutants. **D5+ should continue this** — every new validator gets a boundary test; every new write-path handler gets a three-context persistence test.
- **Phase A's `KeycloakAdminClient` doesn't have an integration test against a real KeyCloak container yet** — the spec §11.3 mentions `Invitation_create_persists_keycloak_user_and_db_row` etc., but those land in Phase H (H1) alongside the endpoints in Phase D. Don't accidentally schedule them earlier. D5's unit tests mock `IKeycloakAdminClient` via NSubstitute (per plan).
- **`OrganizationDbContextModelSnapshot.cs` is now sensitive** — adding new Domain entities in D5+ would regenerate it. D-phase doesn't add new tables (B4 already added users + invitations + organization profile columns), so the snapshot should NOT change in any remaining D task — if it does, investigate immediately before committing.
- **D-phase Nits carried forward** (consider in Phase H mutation-sentinel pass, not Phase D scope):
  1. Carry-forward from C-phase: add `.ValueGeneratedNever()` on `User.Id` and `Invitation._id` defensively.
  2. Carry-forward from C-phase: document the `WHERE status = 1` filter in `AddInvitationsTable` migration with a SQL-side comment (`/* InvitationStatus.Pending */`).
  3. Carry-forward from C-phase: `OrganizationPostAuthSyncHook` claim names (`"email"`, `"sub"`, `"given_name"`, `"family_name"`) are hardcoded.
  4. Carry-forward from C-phase: `OrganizationPostAuthSyncHook.cs` filename cosmetic.
  5. **New from D-phase:** `IOrganizationQueries.GetCurrentAsync` + `OrganizationQueries` are now dead code on the /me path (D2 swap). Cleanup candidate.
  6. **New from D-phase:** the slice may want a separate `LogoTooLarge` problem-type URI rather than reusing `UnsupportedLogoMedia` for 413 (D4 reviewer flagged; controller deferred — spec calls for URI reuse).
  7. **New from D-phase:** `ProblemTypes.UnsupportedLogoMedia` URI is shared across 415/413/422. Status code disambiguates today; if a future SPA caller wants to discriminate by `type` URI alone, split it then.
  8. **New from D-phase:** add a `Content-Security-Policy` header on the SVG serving path (GET /me/logo) for defence-in-depth. Better as a Program.cs-level concern for all binary endpoints.
  9. **New from D-phase:** D4 has no integration test for the 413 streaming guard, 415 mime parse, or `If-None-Match` round-trip — unit tests cover `LogoCommands` but not the endpoint delegates. H1 will cover this.
