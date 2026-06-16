# Deep Review — Slice 7: RBAC Roles + Reverse Lifecycle

**Branch:** `feat/slice-7-rbac-roles-and-reverse-lifecycle`
**Date:** 2026-05-22
**Reviewer:** Claude (deep-review skill)
**Status:** OPEN — pre-merge gate

---

## Overview

Slice 7 ships the granular permission model promised by ADR-0008 and the backward lifecycle transitions promised by ADR-0073. The backend lands `KartovaPermissions` / `KartovaRolePermissions.Map` as a single source of truth, extends `TenantClaimsTransformation` to expand role claims into permission claims, gates all eight catalog endpoints by named ASP.NET policies, adds `POST /applications/{id}/reactivate` and `POST /applications/{id}/un-decommission` (OrgAdmin-only), and exposes `GET /organizations/me/permissions`. The SPA gains a `usePermissions()` hook, a drift-sentinel JSON snapshot, hide-by-default gating on four components, and two new reverse-lifecycle dialogs. Architecture, unit, and integration tests cover all eight public endpoints and the full role × endpoint matrix (4 × 8 = 32 cells).

---

## Blocking-class issues

### B-1. DoD §5 (docker compose + real HTTP smoke) and §7 (mutation feedback loop) are explicitly incomplete

**Evidence:**
- `CLAUDE.md §Definition of Done` §5 and §7.
- Confirmed in the prompt: §5 "NOT YET DONE", §7 "NOT YET DONE — mutation-report-surviving.md dated 2026-05-09, predates slice 7".

**Impact:** Authorization middleware order, `SET LOCAL` semantics, JWT issuer/audience mismatches, and per-role HTTP smoke are all unverifiable without a running container. Mutation feedback is required by DoD to reach ≥80% score on changed files.

**Fix:** Run `docker compose up --build` and execute the per-role smoke script captured in spec §11 DoD item 5 (Viewer `GET /me/permissions` → 200, Viewer `POST /applications` → 403, Member `POST /applications` → 201, OrgAdmin `/reactivate` → 200). Then run `/misc:mutation-sentinel` against changed files and `/misc:test-generator` until surviving mutants are killed or explicitly accepted. Capture and cite both outputs before merge.

---

### B-2. `usePermissions` hook silently swallows 403 without surfacing a toast — spec §8.4 behaviour gap

**Evidence:**
- `web/src/shared/auth/usePermissions.ts:39` — `if (!res.ok) throw new Error(...)` on any non-200, including 401 or 403.
- `web/src/shared/auth/__tests__/usePermissions.test.tsx:97` — only tests the `401` case and asserts `isLoading: false`, `role: null`; does not assert `isError: true` or toast.
- Spec §8.4: "On a 403 from the API (race / token drift): existing problem-details handler surfaces a toast via the existing problem-details handler. Mutation result reflects the error so the dialog stays open with inline alert."

**Impact:** If the `/me/permissions` endpoint returns 403 (e.g., token drift after a role change), the `AppLayout` fallback is `PermissionsErrorShell` (generic error screen, no toast). Spec requires a toast so the user knows a permission-related 403 happened on a mutation call. This is a missing branch in `usePermissions.test.tsx` and a missing assertion in the hook's error path.

**Fix:**
1. In `web/src/shared/auth/usePermissions.ts`, distinguish `res.status === 401` (session expired → redirect) from `res.status === 403` (permission denied → set `isError`) and surface a toast for 403.
2. Add a test case to `web/src/shared/auth/__tests__/usePermissions.test.tsx` asserting `isError: true` on a 403 response and verifying no toast fires on 401 (handled by the OIDC redirect path), toast fires on 403.

---

## Should-fix issues

### S-1. `ReactivateApplicationHandler` and `UnDecommissionApplicationHandler` reside in `Kartova.Catalog.Infrastructure` while the spec's file-structure table lists them under `Kartova.Catalog.Application`

