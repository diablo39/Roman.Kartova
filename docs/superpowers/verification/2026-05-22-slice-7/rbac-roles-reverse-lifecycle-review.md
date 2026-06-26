# Deep Review — Slice 7: RBAC Permission Model + Reverse Lifecycle

**Branch reviewed:** slice-7/rbac-roles-and-reverse-lifecycle (HEAD 4e546e3)
**Base:** 6aaa6df (master tip)
**Status:** OPEN — pre-merge gate
**Reviewed:** 2026-05-22
**Commits:** 27
**Files changed:** 57 (+2827 / −95)

**Sources read against:**
- `docs/superpowers/specs/2026-05-22-slice-7-rbac-roles-and-reverse-lifecycle-design.md`
- `docs/superpowers/plans/2026-05-22-slice-7-rbac-roles-and-reverse-lifecycle-plan.md`
- `docs/architecture/decisions/README.md` (ADR-0008, ADR-0009, ADR-0028, ADR-0073, ADR-0082, ADR-0090, ADR-0093, ADR-0097)
- `CLAUDE.md` §Definition of Done
- `mutation-report-surviving.md` (pre-dates this branch — score 100% on prior codebase; no new report for slice 7)

---

## Overview

Slice 7 lands the granular RBAC permission model across the full stack: five-role `KartovaRolePermissions.Map` (Viewer / Member / TeamAdmin / OrgAdmin + PlatformAdmin/ServiceAccount intentionally excluded), per-permission ASP.NET policies registered via `AddKartovaPermissionPolicies`, `TenantClaimsTransformation` expansion of role claims → permission claims, `GET /organizations/me/permissions`, permission gating on all 8 catalog endpoints, two new OrgAdmin-only reverse-lifecycle endpoints (`/reactivate`, `/un-decommission`), and a SPA `usePermissions()` hook with hide-by-default UI gating across `AppLayout`, `CatalogListPage`, `ApplicationDetailPage`, and `LifecycleMenu`. Closes E-01.F-04.S-03 + S-04; closes the backward-transitions half of slice-5 §13.6.

---

## Blocking-class issues

None.

---

## Should-fix issues

**1. Three raw-`fetch` mutation hooks carry a self-referencing TODO for the current slice that will age into noise.**

- **Evidence:** `web/src/shared/auth/usePermissions.ts:20`, `web/src/features/catalog/api/applications.ts:198`, `web/src/features/catalog/api/applications.ts:234`
- **Impact:** The TODOs say `TODO(slice-7): migrate to apiClient.GET/POST once the Docker-running API container is rebuilt… and pnpm codegen picks up the new endpoint`. The slice is about to merge; after merge the container will be rebuilt in normal workflow, making the qualifier stale. Future devs reading the code will not know whether the condition has been satisfied or is still pending. The TODO was valid during implementation but survives into the merged product.
- **Fix:** Before merge (or in the first follow-up commit), remove the conditional prose (`once the Docker-running API container is rebuilt with slice-7 changes and pnpm codegen picks up the new endpoint`) from each TODO comment. The TODO itself — migrate to typed `apiClient` — is a valid follow-up; keep that. Replace with: `// TODO: migrate to typed apiClient.GET/POST once pnpm codegen picks up the new endpoints (slice 7 residual).`
- **ADR ref:** Not an ADR violation; a code hygiene finding.

**2. `CatalogListPage` holds a stale `dialogOpen` boolean when `canRegister` flips to false.**

- **Evidence:** `web/src/features/catalog/pages/CatalogListPage.tsx:26,73`
- **Impact:** `dialogOpen` is a local `useState(false)`. The `RegisterApplicationDialog` only mounts when `canRegister && <RegisterApplicationDialog open={dialogOpen} .../>`. If `canRegister` becomes false while the dialog is open (token refresh mid-session, permissions query stale-refetch), the dialog component unmounts without the `open` prop being cleared. The next time `canRegister` becomes true (unlikely in one session but possible after a token refresh that upgrades permissions), the dialog would re-mount with `open={true}`. This is a latent race, not an active bug — normal usage never triggers it.
- **Fix:** Add a `useEffect` that resets `dialogOpen` when `canRegister` becomes false:
  ```ts
  useEffect(() => {
    if (!canRegister) setDialogOpen(false);
  }, [canRegister]);
  ```
  Alternatively, derive `dialogOpen` as `dialogOpen && canRegister` when passing `open` to the dialog.

