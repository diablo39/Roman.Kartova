# Deep PR Review — Invitation Set-Password Flow
**Date:** 2026-06-01
**Branch:** feat/slice-9-organization-people-management (invitation sub-slice)
**Diff range:** afefa67..HEAD (77c7e8f) — 23 commits
**Reviewer:** Claude Sonnet 4.6 (automated deep review, gate 9)

---

## Overview

The slice implements the full invitation-accept flow end-to-end: `InvitationToken` (CSPRNG, SHA-256 hash), `Invitation` aggregate extension (`TokenHash`/`CredentialSetAt`/`MarkCredentialSet`), EF Core migration with partial-unique index, `AcceptInvitationHandler` (BYPASSRLS), two anonymous ASP.NET Core minimal-API endpoints with per-IP rate limiting and `Referrer-Policy: no-referrer`, `SetPasswordAsync`/`UpdateUserAsync` additions to `IKeycloakAdminClient`, `kartova-realm.json` hardening (`length(12)` + `bruteForceProtected`), and the React `AcceptInvitationPage` with anonymous API client. All spec-listed files are present and the architecture boundaries (ADR-0082, ADR-0090) are respected. Prior review passes (per-task quality, full-branch code review, `/simplify`, `/pr-review-toolkit`) applied their findings through commit `77c7e8f`; the deliberately documented deviations (spec §Context) are confirmed in code.

---

## Blocking-class issues

None.

---

## Should-fix issues

### 1. `MarkCredentialSet` throws `InvalidOperationException` on a path the handler cannot reach — silent landmine for future callers

**Evidence:** `src/Modules/Organization/Kartova.Organization.Domain/Invitation.cs:56–57`
```csharp
if (CredentialSetAt is not null)
    throw new InvalidOperationException("Credential already set for this invitation.");
```
`AcceptInvitationHandler.AcceptAsync` calls `MarkCredentialSet` only after `ResolveAsync` returns a `Pending`-status tracked entity. By the time execution reaches that line `CredentialSetAt` is always `null` — the guard can only be triggered by a caller that invokes `MarkCredentialSet` a second time on the same in-memory instance (e.g. a future handler that retries after a transient DB failure). However, the guard throws an `InvalidOperationException` rather than returning a typed result, making it invisible to the handler's `try/catch` block and the route's switch. If the guard fires it propagates as an unhandled 500 — not a 410 — with the password already set in KC.

**Spec cite:** Spec §4 "single-use guard + audit"; spec §5.2 "concurrent second POST affects 0 rows"; plan Task 2 (the guard was added to prevent stale re-use but the error contract is not documented).

**Impact:** Low probability in production (requires the same in-memory `Invitation` object to be used twice), but any future retry path or unit-test mistake that calls `MarkCredentialSet` twice gets a 500 instead of a graceful 410. The `CredentialSetAt is not null` guard is also untested (there is a test for `AcceptAsync` status==Accepted via `MarkAccepted`, which goes through `ResolveAsync`, but no test for the idempotency guard itself).

**Fix:** Either document that this exception path is intentionally unreachable from the current handler (a `// UNREACHABLE from AcceptAsync: ResolveAsync already guards Pending-only` comment would suffice), or replace the throw with a no-op/return (if idempotency is the goal). Add a unit test that calls `MarkCredentialSet` twice on the same object and asserts the chosen behavior.

**Test that would catch it:** `InvitationTests` — add `MarkCredentialSet_is_idempotent_or_throws_predictably`.

---

### 2. `AcceptInvitationHandlerTests` uses an in-memory provider — `ExecuteUpdateAsync` not exercised at the unit-test layer

**Evidence:** `src/Modules/Organization/Kartova.Organization.Infrastructure.Tests/AcceptInvitationHandlerTests.cs:26–29` (`UseInMemoryDatabase`)

The actual implementation in `AcceptInvitationHandler` uses `MarkCredentialSet(clock)` + `SaveChangesAsync` (not `ExecuteUpdateAsync`), so this mismatch has no current correctness impact. However, the plan (Task 7 step 3 comment) and spec §5.2 describe a CAS/`ExecuteUpdateAsync` path that was superseded by the `MarkCredentialSet` domain method. The unit-test comment at line 111–116 still says "Strict atomic compare-and-swap is intentionally NOT used — the concurrent same-token case is benign" but does not test that the `SaveChangesAsync` actually burns the token under InMemory (it does, because change-tracking writes it). The integration tests (`InvitationAcceptTests.cs`) do exercise the full Postgres path.

**Spec cite:** Spec §5.2 "Compare-and-swap"; plan Task 9 (concurrent POST → exactly one 200); spec Context note "concurrent same-token case is benign".

**Impact:** The plan's Task 9 calls for a "two concurrent POSTs → exactly one 200" integration test which is absent from `InvitationAcceptTests.cs` (the test file has 7 tests; none tests true parallel POST concurrency). For a `SaveChangesAsync`-based burn, a true concurrent-POST scenario can in principle result in both calls calling `MarkCredentialSet` if both loads happen before either save — the `CredentialSetAt is not null` guard inside `MarkCredentialSet` would then throw on the second call, producing a 500 rather than 404/410.

