# Slice 9 — Deep PR Review Rerun (DoD #9)

**Target:** `feat/slice-9-organization-people-management` (HEAD `3a67b95`) vs `master` (`bac512d`)
**Status:** OPEN — pre-merge gate
**Diff scope:** 277 files, +21,933 / −709 LOC, 123 commits ahead of master
**Spec:** `docs/superpowers/specs/2026-05-27-slice-9-organization-people-management-design.md` (on master at `bac512d` — branch tip carries the pre-reconciliation copy; addressed below)
**Plan:** `docs/superpowers/plans/2026-05-27-slice-9-organization-people-management-plan.md` (on master at `bac512d`)
**ADRs touched:** 0011, 0080, 0083, 0084, 0090, 0092, 0095, 0096, 0097, 0098, 0100
**Prior review:** `docs/superpowers/reviews/2026-05-29-slice-9-deep-review.md` at HEAD `f178520`. The 2 prior Blocking findings (DoD #4 full-suite evidence, DoD #7 mutation breadth) and the prior S1 + G2 items have since been addressed.

This rerun scopes findings exclusively to delta introduced by the 6 new commits since the prior review: `e8bf859`, `a8fd443`, `fc86775`, `a6b8fde`, `ffc956c`, `3a67b95`.

---

## Overview

The 6 new commits collapse the `IPostAuthSyncHook` pipeline + `JustAcceptedInvitationId` shared-state machinery into a single inline `SessionStartHandler.HandleAsync` flow, close S4-S6 production findings (OrgLogo defensive clone, OrganizationModule per-resource route extraction, cursor codec `ownerUserId` propagation + UNIQUE `ix_users_tenant_email` DB-level closure of ADR-0100, generic `DbUpdateException` compensation for `CreateInvitationHandler`), and add 8 missing wire-tier tests (MT1, MT2, MT3, MT4, MT5, MT7 — two MT7 boundary tests). Net deletion of ~80 LOC of cross-module pipeline plumbing; net addition of ~700 LOC of test surface + per-resource route composition.

---

## Blocking-class issues

None.

The two prior-review Blocking findings (DoD #4 full-suite evidence, DoD #7 mutation-breadth) are not in the delta this rerun examines — they remain outside the 6-commit scope and (per the prompt's "the 2 Blocking findings ... have since been resolved by 6 new commits") are considered resolved upstream. The 6 commits themselves introduce no new Definition-of-Done gate failures.

---

## Should-fix issues

### S1. ADR-0100 23505 catch path has zero test coverage

**Evidence:** `src/Modules/Organization/Kartova.Organization.Infrastructure/UserProjectionUpdater.cs:48-68` (commit `a8fd443`) — the `catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == "23505")` branch logs at Error and throws `OneEmailPerTenantViolationException`. `tests/Kartova.Organization.Infrastructure.Tests/UserProjectionUpdaterTests.cs` has 3 happy-path tests against the EF InMemory provider (which does NOT enforce the functional `(tenant_id, lower(email))` UNIQUE constraint) and zero tests for the 23505 branch. `git grep "OneEmailPerTenantViolationException" -- tests` returns no hits.

**Impact:** The deep-review's S2 promised closure ("update the upsert handler to surface a typed error on 23505") is in production code but is unverified end-to-end. Any future refactor that drops the `when` clause, swaps the SqlState constant, or breaks the `OneEmailPerTenantViolationException` constructor signature would pass CI. The migration commentary in `OneEmailPerTenantViolationException.cs:9` even mis-describes the new index as "partial UNIQUE" — a fact that test cover would have caught at read-time.

**Fix:** Add an integration test in `src/Modules/Organization/Kartova.Organization.IntegrationTests/UserProjectionUpdaterIntegrationTests.cs` (new file) that uses the real Postgres Testcontainers fixture, pre-inserts a `users` row via BYPASSRLS with `email = 'alice@example.com'`, then calls `UpsertAsync` with a different `userId` + `email = 'Alice@Example.com'` (uppercase variant the case-sensitive `ux_users_tenant_email` would miss). Assert `OneEmailPerTenantViolationException` is thrown AND the exception's `TenantId` + `Email` properties match. Naming convention to match the slice-9 standard: `Upsert_throws_OneEmailPerTenantViolationException_on_case_insensitive_collision`.

### S2. Spec-vs-code drift on branch HEAD (spec §9.1 + §9.4 + §4.3)

**Evidence:** `git show HEAD:docs/superpowers/specs/2026-05-27-slice-9-organization-people-management-design.md` at lines 228, 760-790, 851-862 carries the pre-reconciliation text — describes a `TenantClaimsTransformation`-driven upsert, an `IPostAuthSyncHook`, and an `ICurrentUser.JustAcceptedInvitationId` shared property. None of these exist in the branch's production code anymore (all deleted by `e8bf859`). Master's `bac512d` reconciled the spec; the branch has not pulled that commit forward.

**Impact:** A reviewer reading the branch's own spec against the branch's own code sees contradictions: spec says "every authenticated request via `TenantClaimsTransformation`", code says "once per `POST /auth/session` via `SessionStartHandler`"; spec defines `ICurrentUser.JustAcceptedInvitationId`, code has no such property. The mismatch only resolves on merge (master's `bac512d` lands together with the branch's code). Until then, anyone checking out the branch sees stale design docs.

**Fix:** Either (a) rebase the branch onto master so `bac512d` becomes an ancestor of HEAD, OR (b) cherry-pick `bac512d` onto the branch as a tail commit. The former is preferred because the spec reconciliation is a logical prerequisite of the `e8bf859` refactor.

### S3. `OneEmailPerTenantViolationException` + `UserProjectionUpdater` comments mis-describe the new index as "partial UNIQUE"

**Evidence:**
- `src/Modules/Organization/Kartova.Organization.Domain/OneEmailPerTenantViolationException.cs:9` — "partial UNIQUE index `ix_users_tenant_email`"
- `src/Modules/Organization/Kartova.Organization.Infrastructure/UserProjectionUpdater.cs:54` — "the new partial UNIQUE index ix_users_tenant_email"

The actual migration (`src/Modules/Organization/Kartova.Organization.Infrastructure/Migrations/20260531200125_AddUsersTenantEmailUnique.cs:33-41`) creates a **functional** UNIQUE index (`CREATE UNIQUE INDEX ix_users_tenant_email ON users (tenant_id, lower(email))`) — there is no `WHERE` clause, so it is NOT partial. Compare to `idx_invitations_email_pending` which IS a true partial index (`WHERE status = 0`).

**Impact:** Documentation hazard — a future reader investigating the index will look for the `WHERE` clause that doesn't exist. The terminology overlap with the genuinely-partial `idx_invitations_email_pending` makes this drift especially confusing.

**Fix:** Replace "partial UNIQUE index" with "functional UNIQUE index" in both files (the index uses `lower(email)` which IS a functional expression). Single-line edit in two places.

### S4. `EndpointRouteRules` inventory still pins only 9 named routes; the `fc86775` refactor moved 22 organization-module routes without expanding the inventory

**Evidence:** `tests/Kartova.ArchitectureTests/EndpointRouteRules.cs:36-51` — the `ExpectedEndpoints` array contains 9 entries: 6 Catalog routes, 2 Organization routes (`GetOrganizationMe`, `GetOrganizationMeAdminOnly`), and 1 Admin route. Slice 9 + slice 8 collectively shipped ~20 additional Organization routes (`/teams/*`, `/users/*`, `/invitations/*`, `/me/logo`, `/me/permissions`, `/auth/session`) — none are pinned by name+verb+template. The `fc86775` extraction moved all of these from `OrganizationModule.MapEndpoints` into per-resource `*Routes.MapTo` extension methods.

**Impact:** Pre-existing gap (slice 8 also failed to extend the inventory). But the 22-route move in `fc86775` raises the consequence: a future drop of e.g. `tenant.MapPut("/me/logo", ...)` from `OrganizationProfileRoutes.MapTo` survives the arch suite (the "every endpoint has a name" guard still passes — there's just one fewer named endpoint). The slice-9 boundary review will not catch this either because no diff against the inventory exists.

**Fix:** Extend `ExpectedEndpoints` with the slice-9 Organization routes (8 from `OrganizationProfileRoutes.MapTo`, 7 from `TeamRoutes.MapTo`, 3 from `InvitationRoutes.MapTo`, 2 from `UserRoutes.MapTo`, 1 from `AuthRoutes.MapTo`). Names + verbs + templates are all in the per-resource `*Routes.MapTo` source files — straightforward 22-entry addition. Defer to slice-10 entry if H6 PR scope is already large; track as a follow-up tied to `EndpointRouteRules`.

### S5. `OneEmailPerTenantViolationException` is decorated `[ExcludeFromCodeCoverage]` despite carrying message-building logic

**Evidence:** `src/Modules/Organization/Kartova.Organization.Domain/OneEmailPerTenantViolationException.cs:21` — `[ExcludeFromCodeCoverage]` on a class that has a `BuildMessage` static helper with interpolated diagnostics. The CLAUDE.md rule is `[ExcludeFromCodeCoverage]` for "every type in a `*.Contracts` assembly and every `*Dto`/`*Request`/`*Response` type in production code MUST carry [ExcludeFromCodeCoverage]. Pure data carriers ... also excluded." A typed exception with a `BuildMessage` helper is borderline — not strictly a pure data carrier.

**Impact:** Low — the exception is on the failure-path that doesn't currently have ANY tests (see S1), so the coverage attribute is masking the missing coverage rather than legitimately suppressing it. Once S1 lands, the attribute should be reconsidered: if the integration test asserts the message format, removing `[ExcludeFromCodeCoverage]` keeps the assertion meaningful.

**Fix:** Remove `[ExcludeFromCodeCoverage]` from `OneEmailPerTenantViolationException`. Pair with the S1 test that asserts `ex.Message` contains the tenant + email values — that gives genuine coverage rather than gaming the metric.

---

## Nits

### N1. `MapTo` extension methods are named `*Routes`, breaking the singular `Module` precedent

**Evidence:** `OrganizationProfileRoutes` / `TeamRoutes` / `InvitationRoutes` / `UserRoutes` / `AuthRoutes` (commit `fc86775`). Catalog's parallel uses `CatalogEndpointDelegates` (per-resource within one class) — there's no precedent. The pluralization (`Routes` not `RouteComposition`) is fine, but the colocated `*EndpointDelegates` host class in the same file makes the file name slightly ambiguous (`OrganizationProfileEndpointDelegates.cs` contains both `OrganizationProfileEndpointDelegates` AND `OrganizationProfileRoutes`). Discoverability is unchanged in practice — both types are referenced from `OrganizationModule.MapEndpoints` — but the file-name-doesn't-match-all-types pattern is new.

### N2. `AuthRoutes.MapTo` takes `IEndpointRouteBuilder` while sibling `*Routes.MapTo` take `RouteGroupBuilder` — type asymmetry not documented at the call site

**Evidence:** `src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationModule.cs:56-62` — `OrganizationProfileRoutes.MapTo(tenant)`, `TeamRoutes.MapTo(tenant)`, etc. take the `RouteGroupBuilder` from `MapTenantScopedModule`, but `AuthRoutes.MapTo(app)` takes the raw `IEndpointRouteBuilder`. The reason is correct (auth lives outside `/api/v1/organizations`) and IS documented in the XML doc on `OrganizationModule.MapEndpoints`, but the call site reads as if the parameter type was a typo. Consider adding an inline `// raw builder — /api/v1/auth lives outside the tenant slug` on line 62.

### N3. `SessionStartHandler.HandleAsync` `email` claim lookup is case-sensitive on the claim key

**Evidence:** `SessionStartHandler.cs:69-71` — `principal.FindFirst("email")?.Value ?? principal.FindFirst(ClaimTypes.Email)?.Value`. Both `FindFirst` calls are case-sensitive on the claim type. If KC ever emits the claim as `"Email"` (capitalized) or as `"http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress"` with a casing variation, both lookups miss and the handler throws `InvalidOperationException` → 500. Defense in depth would be `.FindAll(c => c.Type.Equals("email", StringComparison.OrdinalIgnoreCase) || c.Type == ClaimTypes.Email).FirstOrDefault()`. Low risk — KC's well-known JWT claim names are stable — but worth documenting the assumption.

### N4. `OrganizationModule.cs:127-132` DI registration comment for `UserProjectionUpdater` says "no separate per-request pipeline hook is needed" — stale framing

**Evidence:** Line 130: "no separate per-request pipeline hook is needed". The framing is correct for the post-`e8bf859` design, but the wording is defensive against the OLD design that DID have such a hook. New readers without slice-9 context won't know what hook was being defended against. Consider replacing with a forward-looking statement: "Invoked exclusively by `SessionStartHandler` — the SPA's `OidcCallbackHandler` always hits `/auth/session` first after the KC roundtrip."

### N5. `OrganizationModule.MapEndpoints` XML doc references "slice-9 carry-forward S6" — the carry-forward ledger is internal

**Evidence:** `OrganizationModule.cs:47` — `(slice-9 carry-forward S6 — extends the H5 R2 per-resource delegate split to the route registrations too)`. Future readers without the slice-9 review history won't be able to map "S6" to anything. Consider rewording to a self-contained description (the rationale paragraph is already complete; the S6 tag is just history that belongs in the commit message, not the production XML doc).

---

## Missing tests

### MT-A. `UserProjectionUpdater.UpsertAsync` 23505 case-insensitive collision (= S1 above)
- **Acceptance:** ADR-0100 enforcement at the DB level on case-insensitive email collision via `ix_users_tenant_email`.
- **Test that should exist:** Project: `Kartova.Organization.IntegrationTests`. Class: `UserProjectionUpdaterIntegrationTests` (new). Scenario: BYPASSRLS-insert `users (tenant, 'alice@example.com', userId-A)`, then call `UpsertAsync(userId-B, 'Alice@Example.com', tenant)`. Assert `OneEmailPerTenantViolationException` thrown + `ex.TenantId == tenant.Value` + `ex.Email == "Alice@Example.com"`.

### MT-B. `CreateInvitationHandler` compensation cleanup-failure swallow on the `DbUpdateException` (non-23505) branch
- **Acceptance:** The new compensation path in commit `a8fd443` (`CreateInvitationHandler.cs:144-152`) catches the secondary `kc.DeleteUserAsync` failure and logs at Warning, allowing the original `DbUpdateException` to propagate. The role-assign-cleanup-failure path is tested (`Compensation_swallows_secondary_KC_delete_failure`) — the symmetric DB-cleanup-failure path is not.
- **Test that should exist:** Project: `Kartova.Organization.Infrastructure.Tests`. Class: `CreateInvitationHandlerTests`. Scenario: Configure NSubstitute to throw a generic `DbUpdateException` on `SaveChangesAsync` AND throw `KeycloakAdminException` on `DeleteUserAsync`. Assert the original `DbUpdateException` propagates (not the KC one) AND `kc.Received(1).DeleteUserAsync(...)` was called. Mirrors `SaveChangesAsync_DbUpdateException_triggers_KC_user_cleanup` plus the secondary-failure swallow.

### MT-C. `OrgLogo.Bytes` defensive clone — assertion that EF materialization still works
- **Acceptance:** `a6b8fde` commit message claims "EF reads the logo_bytes column directly in the GetServeDataAsync projection ... the per-read clone is invisible to the EF comparer". The unit test `Bytes_returned_array_is_a_defensive_clone` pins the property-getter behavior but does NOT pin the EF integration. The existing JPEG-upload integration test (`Logo_upload_with_jpeg_returns_200_and_serve_returns_correct_bytes_and_etag`) exercises the full path implicitly, but a dedicated assertion that EF change-tracking does not falsely flag the entity as modified would future-proof the invariant against EF version upgrades.
- **Test that should exist:** Project: `Kartova.Organization.Infrastructure.Tests`. Class: `OrgLogoEfMaterializationTests` (new). Scenario: Use InMemory provider + tracking enabled; load an Organization with a Logo, call `org.Logo.Bytes` twice in a row, then assert `db.ChangeTracker.HasChanges() == false`. Pins the "per-read clone is invisible to the EF comparer" claim.

### MT-D. Cursor `ownerUserId` mismatch path at the integration tier (carry-forward S5 / item 17)
- **Acceptance:** `QueryablePagingExtensions.cs:90-96` throws `CursorFilterMismatchException` if a cursor was issued with `ownerUserId=X` and a subsequent page request omits or changes the filter. Unit-tier coverage exists in `CursorCodecTests`; integration-tier (full HTTP wire) does not.
- **Test that should exist:** Project: `Kartova.Catalog.IntegrationTests`. Class: `ListApplicationsTests` (existing). Scenario: Seed >limit applications with two distinct owners. Page 1 with `?ownerUserId={ownerA}&limit=2` returns `nextCursor`. Page 2 with the returned `nextCursor` but `ownerUserId={ownerB}` (or omitted) must return 400 + `ProblemTypes.InvalidCursor` (or whatever the `CursorFilterMismatchException` maps to). Mirrors the existing `includeDecommissioned` mismatch coverage if present.

---

## What looks good

### G1. The `IPostAuthSyncHook` deletion is a textbook YAGNI reversal — net subtractive refactor with no behavioral regression

**Files:** `src/Kartova.SharedKernel.AspNetCore/IPostAuthSyncHook.cs` (deleted), `src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationPostAuthSyncHook.cs` (deleted), `src/Kartova.SharedKernel.Multitenancy/ITenantContext.cs` (`SetJustAcceptedInvitation` removed), `src/Kartova.SharedKernel.AspNetCore/ICurrentUser.cs` (`JustAcceptedInvitationId` removed). Commit `e8bf859` deletes 711 lines of pipeline plumbing and adds 359 lines (a 352-line net deletion) while leaving wire behavior unchanged. The commit message names the architectural insight cleanly (Volue Identity static-`redirect_uri` guarantees `OidcCallbackHandler` is the first authenticated code path) and the test surface contracted accordingly — 5 test files deleted, `SessionStartHandlerTests.cs` rewritten from 138 LOC of mock-the-hook scenarios to 304 LOC of direct end-to-end scenarios. This is the right direction of motion: when a piece of infrastructure exists to defend against a scenario that can't happen, deleting it is the correct refactor.

### G2. `OrganizationModule.MapEndpoints` per-resource extraction preserved every route's permission + Produces<> annotations verbatim

**File:** `src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationModule.cs:55-63` and the 5 `*Routes.MapTo` extensions. Diff comparison (`git diff fc86775~1..fc86775 -- *.cs`) confirms 22 routes moved one-to-one: every `RequireAuthorization(KartovaPermissions.X)`, every `Produces<T>`, every `ProducesProblem(StatusCodes.Status4xxY)` is preserved. The pattern uniformly threads `RouteGroupBuilder tenant` through 4 of 5 extensions and exceptionally takes `IEndpointRouteBuilder` for `AuthRoutes` (correctly — `/api/v1/auth/session` lives outside the `/api/v1/organizations` slug). The route registrations now colocate with their delegate definitions in the same `*EndpointDelegates.cs` file, which is the right move for navigation. The pre-existing weak `EndpointRouteRules` inventory (see S4) is the only reason this isn't pinned by an arch test — the refactor itself is exemplary.

### G3. The `OrgLogo.Bytes` defensive-clone test has three independent assertions that kill three distinct mutants

**File:** `src/Modules/Organization/Kartova.Organization.Tests/OrgLogoTests.cs:60-86`. The `Bytes_returned_array_is_a_defensive_clone` test asserts (a) `AreNotSame(firstRead, secondRead)` — kills any mutant that returns `_bytes` directly; (b) post-mutation `ContentHash` unchanged — kills any mutant that aliases `_bytes` to the returned reference; (c) `thirdRead` returns the original bytes — kills any mutant that mutates `_bytes` during `Create`. This is the rare MC/DC-aware test design: each assertion fails independently for a distinct mutation, so the surviving-mutant cluster is genuinely empty.

### G4. The `CursorCodec` + `QueryablePagingExtensions` extension to `ownerUserId` is symmetric to the existing `includeDecommissioned` precedent

**Files:** `src/Kartova.SharedKernel/Pagination/CursorCodec.cs:32-49` (encode) + `:90-105` (decode) + `src/Kartova.SharedKernel.Postgres/Pagination/QueryablePagingExtensions.cs:48-96` (replay + mismatch). The new `ou` JSON field follows the same shape as `ic`: optional, default-null, omitted from wire when default. The mismatch detection uses `Nullable.Equals(decoded.OwnerUserId, expectedOwnerUserId)` which gives correct semantics across all four `(null,null)/(null,X)/(X,null)/(X,Y)` cases — and the inline comment names exactly why that matters. Forward-compatible (legacy cursors decode as `OwnerUserId = null` and any caller that doesn't opt in is unaffected) and backward-compatible.

### G5. `MT4` (the broaden-the-catch ExpireDueAsync test) directly attacks the mutation surface the prior review identified

**File:** `src/Modules/Organization/Kartova.Organization.Infrastructure.Tests/ExpireInvitationsHostedServiceTests.cs:162-225`. The test seeds two due invitations, throws `KeycloakAdminException(Unexpected)` on the first KC delete, and asserts (a) the exception propagates (not swallowed), (b) `kc.Received(1)` on the first id, `DidNotReceive` on the second (loop aborted), AND (c) **both rows still Pending** in the DB — proving no partial commit. The triple-assertion pattern kills the entire mutation cluster around the catch-clause boundary: a mutant that swallows the non-NotFound exception fails (a), a mutant that processes the second row before throwing fails (b), a mutant that moves `SaveChangesAsync` inside the loop fails (c). This is the model template for boundary-condition test design under the mutation feedback loop.

---

## DoD evidence trail (delta only)

| Gate | Status | Evidence |
|------|--------|----------|
| #1 Build green | Unverified for delta | Per-commit summaries claim 0/0 warnings, but no single full-solution build at HEAD `3a67b95` is cited in the 6 commits' messages. |
| #2 Per-task subagent reviews | Implied | Commit messages don't enumerate review IDs; the slice-boundary review is what produced these 6 commits in the first place. |
| #3 `/superpowers:requesting-code-review` | Out of scope | Branch-level gate, not per-commit. |
| #4 Full test suite green | Unverified for delta | The 8 new tests in `3a67b95` + `ffc956c` + `a6b8fde` are individually green per commit messages; no full-solution `dotnet test` run is captured at HEAD. |
| #5 Docker happy + negative | Out of scope | The 6 commits don't touch the HTTP middleware/auth/Dockerfile surfaces; previously-established H3 + H4 evidence still holds. |
| #6 `/simplify` | Out of scope | Branch-level gate. |
| #7 Mutation feedback loop | Partial | Per the prompt, Stryker is currently running over `Kartova.Organization.Tests` + `Kartova.Catalog.Tests` — locked DLLs. Once it completes the surviving-mutant set should be cross-checked against the 8 new tests; in particular the `Bytes_returned_array_is_a_defensive_clone` test and the `ExpireDueAsync_propagates_non_NotFound_KC_error_and_aborts_before_save` test were authored to kill specific mutation classes and the report should confirm. |
| #8 `/pr-review-toolkit:review-pr` | Out of scope | Branch-level gate. |
| #9 `/deep-review` | This document | — |

---

## Recommended next actions

1. **Land S1 (the 23505 integration test)** — this is the deep-review-promised closure of ADR-0100. Cost is one new test class; pairs naturally with S5 (drop the `[ExcludeFromCodeCoverage]` once the test exists).
2. **Land S2 (rebase or cherry-pick `bac512d` onto the branch)** — required so the branch's spec/plan match the branch's code. Mechanical fix.
3. **Apply S3 + S5 in a single `docs(slice-9):` commit** with N1-N5 — single-screen cleanup, zero risk.
4. **Defer S4** (`EndpointRouteRules` inventory expansion) to slice-10 entry with an explicit TODO comment in `EndpointRouteRules.cs` — the refactor didn't introduce the gap and lifting it now expands H6 PR scope.
5. **Add MT-A / MT-B / MT-C / MT-D** in a single `test(slice-9):` follow-up commit on the branch before merge, or queue them as the H6-eve commit.

No new blocking-class findings. The 6 commits are net subtractive on architectural complexity and net additive on test coverage — exactly the right shape for a pre-merge cleanup batch.
