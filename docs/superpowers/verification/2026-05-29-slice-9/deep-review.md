# Slice 9 — Deep PR Review (DoD #9)

**Target:** `feat/slice-9-organization-people-management` (HEAD `f178520`) vs `master` (`248dee8`)
**Status:** OPEN — pre-merge gate
**Diff scope:** 283 files, +21,019 / −576 LOC, 116 commits
**Spec:** `docs/superpowers/specs/2026-05-27-slice-9-organization-people-management-design.md`
**Plan:** `docs/superpowers/plans/2026-05-27-slice-9-organization-people-management-plan.md`
**ADRs touched:** 0011 (one-org-per-tenant), 0080 (Wolverine no-MediatR), 0083 (test taxonomy), 0084 (Playwright cold-start), 0090 (tenant scope), 0095 (cursor pagination), 0096 (If-Match optimistic concurrency), 0097 (test pyramid), 0098 (cross-module ports), 0100 (strict one-email-per-tenant — new)
**Prior review passes consulted:** slice-boundary code review (`a0dfabd5347d4cef0`), pr-test-analyzer (`a1540cd98f70c255f`), silent-failure-hunter (`add9f35b431e5081e`), type-design-analyzer (`a5d9e64450a88c59e`), comment-analyzer (`a4ae75d4d799318fa`). Fixes applied across `03f5738..f178520`.

This review focuses on cross-cutting findings the 5 prior passes missed: ADR compliance audit, cross-slice fit, DoD evidence trail, and architecture-test coverage of the NEW patterns slice-9 introduced.

---

## Overview

Slice 9 ships invitations, org profile + logo, user search + detail, session bootstrap, KeyCloak admin client integration, JWT-claim sync via post-auth hook, 7 new role permissions, and ADR-0100. Backend covers RLS-scoped `users` + `invitations` tables, real-KC integration tests via Testcontainers (88 Org IT + 5 KC admin IT + 70 arch + 16 SK.Identity unit + 369 SPA vitest), Alpine `tzdata` runtime fix, opt-in `KartovaApiFixtureBase` KC composition, and a closed concurrency gap via UNIQUE partial index + 23505 catch.

---

## Blocking-class issues

### 1. DoD #4 evidence not yet captured at slice HEAD

**Evidence:** `CLAUDE.md §Definition of Done #4` requires "Full test suite green: unit + architecture + integration (Testcontainers)" cited by command + output. The per-batch reports across H1-H5 cite per-suite counts at different SHAs (84 → 87 → 88 Org IT over time), but no single `dotnet test Kartova.slnx` invocation at `f178520` has been captured. The pr-review-toolkit critical-fixes commit (`f178520`) added a new test (Infrastructure.Tests 72 → 73) but never ran the full sweep.

**Impact:** DoD #4 is non-citable until a single-invocation test run is captured. The honest status is "implementation staged, full-solution test verification pending" — claiming complete without it fails the Stop hook. The risk is concrete: H2's `KeycloakAdminOptionsEnvValidator` regression was caught only by running the integration tests, not by per-suite-in-isolation reasoning. Another such regression could be present in the 116-commit branch.

**Fix:** Task #17 in the project todo list — run `dotnet build Kartova.slnx -c Release /p:TreatWarningsAsErrors=true` + `dotnet test Kartova.slnx -c Release --no-build` at `f178520` (or branch tip when this lands). Capture and cite the per-assembly totals in the H6 commit message or as evidence in the PR description.

### 2. DoD #7 mutation score not measured against slice-9 production code

**Evidence:** Mutation testing was started in this session against all 12 projects; the first run (`briqwh4mp`) exited 1 after producing reports for projects 1-5 (`SharedKernel`, `SharedKernel.AspNetCore`, `SharedKernel.Postgres`, `Catalog.Domain`, `Catalog.Application`). Projects 6-12 — which include every slice-9-touched Organization project — were never mutated. `StrykerOutput/mutation-sentinel-gh-last-run.manifest` shows `report_count=5`, `expected_report_count=12`, `exit_code=1`.

**Impact:** Spec §11.6 names the focus surfaces — `Invitation` state machine, `OrgLogo.Create` validation, `ExpireInvitationsHostedService`, `PostgresAdvisoryLock.Handle.DisposeAsync` catch-clauses. NONE of these were mutation-tested. The repo target ≥80% is unverified for the slice-9 code itself.