**Fix:** Add a concurrent integration test with two `Task.WhenAll`'d `PostAsJsonAsync` calls to `/api/v1/invitations/accept` with the same token, asserting exactly one `200` and one `404` (or `410`). This kills the concurrency edge case definitively.

**Test that would catch it:** `InvitationAcceptTests` — add `Post_accept_concurrent_calls_with_same_token_returns_one_200_and_one_404`.

---

### 3. `UpdateUserAsync` NotFound path is not unit-tested for `AcceptInvitationHandler`

**Evidence:** `src/Modules/Organization/Kartova.Organization.Infrastructure.Tests/AcceptInvitationHandlerTests.cs` — no test named `kc_UpdateUser_NotFound_*`.

The handler catches `KeycloakAdminError.NotFound` from the entire KC block (SetPassword + UpdateUser) and maps it to `GoneAlreadyUsed`. The unit suite has `AcceptAsync_kc_SetPassword_NotFound_returns_GoneAlreadyUsed` but no equivalent for `UpdateUserAsync` throwing NotFound. Both calls share the same `catch (KeycloakAdminException ex) when (ex.Error == KeycloakAdminError.NotFound)` block (lines 96–101), so the second caller is covered by the same catch — but there is no test asserting the path where `SetPasswordAsync` succeeds and `UpdateUserAsync` then throws `NotFound`.

**Spec cite:** Spec §5.2 "KC user gone → NotFound → 410"; plan Task 7 test skeleton line: `kc.SetPasswordAsync(...).Throws(...)`; ADR-0097 (unit tier must cover all public surface branches).

**Impact:** Surviving mutant risk: a mutation that changes `when (ex.Error == KeycloakAdminError.NotFound)` to always-true (or removes the second catch arm) would not be caught.

**Fix:** Add `AcceptAsync_kc_UpdateUser_NotFound_returns_GoneAlreadyUsed` to `AcceptInvitationHandlerTests` — stub `SetPasswordAsync` to return normally, stub `UpdateUserAsync` to throw `NotFound`.

---

## Nits

1. **`InvitationAcceptRoutes.cs:14` — `RateLimitPolicy` constant is `public` but the type is `internal static`** — `public const string` on an `internal` type is effectively `internal`. Consistency with the enclosing access level: change to `internal const string`. (`src/Modules/Organization/Kartova.Organization.Infrastructure.Admin/InvitationAcceptRoutes.cs:12`)

2. **`AcceptInvitationHandlerTests.cs:39–57` — `SeedPendingInvitation` does synchronous `SaveChanges`** — all other helpers in the file use `await SaveChangesAsync`. No correctness issue with InMemory, but async consistency (`await seedDb.SaveChangesAsync()`) avoids a divergence if the provider ever changes. (`src/Modules/Organization/Kartova.Organization.Infrastructure.Tests/AcceptInvitationHandlerTests.cs:56`)

3. **`InvitationAcceptRoutes.cs:55` — GET fallthrough maps any non-NotFound error to a generic 410** — the spec §5.1 shows `GET 410 {reason}` where `reason ∈ expired|revoked|alreadyUsed`. The current `GetAcceptContextResult.Failed` (non-NotFound) collapses all reasons into "The invitation has expired, been revoked, or was already accepted." This is a documented intentional deviation (spec Context note "GET 410 returns a generic reason") but a brief inline `// deliberate: see spec §5.1 + context-review deviation note` comment would help reviewers understand the fallthrough is not an oversight. (`src/Modules/Organization/Kartova.Organization.Infrastructure.Admin/InvitationAcceptRoutes.cs:54`)

4. **`anonymousClient.ts` imports `API_BASE_URL` from `@/features/catalog/api/client`** — coupling the invitations feature to the catalog feature's export. The plan's file structure lists this as intentional (Task 11 step 2), but a shared `@/shared/api/baseUrl.ts` barrel would make the dependency direction cleaner if either module is extracted later. (`web/src/features/invitations/api/anonymousClient.ts:2`)

5. **`CHECKLIST.md` line 124 — E-03.F-01.S-02 still says "invite users" with no mention of accept flow** — the story covers the accept half per the spec. Update the checklist entry to read "...plus accept-invitation set-password flow (slice-9 sub-slice, PR #TBD, 2026-06-01)" so the progress record is accurate. (`docs/product/CHECKLIST.md:124`)

---

## Missing tests

1. **`MarkCredentialSet` idempotency guard** — `Kartova.Organization.Tests/InvitationTests.cs`: no test for calling `MarkCredentialSet` on an already-set invitation. Given the guard throws `InvalidOperationException`, the behavior needs a test: `MarkCredentialSet_when_already_set_throws_InvalidOperationException` (or asserts no-op if the behavior is changed). Without it the guard is a dead-code path with no coverage and is a mutation-testing gap.