**Evidence:**
- Spec §6.4 file table: `src/Modules/Catalog/Kartova.Catalog.Application/ReactivateApplicationHandler.cs`.
- Actual location: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/ReactivateApplicationHandler.cs` (confirmed by `ls`).

**Impact:** This is consistent with the established repo convention — all existing handlers (`DeprecateApplicationHandler`, `EditApplicationHandler`, etc.) live in Infrastructure and the plan text itself says "Mirror `DeprecateApplicationHandler` exactly". The divergence is between the spec's file table and the actual repo convention (ADR-0093 direct-dispatch). The spec table was aspirational. The placement is correct for the codebase, but the spec's file-structure section is now stale and will mislead future slice authors.

**Fix:** Add a one-sentence note to spec §6.4 (or add it as an implementation note inline in the spec file): "Handlers are placed in `Kartova.Catalog.Infrastructure`, not `Kartova.Catalog.Application`, per the established repo convention (ADR-0093 direct-dispatch). The original spec file table listed the Application project; the Infrastructure placement is correct."

---

### S-2. `CatalogPermissionMatrixTests` uses a single seeded application for all 32 cells — reverse-lifecycle cells against a non-Deprecated/Decommissioned app will return 409, not 2xx, but the assertion only checks ≠ 403

**Evidence:**
- `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/CatalogPermissionMatrixTests.cs:49-83` — seeds one `Active` application; the reactivate/un-decommission cells use the same ID for the OrgAdmin allowed-cells check, which correctly asserts `≠403` (409 is fine).
- This is documented in the plan Task 8 comment: "OrgAdmin should get either 200 or 409 depending on the seeded application's state — both are non-403 so the assertion holds."

**Impact:** The test proves authorization decisions only, not correctness of state machine transitions. This is intentional and correct, but the test's inline comment does not state this limitation. A future maintainer adding a role that is "allowed" but sends an invalid body will see confusing 4xx behavior when the assertion passes.

**Fix:** Add an inline comment above the `else` branch of the matrix assertion (`src/Modules/Catalog/Kartova.Catalog.IntegrationTests/CatalogPermissionMatrixTests.cs:76`) stating: "// Allowed means ≠ 403 and ≠ 401. The specific 2xx/4xx response (e.g. 409 for lifecycle state mismatch) is irrelevant here — per-endpoint integration tests cover response correctness."

---

### S-3. `GetMePermissions` delegate returns `null` for `Role` when the user has no recognized `ClaimTypes.Role` but the `MePermissionsResponse` DTO has `string? Role` (nullable) — no test for this null path

**Evidence:**
- `src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationEndpointDelegates.cs:32` — `FirstOrDefault()` returns `null` when no `ClaimTypes.Role` claim exists.
- `src/Modules/Organization/Kartova.Organization.Contracts/MePermissionsResponse.cs:6` — `string? Role` (nullable, correct).
- `src/Modules/Organization/Kartova.Organization.IntegrationTests/GetMePermissionsTests.cs` — all four role tests provide a valid role; no test for a token with no role claim.

**Impact:** A client using a token that somehow lacks a role claim (e.g., a PlatformAdmin token hitting the tenant-scoped endpoint) will receive `{ "role": null, "permissions": [] }` — a valid but uncovered path. The test gap is minor but observable by any API consumer.

**Fix:** Add one test to `GetMePermissionsTests` that authenticates a user whose JWT was issued without a role claim (or with `PlatformAdmin` role which maps to no catalog permissions): assert `200 OK`, `body.Role == null`, `body.Permissions` is empty.

---

### S-4. `usePermissions.ts` uses a hard-coded `staleTime: 5 * 60 * 1000` with no `refetchOnWindowFocus: false` — permission set can become stale after an admin role change

**Evidence:**
- `web/src/shared/auth/usePermissions.ts:40` — `staleTime: 5 * 60 * 1000, retry: false`.
- No `refetchOnWindowFocus` flag is set, meaning React Query's default (`refetchOnWindowFocus: true`) applies.

**Impact:** After a permission change (OrgAdmin demotes a user to Viewer), the SPA will re-fetch on next window focus and correctly update the UI. However, the 5-minute stale window means the user can still click gated actions and receive a 403 from the API for up to 5 minutes after demotion. Spec §8.4 covers the toast UX for this case. The bigger concern is that `refetchOnWindowFocus: true` is the correct default here — this is actually the right behavior. No change needed on the refetch flag. The 5-minute stale window is a product decision and is acceptable.

**Fix (documentation only):** Add a comment to `usePermissions.ts:39` noting: "// staleTime: 5 minutes — role changes propagate on next window focus. 403 on gated actions surfaces a toast (spec §8.4)." This makes the intentional tradeoff visible.

---

### S-5. `DeprecateConfirmDialog` refactored during this slice — its test file was not updated to cover the extracted `sunsetDateField` schema

**Evidence:**
- `web/src/features/catalog/schemas/deprecateApplication.ts` was modified in this branch (28 insertions, 2 deletions per `--stat`).
- `web/src/features/catalog/schemas/sunsetDateField.ts` is a new file extracted from the deprecate schema.
- No new unit test for `sunsetDateField` alone (the schema logic — valid future date, invalid past date, required field) is visible in the diff.

**Impact:** The future-only validation in `sunsetDateField.ts` is now shared between `deprecateApplication.ts` and `unDecommissionApplication.ts`. If the boundary behavior (`Date.now()` boundary, empty string, invalid date string) is only tested indirectly through dialog tests, a refactor of the shared schema can break both dialogs silently.

**Fix:** Add `web/src/features/catalog/schemas/__tests__/sunsetDateField.test.ts` with unit tests: valid future ISO string → passes, past ISO string → fails with "Sunset date must be in the future.", empty string → fails with "Sunset date is required.", invalid string → fails. Six test cases, one file.

---

## Nits

### N-1. `ReactivateConfirmDialog` — the JSX `aria-label` uses a capital "A" in "Application" inconsistently with the rest of the dialog suite

**Evidence:** `web/src/features/catalog/components/ReactivateConfirmDialog.tsx:54` — `aria-label="Reactivate Application"`. Other dialogs use sentence case for aria-labels; this uses title case.

**Fix:** Change to `aria-label="Reactivate application"` (lowercase "a").

---

### N-2. `CatalogPermissionMatrixTests.cs` duplicates the `AttachShapeValidBody` helper that already partially exists in the plan's draft — the `decommission`-path has no body attached but could silently mask a shape-validation error on that endpoint

**Evidence:** `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/CatalogPermissionMatrixTests.cs:101` — the `/decommission` path is implicitly left with `null` body (no `Content-Type`, empty body), which is correct for that endpoint. The comment states "GET methods and /decommission have no body." This is correct — nit only on readability.

**Fix:** Add an explicit comment beside the `else if` chain: `// /decommission takes no body (the domain invariant "now >= sunsetDate" is checked at handler time, not at binding).`