---

## Nits

**N1. `AddAuthorization()` + `AddAuthorizationBuilder()` are called sequentially on lines 52–53 of `JwtAuthenticationExtensions.cs`.**

- **Evidence:** `src/Kartova.SharedKernel.AspNetCore/JwtAuthenticationExtensions.cs:52-53`
- `AddAuthorization()` and `AddAuthorizationBuilder()` both register the same core authorization services idempotently; calling both is harmless (ASP.NET Core TryAdd-guards) but redundant. The `AddAuthorizationBuilder()` call subsumes the `AddAuthorization()` call.
- **Fix:** Remove the standalone `services.AddAuthorization();` on line 52; keep only `services.AddAuthorizationBuilder().AddKartovaPermissionPolicies();`.

**N2. Plan code snippet used the non-existent `BindingFlags.FlatHierarchy`; the real file correctly uses `FlattenHierarchy`.**

- **Evidence:** `docs/superpowers/plans/2026-05-22-slice-7-rbac-roles-and-reverse-lifecycle-plan.md` Task 3 Step 1 code snippet uses `BindingFlags.FlatHierarchy` (typo). The committed arch test `tests/Kartova.ArchitectureTests/KartovaPermissionsRules.cs:15,63` and `KeycloakRealmSeedRules.cs:138` correctly use `BindingFlags.FlattenHierarchy`.
- **Impact:** None in runtime; plan doc only. The implementation corrected the typo.
- **Fix:** No action required on the code; the plan document is historical. Note for future plan readers: use `FlattenHierarchy`.

**N3. `NoAccessPage` has no test coverage and is not listed in the spec's §10 test inventory.**

- **Evidence:** `web/src/components/layout/NoAccessPage.tsx` (new file); `AppLayout.test.tsx` tests the gating wrapper but not the `NoAccessPage` component itself (content rendering, accessible heading, link presence).
- **Impact:** Low — the component is 12 lines with no logic. Missing coverage is cosmetic.
- **Fix:** Add one Vitest rendering test asserting the "No access" heading and the "Contact your organization admin" copy. Can land in the same file as `AppLayout.test.tsx`.

**N4. Spec §5.4 describes the endpoint URL as `/api/v1/organizations/me/permissions`; the `OrganizationModule.cs` slug is `organizations` and the route is `/me/permissions`, producing the correct final URL. However, the spec originally read `/api/v1/organization/me/permissions` (singular) before the fix-up commit `4e546e3` corrected the docs. Residual singular references may survive in internal documentation comments.**

- **Evidence:** `src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationModule.cs:48` — URL is correct (`organizations/me/permissions`). Docs were corrected in `4e546e3`. No residual code impact.
- **Fix:** No action required; note for future readers that the canonical URL is plural `/organizations/`.

**N5. `UnDecommissionApplicationTests.cs` re-declares `ProblemPayload` as a private nested class; identical private nested classes also exist in `ReactivateApplicationTests.cs`, `DeprecateApplicationTests.cs`, and `DecommissionApplicationTests.cs`.**

- **Evidence:** `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/UnDecommissionApplicationTests.cs:152` and `ReactivateApplicationTests.cs:118` both declare `private sealed class ProblemPayload`.
- **Impact:** None at runtime; test duplication only. The duplication was pre-existing in prior lifecycle test classes.
- **Fix:** Extract `ProblemPayload` to a shared `CatalogIntegrationTestBase` or a `TestHelpers` file. This is a follow-up refactor, not a blocker.

---

## Missing tests

**MT1. No unit test asserts that `Application.Reactivate()` leaves `SunsetDate = null` when transitioning from `Deprecated` (SunsetDate was non-null).**