**Fix:** Re-run mutation-sentinel scoped to the failed projects: `dotnet-stryker --project Kartova.Organization.Domain.csproj --since:master` (started concurrently in this session as task `by2t9fm2a`), then `Kartova.Organization.Infrastructure.csproj`, `Kartova.Organization.Infrastructure.Admin.csproj`. Parse the resulting reports via `scripts/ms-translate-stryker-results.ps1`, address ≥80% surviving mutants. If full mutation testing is deferred to a follow-up commit, document the score gap explicitly in the PR description as "DoD #7 partial: SharedKernel + Catalog mutation reports captured; Organization scope to follow in PR-N+1".

---

## Should-fix issues

### S1. ~~New `IPostAuthSyncHook` interface has no architecture-test enforcement~~ (RESOLVED — interface deleted in `e8bf859`, post-review)

**Resolution note (post-review):** The `IPostAuthSyncHook` interface, its sole `OrganizationPostAuthSyncHook` implementation, and the related `ICurrentUser.JustAcceptedInvitationId` / `ITenantContext.SetJustAcceptedInvitation` infrastructure were all deleted in `e8bf859` and the responsibilities consolidated into `SessionStartHandler.HandleAsync`. The proposed arch test (sealed + suffix + Infrastructure namespace) no longer has a target. See spec §9.1 rationale paragraph for the design discussion.

**Evidence:** `src/Kartova.SharedKernel.AspNetCore/IPostAuthSyncHook.cs` is a new cross-module port. `tests/Kartova.ArchitectureTests/Slice9BoundarySentinels.cs` has 4 new arch tests but none assert that `IPostAuthSyncHook` implementations are `sealed`, end with `PostAuthSyncHook` suffix, or live in their owning module's Infrastructure namespace.

**Impact:** ADR-0098 establishes the cross-module port pattern. Without an arch sentinel, a future module can register an `IPostAuthSyncHook` that ships in `Catalog.Domain` (wrong namespace) or fails the idempotency contract documented on the interface. The reviewer panel (type-design pass) flagged this — should land as a follow-up arch test.

**Fix:** Add to `Slice9BoundarySentinels.cs`:
```csharp
[TestMethod]
public void IPostAuthSyncHook_implementations_live_in_module_Infrastructure_and_are_sealed()
{
    var hookType = typeof(IPostAuthSyncHook);
    foreach (var asm in AssemblyRegistry.AllProduction())
    {
        var impls = asm.GetTypes()
            .Where(t => t.IsClass && hookType.IsAssignableFrom(t))
            .ToList();
        foreach (var impl in impls)
        {
            Assert.IsTrue(impl.IsSealed, $"{impl.FullName} must be sealed.");
            StringAssert.EndsWith(impl.Name, "PostAuthSyncHook");
            StringAssert.Contains(impl.Namespace ?? "", ".Infrastructure");
        }
    }
}
```

### S2. ADR-0100 schema claim not enforced at the DB level

**Evidence:** `docs/architecture/decisions/ADR-0100-strict-one-email-per-tenant.md` documents "strict one-email-per-tenant" identity scope. The slice ships partial UNIQUE index `idx_invitations_email_pending` (closed in `eb1fe88`) for **pending** invitations only — but there is no equivalent UNIQUE on the `users` projection (the actual identity record). `src/Modules/Organization/Kartova.Organization.Infrastructure/UserEntityTypeConfiguration.cs` has no `(tenant_id, email)` unique constraint.

**Impact:** Two `users` rows with the same `(tenant_id, lower(email))` can be inserted via the post-auth hook if two distinct KC `sub` claims share the same email — exactly the case ADR-0100 forbids. The post-auth hook (`OrganizationPostAuthSyncHook.UpsertAsync`) currently keys by KC user id (sub), not by email. The CASE that ADR-0100 wants to prevent (one human, two KC sessions sharing an email) is invisible to the projection writer.

**Fix:** Either (a) add `CREATE UNIQUE INDEX ix_users_tenant_email ON users (tenant_id, lower(email))` migration and update the upsert handler to surface a typed error on 23505, OR (b) update ADR-0100 to scope its claim to "no PENDING invitation across (tenant, email)" — which is what the code actually enforces today. Pick the path that matches operational intent.