---

### N-3. `permissions.ts` — the runtime drift guard executes at module-load time and throws a hard `Error` if drift is detected, which would crash the SPA in production if the snapshot is accidentally stale

**Evidence:** `web/src/shared/auth/permissions.ts:17-22` — `if (declared.size !== fromSnapshot.size ...) { throw new Error(...) }`.

**Fix:** In production builds, replace the hard `throw` with `console.error(...)` so a stale snapshot degrades gracefully (wrong permissions visible, but no SPA crash). Reserve the hard throw for test/dev environments. Alternatively, add a `import.meta.env.DEV` guard: `if (import.meta.env.DEV && (declared.size !== fromSnapshot.size ...)) { throw new Error(...); }`.

---

### N-4. `web/tsconfig.app.json` adds `"resolveJsonModule": true` as a one-liner addition — this is correct but should be accompanied by a comment about why

**Evidence:** `web/tsconfig.app.json:23` — `"resolveJsonModule": true`.

**Fix:** Add an inline comment: `// Required for import of permissions.snapshot.json (slice 7 drift sentinel).`

---

### N-5. The `openapi-snapshot.json` in `web/` is listed as a dirty (unstaged) modification in `git status` output — it was not committed with the slice

**Evidence:** `git status` output shows `modified: web/openapi-snapshot.json` (not staged). This file is the codegen artifact that should reflect the new `/me/permissions`, `/reactivate`, and `/un-decommission` endpoints.

**Fix:** Regenerate the OpenAPI snapshot (`pnpm codegen` or equivalent) once the API container is built with slice-7 changes, then stage and commit `web/openapi-snapshot.json`. The `TODO(api-codegen)` comments in `useReactivateApplication` and `usePermissions` should be resolved at that point.

---

## Missing tests

### MT-1. `ApplicationReactivateTests.cs` — no test for `Viewer` or `TeamAdmin` role on `/reactivate`

**Acceptance criterion:** Spec §10, integration table: `CatalogPermissionMatrixTests` covers 4 roles × 8 endpoints. The matrix test does cover these combinations, but `ReactivateApplicationTests.cs` itself only tests `Member` (403) and unauthenticated (401). The `Viewer` and `TeamAdmin` 403 paths are only asserted via the matrix. This is acceptable given the matrix — this is informational only.

---

### MT-2. `GetMePermissionsTests` — no test for unauthenticated `/me/permissions` returning 401

**Acceptance criterion:** Spec §10: "401 for unauthenticated" is listed as a test case for `GetMePermissionsTests.cs`. The plan step 1 includes it with the note "If `CreateUnauthenticatedClient` does not exist on the fixture, drop the 401 test in this task."

**Evidence:** `src/Modules/Organization/Kartova.Organization.IntegrationTests/GetMePermissionsTests.cs` — the diff shows only 4 role tests; the 401 case is absent.

**Test that should exist:** `Kartova.Organization.IntegrationTests.GetMePermissionsTests.GET_me_permissions_returns_401_when_unauthenticated`. Creates an anonymous client (no auth header), calls `GET /api/v1/organizations/me/permissions`, asserts `HttpStatusCode.Unauthorized`.

---

### MT-3. Mutation report predates slice 7 — surviving mutants on new files are unknown

**Mutation report:** `mutation-report-surviving.md` at repo root is dated 2026-05-09 and does not cover any slice-7 files.