- **Acceptance criterion:** Spec §3 Decision #8 — "Reactivate clears SunsetDate."
- **Evidence:** `src/Modules/Catalog/Kartova.Catalog.Tests/ApplicationReactivateTests.cs:32-45` — `Reactivate_from_Deprecated_returns_to_Active_and_clears_sunset_date` does assert `Assert.IsNull(app.SunsetDate)`. This criterion IS covered.
- **Resolution:** No action needed — this was a false alarm from initial scan.

**MT2. No unit test for `Reactivate` called on an application whose `Lifecycle` is `Active` (transition from Active → *, source not in allowed set).**

- **Evidence:** `src/Modules/Catalog/Kartova.Catalog.Tests/ApplicationReactivateTests.cs:58-65` — `Reactivate_from_Active_throws_InvalidLifecycleTransitionException` IS present.
- **Resolution:** No action needed.

**MT3. `CatalogPermissionMatrixTests` covers 4 roles × 8 endpoints = 32 cells in a single test method. If one cell fails, the test stops and later cells are not reported.**

- **Evidence:** `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/CatalogPermissionMatrixTests.cs:51-100`
- **Impact:** A failing matrix cell masks other failures; debugging requires a second run. The spec §10 documents this as data-driven. The current structure is a valid tradeoff for a 32-cell matrix but `Assert.AreEqual` inside a foreach stops at first failure.
- **Fix (should-fix):** Consider converting the inner loops to a `[DataTestMethod]` + `[DataRow]` structure (MSTest v4 native, per ADR-0097) so each cell is an independent test result. Alternatively, collect all failures and `Assert.Fail` once at the end with the full list.

**MT4. `usePermissions` Vitest does not cover the case where the user is authenticated but `auth.user.access_token` is `undefined` (e.g., initial PKCE flow before token is set).**

- **Evidence:** `web/src/shared/auth/__tests__/usePermissions.test.tsx` — all tests mock `user: { access_token: "test-token" }`. The spec §8.4 says the SPA should behave correctly while loading.
- **Fix:** Add one test where `auth.user` is null/undefined but `isAuthenticated` is still true (edge case at PKCE callback); assert `hasPermission` returns false for all permissions and `isLoading` is true (query is `enabled: false` or pending).

---

## What looks good

1. **Single source of truth is faithfully maintained end-to-end.** `KartovaPermissions.All` → `KartovaRolePermissions.Map` → C# arch test → committed JSON snapshot → TypeScript constants → runtime drift guard in `permissions.ts:17-22` (`throw new Error(...)` if sizes diverge). The four-layer sentinel chain is tight: adding a permission that escapes any one of these checkpoints fails CI before merge.

2. **Permission expansion stays off the JWT wire and out of KeyCloak config.** `TenantClaimsTransformation.cs:43-59` expands permissions in-memory at validation time. The JWT stays role-only; the full permission set is derived server-side. The design comment in spec §3 Decision #4 ("when permissions become per-entity, the expansion logic stays in C# and the JWT shape doesn't change") is correctly anticipated in the implementation.

3. **`UnDecommissionApplicationTests.cs` drives the application through the full state machine** — `Active → Deprecated (near-future sunset) → Decommissioned (Task.Delay past sunset) → UnDecommission` — with actual wall-clock time progression (`Task.Delay(2000)`). This is thorough for an integration test and avoids false confidence from mocked clock-only assertions.

4. **`LifecycleMenu` component redesign is clean.** The old sentinel `if (lifecycle === "decommissioned") return <badge only>` guard was removed in favor of `if (items.length === 0) return <badge only>`, which is the correct general form: any combination of `canForward`/`canReverse`/lifecycle that yields no items renders the badge. The `buildItems` function is pure and its new `canForward`/`canReverse` parameters are tested in `LifecycleMenu.test.tsx` with six dedicated test cases across state × permission combinations.