### S3. `KeycloakAdminClient.GetTokenAsync` fetches a fresh token on every call (no cache)

**Evidence:** `src/Kartova.SharedKernel.Identity/KeycloakAdminClient.cs:123-129` calls `tokenClient.RequestClientCredentialsTokenAsync` for every Admin API operation. Each invitation create makes ≥3 KC round trips (token + user-create + role-get + role-assign = 4 token fetches). Spec §15 was reconciled in `807ca5b` to acknowledge this is intentional for slice-9 volume, but the spec wording says "follow-up at E-06a" — no follow-up issue/ticket exists.

**Impact:** At E-06a notification slice scale (~100 invites/min/tenant during bulk-import), the 4× token-fetch multiplier becomes a measurable KC load. The TokenClient instance is a Singleton in DI (`ServiceCollectionExtensions.cs:31-41`) — caching is on the doorstep.

**Fix:** Add an `IAccessTokenCache` (or Duende's `AccessTokenManagement` package) wrapping `TokenClient`. The slice-9 deferral is acceptable but a tracked ticket should exist before merge — search GitHub issues or backlog for "KeycloakAdminClient token cache" and link from the PR description.

### S4. `OrgLogo.Bytes` exposes mutable `byte[]`; `ContentHash` invariant bypassable

**Evidence:** `src/Modules/Organization/Kartova.Organization.Domain/OrgLogo.cs` — `Bytes` is a property returning `byte[]`. A reference to the array can be mutated by any caller (`logo.Bytes[0] = 0xFF`) which silently invalidates `ContentHash`.

**Impact:** Two write paths consume `Bytes`: EF persistence (no mutation) and `OrganizationProfileEndpointDelegates.GetLogoAsync` (no mutation). Risk is forward-looking — any future caller that does `Bytes[k] = …` (e.g., watermarking, format conversion) breaks the invariant. The type-design reviewer flagged this — should land as a follow-up via `ReadOnlyMemory<byte>` return or a `CopyBytesTo(Span<byte>)` API.

**Fix:** Return `ReadOnlyMemory<byte>` from the public property and keep a private `byte[]` field for EF mapping; expose `CopyBytesTo(Span<byte>)` for the streaming path. Existing call sites use `o.Logo!.Bytes` (`OrganizationProfileEndpointDelegates.cs:226`) and pass to `Results.File(byte[])` — they'd need migration to `Results.Stream(ReadOnlyMemory<byte>.ToStream())` or equivalent.

### S5. Cursor codec does not encode `ownerUserId` filter

**Evidence:** Resume-prompt carry-forward #17 + slice-9 spec §6.5 cursor format. `src/Modules/Catalog/Kartova.Catalog.Infrastructure/ListApplicationsHandler.cs:97-102` documents a `// TODO: Phase H` comment about the cursor codec not encoding `ownerUserId`.

**Impact:** Cursor-replay under a different `ownerUserId` produces inconsistent results without error. RLS bounds the visible set, but the page boundary is computed against an unfiltered ordering. The SPA does not change filters mid-pagination (verified by reading `OwnerLink.tsx` + `applications.ts` hooks), so this is forward-looking risk. The TODO has been in place since slice-9 E2 (`15395c9`) without resolution.

**Fix:** Either (a) encode the filter in the opaque cursor blob (the cursor codec lives in `Kartova.SharedKernel.Postgres.Pagination` per H5's R2 lift), OR (b) update the comment to mark this as deliberate-deferral with a tracking issue. The spec doesn't mandate one or the other — pick whichever closes the TODO honestly.

### S6. `OrganizationModule.MapEndpoints` is 200+ lines after the delegate split

**Evidence:** `src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationModule.cs:43-224` — the `MapEndpoints` method registers 22 routes via `tenant.MapXxx(...)` + `authGroup.MapPost(...)` chains. The H5 simplifier-pass split the delegates into per-resource files but left this central registration intact.

**Impact:** The method has grown beyond comfortable single-screen review. A new module endpoint in slice 10 will linearly grow this further. Pattern is otherwise sound (single source of routing truth), so the right cleanup is extraction rather than splitting the module.

**Fix:** Per the simplifier-pass pattern, lift the per-resource registrations to extension methods on `IEndpointRouteBuilder` (or to each delegate file): `app.MapInvitationEndpoints(tenant, ...)`, `app.MapTeamEndpoints(tenant, ...)`, etc. The module file becomes 30 lines of composition. Defer to slice-10 if the H6 PR description is already large.

---

## Nits

### N1. `OrganizationModule.cs:240` `TryAddSingleton(TimeProvider.System)` — `TryAdd` vs `Add` semantics

The comment explains: "TryAdd is idempotent — if another module (or test fixture override) already registered TimeProvider, this is a no-op". Verified correct against `KartovaApiFixtureBase` — but in production, the first registration wins. If `CatalogModule.cs:240` also registers `TryAddSingleton(TimeProvider.System)` AND a future slice ships `services.AddSingleton<TimeProvider, FrozenTimeProvider>()` for testing, the result depends on registration order. Document the "first registration wins" contract in the comment.

### N2. `web/src/shared/api/openapi-fetch-helpers.ts` — the new `throwWithStatus` doesn't preserve `Error.stack`

The H5 R1 helper builds `Error(message)` with `__status` attached but does not preserve the original error's stack trace if the source was an Error. Cosmetic; existing callers don't pass Error in.

### N3. `tests/Kartova.SharedKernel.Identity.IntegrationTests/KeycloakAdminClientIntegrationTests.cs` — `StubHostEnvironment` is duplicated

A `StubHostEnvironment` lives at `KeycloakAdminClientIntegrationTests.cs:78-87` AND at `tests/Kartova.SharedKernel.Identity.Tests/KeycloakAdminOptionsValidationTests.cs`. Lift to `Kartova.Testing.Auth` if a third caller appears.

### N4. `Kartova.SharedKernel.Identity.IntegrationTests.csproj` is missing from `mutation-targets.json`

The new test project added in H1 batch 5 isn't a *production* project, so it shouldn't be mutated — correct that it's absent. But the `Kartova.SharedKernel.Identity` production project (the SUT for those tests) IS in `mutation-targets.json`. Confirm by inspection — likely already correct, flag only to verify.

### N5. Spec docstring drift: `slice-9 spec §6.x` placeholder in `TeamEndpointDelegates.cs:121`

Comment-analyzer flagged this. The `§6.x` unresolved token should resolve to a concrete §. Minor doc fix; not slice-blocking but should land as part of the H6 doc commit.

---

## Missing tests

Acceptance criteria from spec §11.3 that lack corresponding integration tests:

### MT1. Spec §6.1 415 unsupported media type — no integration test
- **Acceptance:** `Logo_upload_with_unsupported_content_type_returns_415` (e.g., `Content-Type: image/gif`)
- **Test that should exist:** Project: `Kartova.Organization.IntegrationTests`. Class: `OrgProfileAndLogoTests`. Scenario: PUT `/me/logo` with `Content-Type: image/gif` and a small payload, assert 415 + `ProblemTypes.UnsupportedLogoMedia`.

### MT2. Spec §6.1 422 magic-byte mismatch (PNG-claimed-as-JPEG) — no HTTP wire test
- **Acceptance:** Magic-byte mismatch path emits 422 + `ProblemTypes.LogoInvalidContent`.
- **Test that should exist:** Project: `Kartova.Organization.IntegrationTests`. Class: `OrgProfileAndLogoTests`. Scenario: PUT `/me/logo` with `Content-Type: image/jpeg` and PNG magic bytes, assert 422 + `ProblemTypes.LogoInvalidContent` + detail "magic-byte".

### MT3. Spec §6.2 502 KC upstream — no HTTP wire test
- **Acceptance:** `CreateInvitationHandler.Upstream` and `RevokeInvitationHandler.Upstream` map to 502 + `ProblemTypes.ServiceUnavailable`. Tested at handler level only.
- **Test that should exist:** Project: `Kartova.Organization.IntegrationTests`. Class: `InvitationTests`. Scenario: replace the registered `IKeycloakAdminClient` via `ConfigureTestServices` with an NSubstitute returning `KeycloakAdminError.Unexpected` on `AssignRealmRoleAsync`; POST `/invitations`; assert 502 + `ProblemTypes.ServiceUnavailable`. Repeat for revoke.

### MT4. Spec §9.7 ExpireInvitationsHostedService — non-NotFound KC error abort path untested
- **Acceptance:** Sweep aborts before `SaveChangesAsync` on non-NotFound KC errors; partial-commit prevented (carry-forward #12).
- **Test that should exist:** Project: `Kartova.Organization.Infrastructure.Tests`. Class: `ExpireInvitationsHostedServiceTests`. Scenario: seed 2 due invitations; substitute KC to throw `KeycloakAdminError.Unexpected` on row 1's `DeleteUserAsync`; invoke `ExpireDueAsync`; assert exception propagates AND both rows remain `Pending` (BYPASSRLS read).

### MT5. Spec §6.3 422 typeahead query validation — no integration tests
- **Acceptance:** `GET /users?q=` (empty) returns 422; `GET /users?q=a` (1 char) returns 422.
- **Test that should exist:** Project: `Kartova.Organization.IntegrationTests`. Class: `UserSearchTests`. Two scenarios: empty `q` → 422; single-char `q` → 422. Assert `ProblemTypes.ValidationFailed`.

### MT6. Spec §9.2 step 3 — `EmailAlreadyInvited` 23505 race-path lacks integration coverage
- **Acceptance:** Concurrent invite creates lose the race at DB level (closed in `8c4d99b`); losing request gets 409 `EmailAlreadyInvited`, not 500.
- **Test that should exist:** Project: `Kartova.Organization.Infrastructure.Tests`. Unit test added in `8c4d99b` covers the catch. An integration test would assert the full wire surface. Scenario: spin up two parallel `POST /invitations` calls with the same email + tenant, assert one gets 201 and the other gets 409 `EmailAlreadyInvited` (NOT 500). May be flaky on race timing; acceptable to defer with a comment.

### MT7. Logo 256 KiB boundary at HTTP wire level
- **Acceptance:** Upload of exactly 256 KiB valid JPEG succeeds; 256 KiB + 1 byte fails 413.
- **Test that should exist:** Project: `Kartova.Organization.IntegrationTests`. Class: `OrgProfileAndLogoTests`. Two scenarios: 262144-byte valid JPEG → 200; 262145-byte payload → 413. The current 300 KiB test doesn't hit the boundary.

---

## What looks good

### G1. Migration `MakeInvitationsPendingIndexUnique` closes carry-forward #10 at the DB layer
**File:** `src/Modules/Organization/Kartova.Organization.Infrastructure/Migrations/20260529173745_MakeInvitationsPendingIndexUnique.cs`. The race condition between `AnyAsync` pre-check and `SaveChangesAsync` insert is closed by promoting the partial index to UNIQUE — defense in depth alongside the application-level pre-check + the new `8c4d99b` 23505 catch in `CreateInvitationHandler`. Three layers of enforcement on the same invariant, with the DB as the final arbiter. This is the textbook pattern for closing a documented race condition.

### G2. ~~`IPostAuthSyncHook` lifecycle documentation~~ (interface deleted in `e8bf859`; the XML doc no longer applies)
**File:** `src/Kartova.SharedKernel.AspNetCore/IPostAuthSyncHook.cs`. The XML doc explicitly states: where in the pipeline it runs (after `ITenantScope.BeginAsync`, after `TenantClaimsTransformation` + membership reader), ordering (registration order, sequential), idempotency expectation, and firing scope (only `RequireTenantScopeMarker` endpoints). The comment-analyzer pass identified this as the "template-quality" XML doc to apply to other cross-module ports.

### G3. The slice-9 cleanup-order convention is enforced consistently across 5 integration-test files
**Files:** `InvitationTests.cs`, `OrgProfileAndLogoTests.cs`, `UserSearchTests.cs`, `SessionBootstrapTests.cs`, `UserDetailTests.cs`. Every `finally` block deletes `users` rows BEFORE other-module rows, wraps each cleanup step in per-step try/catch with `Console.Error` logging. This is exactly the slice-9 e5aaf73/4715c87 convention from the resume prompt. The `CleanupTenantInvitationsAsync` / `CleanupTenantOrgAsync` helpers (lifted in H5 R2 + R3) cement it. A regression here would surface as a single test class diverging from the pattern — easy to grep for.

### G4. H3 + H4 verification surfaced 5 real production bugs that integration tests missed
**Files:** `docs/superpowers/plans/slice-9-docker-verification.md`. The docker-compose HTTP verification (KC `username` missing, Migrator Dockerfile csproj gap, Alpine `tzdata`) + Playwright E2E (EF Join translation, SPA logo URL routing, `inviteUrl` placeholder) surfaced 5 distinct production bugs that the per-batch unit/integration test review could not. The slice's H3 + H4 work IS the DoD #5 evidence at its strongest — this is what the gate is for.

### G5. ADR-0100 (strict one-email-per-tenant) was authored as part of slice-9 rather than retrofit
**File:** `docs/architecture/decisions/ADR-0100-strict-one-email-per-tenant.md`. The decision is documented before the implementation is final — readers of slice-9 code can find the architectural rationale via the ADR keyword index. This is the right inversion compared to retrofitting ADRs against shipped code.

### G6. `Slice9BoundarySentinels.cs` arch tests include drift sentinels, not just current-state assertions
**File:** `tests/Kartova.ArchitectureTests/Slice9BoundarySentinels.cs`. `IDistributedLock_implementations_use_session_advisory_locks` reads the actual source file for the `pg_try_advisory_lock` literal — a drift sentinel that catches "someone refactored to xact-level lock and broke leader election". The IANA timezone sentinel (`Runtime_can_resolve_common_IANA_timezones`) similarly anchors the H3-surfaced Alpine `tzdata` regression at the test layer.

---

## DoD evidence trail

| Gate | Status | Evidence |
|------|--------|----------|
| #1 Build green | ✅ | Per-commit reports cite 0/0 at HEAD `f178520` (last H5 critical-fix dispatch) |
| #2 Per-task subagent reviews | ✅ | Spec + quality reviews ran for H1-prereq + each H1 batch 1-5 + each H2-H5 step (>20 review dispatches captured) |
| #3 `/superpowers:requesting-code-review` at slice boundary | ✅ | Subagent `a0dfabd5347d4cef0`; 5 Important findings addressed in `03f5738..807ca5b` |
| #4 Full test suite green | ⏳ **Blocking #1** | Per-suite at `f178520`: Org IT 88, Catalog IT 96, Arch 70, SK.Identity 16, SK.Identity.IT 5, SK.Postgres.IT 3, Api IT 5, SPA vitest 369. NO single-invocation full-solution `dotnet test` capture at HEAD. |
| #5 docker compose HTTP happy + negative | ✅ | `fea16af` (initial 8 scenarios) + `5307367` (Europe/Warsaw re-verification post-tzdata fix). `docs/superpowers/plans/slice-9-docker-verification.md` |
| #6 `/simplify` against branch diff | ✅ | Subagent `a2cbd2a893de268ab` (5 findings + 3 skip-with-reason); 5 commits `ad469e6..68158d5` |
| #7 Mutation score ≥80% | ⏳ **Blocking #2** | Partial: 5 of 12 mutation reports captured (SharedKernel + Catalog). Organization scope NOT mutated. |
| #8 `/pr-review-toolkit:review-pr` | ✅ | 4 agents dispatched in parallel; critical fixes in `8c4d99b..f178520` |
| #9 `/deep-review` | ✅ | This document |

---

## Recommended next actions

1. **Unblock DoD #4** — run full-solution `dotnet test Kartova.slnx -c Release` at HEAD. Capture output. Cite in PR description.
2. **Unblock DoD #7** — re-run mutation testing scoped to Organization projects (started concurrently as `by2t9fm2a`). Parse via translator script, document score, address survivors above 80% target.
3. **Defer remaining S1-S6** to slice-10 entry with explicit follow-up issues — the user-impact is bounded by the absent forward-looking risk these address.
4. **Apply N1-N5** in a single `docs(slice-9):` commit during H6 finalization.
5. **Land MT1-MT7** as a single `test(slice-9):` follow-up commit after merge if PR review timeline is tight; OR as the H6-eve commit if DoD #7 mutation gap reveals overlapping survivors.

The slice is ready to merge after the two blocking gates are evidenced. The S1-S6 + N1-N5 + MT1-MT7 items are appropriate follow-up scope — none of them block on functional correctness.