**Files requiring mutation coverage (new logic):**
- `src/Kartova.SharedKernel/Multitenancy/KartovaRolePermissions.cs` — `ForRole` method: mutant "return EmptySet for any role" would still pass `KartovaRolePermissionsTests.PlatformAdmin_has_no_catalog_permissions` but fail `Viewer_can_read_catalog_only`.
- `src/Modules/Catalog/Kartova.Catalog.Domain/Application.cs` — `Reactivate()` and `UnDecommission()`: boundary mutants on `<=` in `UnDecommission` would not be caught if the `==now` boundary test is absent. The `==now` test exists in `ApplicationUnDecommissionTests` — coverage appears adequate, but must be validated by running Stryker.
- `src/Kartova.SharedKernel.AspNetCore/TenantClaimsTransformation.cs` — the `HasClaim` dedup guard: a mutant removing the `if (!id.HasClaim(...))` check would add duplicate permission claims — no test asserts the de-duplication behavior when a user holds two identical roles.

**Test that should exist:** `TenantClaimsTransformationTests.Duplicate_role_does_not_produce_duplicate_permission_claims` — build a principal with `realm_access.roles = ["Member", "Member"]`, transform, assert `FindAll(KartovaClaims.Permission)` has no duplicate values.

---

### MT-4. `usePermissions` — no Vitest for `isError` state propagation to `AppLayout`

**Acceptance criterion:** Spec §8.4 — "On a 403 from the API (race / token drift): existing problem-details handler surfaces a toast." The `AppLayout.test.tsx` mocks `usePermissions` directly and tests `isError → PermissionsErrorShell`. But `usePermissions.test.tsx` does not test a 403 response (only 401).

**Test that should exist:** `usePermissions.test.tsx` — `it("sets isError to true on 403 response")` — mock `fetch` to return `status: 403`, await loading complete, assert `result.current.isError === true`, `result.current.role === null`, `result.current.hasPermission(...)` returns false.

---

## What looks good

1. **`FrozenDictionary` / `FrozenSet` usage in `KartovaRolePermissions.cs`** (`src/Kartova.SharedKernel/Multitenancy/KartovaRolePermissions.cs:10`). Using `FrozenDictionary` and `FrozenSet` (introduced in .NET 8) for the permission map is an excellent choice — these are faster for lookup than `Dictionary`/`HashSet` and are immutable at the API surface. This is a correct performance optimization for a hot path (every request transformation).

2. **`TenantClaimsTransformation` permission-claim deduplication guard** (`src/Kartova.SharedKernel.AspNetCore/TenantClaimsTransformation.cs:51`). The `if (!id.HasClaim(KartovaClaims.Permission, perm))` guard prevents duplicate permission claims when a principal holds more than one role (not the current design, but defensively correct). This matches the spec §5.1 precisely.

3. **Role × endpoint matrix test driven from `KartovaRolePermissions.Map`** (`src/Modules/Catalog/Kartova.Catalog.IntegrationTests/CatalogPermissionMatrixTests.cs:33-42`). The test is data-driven off the live C# permission map, meaning adding a new role or permission automatically expands the matrix without test-file edits. This is the exact pattern specified in spec §10 and makes the authorization gate self-maintaining.

4. **Architecture drift sentinel connecting C# `KartovaPermissions.All` to the TS JSON snapshot** (`tests/Kartova.ArchitectureTests/KartovaPermissionsRules.cs:62`). The `Ts_snapshot_equals_csharp_KartovaPermissions_All` arch test closes the C# ↔ TypeScript drift loop at CI time. Combined with the runtime guard in `permissions.ts`, this is a two-layer defense against silent drift — a rare but well-executed pattern.

5. **`UnDecommissionApplicationTests` cross-tenant 404 test** (`src/Modules/Catalog/Kartova.Catalog.IntegrationTests/UnDecommissionApplicationTests.cs:166`). The test explicitly verifies that RLS filters a cross-tenant row for the reverse-lifecycle endpoint — not merely a 403. This is a subtle but important invariant: OrgAdmin can't un-decommission another tenant's app even with the right permission. The same test exists for `ReactivateApplicationTests.cs`. Both are correct and valuable.

---

## DoD Gate Summary

| Gate | Status | Evidence |
|------|--------|----------|
| §1 Build clean | ✅ | Cited in prompt |
| §2 Per-task reviews | ✅ | Cited in prompt |
| §3 requesting-code-review | ✅ | Cited in prompt |
| §4 Full test suite green | ✅ | Cited in prompt |
| §5 docker compose + real HTTP | ❌ | NOT YET DONE |
| §6 /simplify | ✅ | Cited in prompt |
| §7 Mutation feedback loop | ❌ | NOT YET DONE (stale report) |
| §8 /pr-review-toolkit:review-pr | ✅ | Cited in prompt |
| §9 /deep-review | ⏳ | This review |

**Status: "Implementation staged, verification pending" — not "slice 7 complete".**