2. **Concurrent POST to `/accept` (same token)** — `Kartova.Organization.IntegrationTests/InvitationAcceptTests.cs`: the plan Task 9 explicitly calls for "two concurrent POSTs → exactly one 200" but the integration test file contains no such test. Scenario: `Post_accept_concurrent_calls_with_same_token_returns_one_200_and_one_404`. This is the only integration spec requirement currently absent.

3. **`UpdateUserAsync` throws `NotFound` mid-accept** — `Kartova.Organization.Infrastructure.Tests/AcceptInvitationHandlerTests.cs`: `AcceptAsync_kc_UpdateUser_NotFound_returns_GoneAlreadyUsed`. Stub: `SetPasswordAsync` returns normally; `UpdateUserAsync` throws `KeycloakAdminException(NotFound)`. Assert `GoneAlreadyUsed` returned and token is NOT burned (KC-first retry semantics).

4. **GET `/accept` returns 429 under rate limiting** — no test in `InvitationAcceptTests.cs` or `AuthSmokeTests.cs` exercises the `invitation-accept` rate-limit policy and verifies a `429 Too Many Requests` response with the correct status code. Scenario: send 11 rapid anonymous GET requests against the same endpoint with the same IP, assert the 11th returns 429.

5. **`acceptInvitation` API module — `POST` sends 502 handling test missing** — `web/src/features/invitations/api/__tests__/acceptInvitation.test.tsx`: the suite covers 200/410/400 but not 502. The page maps "other" errors to a generic `setGlobalError`, but the API module test should assert `throwWithStatus` surfaces `__status: 502` for a 502 response so the page's fallback branch is exercised. (`web/src/features/invitations/api/__tests__/acceptInvitation.test.tsx`)

---

## What looks good

1. **Token security model is fully correct** — `InvitationToken.Issue()` uses `RandomNumberGenerator.GetBytes(32)` → `Base64Url.EncodeToString` (BCL, no hand-rolled encoding); only the SHA-256 hash is persisted; the partial unique index (`token_hash IS NOT NULL`) provides both a lookup key and a uniqueness guarantee; the burn (`TokenHash = null`) makes a reused token resolve to not-found via hash lookup. The full chain from issuance through DB hash through single-use burn is exercised end-to-end in `CreateInvitationHandlerTests.Create_returns_tokenized_url_and_persists_hash`. (`src/Modules/Organization/Kartova.Organization.Domain/InvitationToken.cs`, `src/Modules/Organization/Kartova.Organization.Infrastructure/Migrations/20260601142121_AddInvitationTokenColumns.cs:28`)

2. **ADR-0090 BYPASSRLS placement is correct and documented** — `AcceptInvitationHandler` lives in `Kartova.Organization.Infrastructure.Admin`, uses `AdminOrganizationDbContext`, and is registered in `Program.cs` alongside the other BYPASSRLS consumers. The class doc explicitly states the rationale (cross-tenant token lookup). The `InternalsVisibleTo` list in the `.csproj` properly includes only `Kartova.Api`, `Kartova.Api.IntegrationTests`, `Kartova.Organization.IntegrationTests`, and `Kartova.Organization.Infrastructure.Tests`. (`src/Modules/Organization/Kartova.Organization.Infrastructure.Admin/AcceptInvitationHandler.cs:23–32`, `.csproj:28–31`)

3. **ProblemDetails alignment is spec-correct and ADR-0091-compliant** — All four route outcomes (200 / 400 / 404 / 410 / 502) use `Results.Problem(type: ProblemTypes.*, statusCode: ...)` with canonical URIs from `ProblemTypes.cs`. The new `ProblemTypes.InvitationGone` constant is properly added to the shared type. `ProducesProblem(statusCode)` metadata is declared on both endpoints including the 502. (`src/Modules/Organization/Kartova.Organization.Infrastructure.Admin/InvitationAcceptRoutes.cs:21–31`)

4. **Frontend error-state distinction is correct** — The `AcceptInvitationPage` correctly distinguishes 404 → `invalid` state, 410 → `gone` state, and 5xx/network → `errored` state (the PR-review fix in commit `77c7e8f`). The `AcceptInvitationPage.test.tsx` covers all four context-load paths including the network-error (no `__status`) path separately from the 500 path. (`web/src/features/invitations/pages/AcceptInvitationPage.tsx:254–263`, `web/src/features/invitations/pages/__tests__/AcceptInvitationPage.test.tsx:130–143`)

5. **Architecture tests are extended for new routes** — `EndpointRouteRules.cs` pins both new anonymous endpoints (`GetInvitationAcceptContext` GET + `AcceptInvitation` POST) with correct verb and template in `ExpectedEndpoints`. The `RestVerbPolicyRules` (no PATCH) and `ContractsCoverageRules` (all Contracts DTOs carry `[ExcludeFromCodeCoverage]`) are exercised by the new DTOs. (`tests/Kartova.ArchitectureTests/EndpointRouteRules.cs:53–55`)