5. **The `AddKartovaPermissionPolicies` loop correctly avoids the classic lambda-closure-in-foreach capture bug.** `foreach (var perm in KartovaPermissions.All)` in `AuthorizationExtensions.cs:14-17` — in C# 5+ each iteration variable is a fresh binding, and `KartovaPermissions.All` is `IReadOnlyCollection<string>` (string = value type interned). The `p => p.RequireClaim(KartovaClaims.Permission, perm)` lambda captures the correct iteration value. No bug.

---

## Spec deviation check

| Spec section | Implemented | Notes |
|---|---|---|
| §3 Decision #1 — 5 realm roles, ServiceAccount C#-only | Yes | `KartovaRoles.cs`, realm JSON |
| §3 Decision #2 — Single source of truth: `KartovaRolePermissions.Map` | Yes | `KartovaRolePermissions.cs` |
| §3 Decision #3 — Policy name == permission name | Yes | `AuthorizationExtensions.cs:16` |
| §3 Decision #4 — Expansion in `TenantClaimsTransformation` | Yes | `TenantClaimsTransformation.cs:52-58` |
| §3 Decision #5 — SPA reads from `/me/permissions`, mirrors string constants only, JSON snapshot | Yes | `usePermissions.ts`, `permissions.ts`, `permissions.snapshot.json` |
| §3 Decision #6 — TeamAdmin = Member in slice 7 | Yes | `KartovaRolePermissions.cs:21-28` |
| §3 Decision #7 — Two reverse endpoints, endpoint-per-transition | Yes | `CatalogModule.cs` |
| §3 Decision #8 — Reactivate clears SunsetDate; UnDecommission requires future date | Yes | `Application.cs:133-134, 150-156` |
| §3 Decision #10 — Sunset override NOT in scope | Yes — forward endpoints unchanged | `CatalogModule.cs` |
| §3 Decision #11 — PlatformAdmin stays on RequireRole | Yes | `OrganizationModule.cs:51-52` |
| §5.6 — ADR-0073 addendum | Yes | `ADR-0073-enforced-entity-lifecycle-states.md` |
| §8.4 — Hide-by-default; toast on 403 | Yes | `AppLayout.tsx`, `CatalogListPage.tsx`, `ApplicationDetailPage.tsx`; `ReactivateConfirmDialog.tsx:48-56` |
| §10.2 — Drift sentinel arch test | Yes | `KartovaPermissionsRules.cs:57-70` |
| CHECKLIST.md — E-01.F-04.S-03, S-04 marked done | Yes | `CHECKLIST.md` |

No silent deviations found.

---

## Definition of Done gate audit

| Gate | Evidence | Status |
|---|---|---|
| 1. Build 0 warnings/errors | `TreatWarningsAsErrors=true` — per commit history, each commit was built as part of CI; no build failures in branch | Pending user citation |
| 2. Per-task subagent reviews | Commit history shows per-task review commits | Cited in branch |
| 3. `/superpowers:requesting-code-review` | Commit `4e546e3` ("fix: address requesting-code-review findings") | Done |
| 4. Full test suite green | Not independently citable from diff alone | Pending user citation |
| 5. `docker compose up` + real HTTP per-role | Not citable from diff | Pending user citation |
| 6. `/simplify` | Commit `794f2f4` ("refactor: address simplify findings") | Done |
| 7. Mutation feedback loop | `mutation-report-surviving.md` predates this branch (2026-05-09, score 100%). No new Stryker run for slice 7 files cited | **Gap** — new files not covered |
| 8. `/pr-review-toolkit:review-pr` | Not cited in commit history visible here | Pending user citation |
| 9. `/deep-review` | This report | Done |

**DoD Gate 7 note:** The mutation report on disk (`mutation-report-surviving.md`, 2026-05-09) predates the slice 7 changes. New logic in `Application.Reactivate`, `Application.UnDecommission`, `TenantClaimsTransformation` permission expansion, and `KartovaRolePermissions.ForRole` was not covered by that run. Per CLAUDE.md §DoD gate 7, a Stryker run against the changed files with score ≥80% must be cited before this gate is green.
