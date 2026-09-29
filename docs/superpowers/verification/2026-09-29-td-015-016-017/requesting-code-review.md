# Gate 6 — requesting-code-review (TD-015 / TD-016 / TD-017)

**Range:** f0a62b1..18c6178 (branch `chore/tech-debt-td-015-016-017`) · **Date:** 2026-09-29
**Context:** spec `docs/superpowers/specs/2026-09-29-td-015-016-017-design.md`, plan (Global Constraints + Review Focus), CLAUDE.md.

### Strengths

- **TD-015 is a behaviour-preserving move.** `SystemEndpoints` keeps the same URLs, predicates, writers, and the `PlatformAdminAuth.Policy` binding on `/health/detailed`, so the ADR-0060 contract is unchanged. The only observable drift is the OpenAPI `operationId`/tag on `/api/v1/version`, which is regenerated in the snapshot. The planted-violation proofs in `task-1-report.md` show all three rules discriminate.
- **Justified deviations:**
  - The arch-test csproj gets a direct `Microsoft.AspNetCore.OpenApi` reference, because `InterceptorsNamespaces` does not flow through a ProjectReference. The reason is written inline.
  - The `RestVerbPolicyRules` host got `AddHealthChecks`/`AddOpenApi`. The full-suite run caught this, and the ledger records it.
- **TD-016 is minimal and correct:**
  - The exact-match `location = /config.js` outranks the immutable `*.js` rule.
  - `no-store` and `application/javascript` are asserted in CI.
  - The Dockerfile has `ENV` defaults, so an unset var never leaks a literal `${…}`. Review Focus 3 is asserted by the check script.
  - A classic `<script>` runs before the deferred module bundle, so the module-level reads are valid.
  - `.dockerignore` excludes `.env*`, so no stale `VITE_*` can be baked into an image.
  - `render-check.sh` catches web/web-admin cross-wiring (it asserts the admin-only override lands).
- **TD-017 marker semantics are sound:**
  - The marker holds a timestamp only, with no token data.
  - It is per-origin through `sessionStorage`.
  - Every storage access sits in try/catch, and the storage-unavailable path falls back to today's redirect (Review Focus 5).
  - A clock moved back reads as "not recent".
  - A failed redirect clears the marker, so no round-trip is counted (Review Focus 2).
  - `retry` re-marks instead of clearing, as the plan specifies. That is better than the spec's "clears": a still-rejected session trips after one round-trip rather than two.
- **Tests exercise real behaviour.** `createAuthedApiClient` tests go through real openapi-fetch middleware with a fetch spy, the bridge tests drive the real hook, and a fake is used only for storage-throws. Every Review Focus item maps to a named test or CI assertion.
- **Docs are in sync:** README, csp-configuration, values.yaml, and the tech-debt statuses are updated. The KeyCloak heap cap is its own commit (D6).

### Issues

#### Critical (Must Fix)

None.

#### Important (Should Fix)

**I-1 — The `Program_maps_no_routes_directly` guard misses this repo's own route-mapping helpers.**
- **Where:** `tests/Kartova.ArchitectureTests/EndpointRouteRules.cs:213`.
- **What:** the regex `\.Map(Get|Post|Put|Patch|Delete|Methods|HealthChecks|OpenApi|Group|Fallback)\w*\(` does not match:
  - `MapTenantScopedModule(` and `MapAdminModule(` (`src/Kartova.SharedKernel.AspNetCore/ModuleRouteExtensions.cs:18,32`), which are the most likely way a future route gets mapped in this codebase;
  - plain all-verb `.Map(`, `MapControllers`, and `MapHub`.
- **Why:** an `app.MapAdminModule("x").MapGet(...)` in Program.cs is caught only by accident, through the chained `.MapGet(`. A bare `app.Map("/x", …)` or a helper that maps inside itself passes silently. That is exactly the invisible-route class TD-015 closes, and for the admin prefix it is the ADR-0118 hole.
- **Fix:** Program.cs's only `.Map*` call today is `module.MapEndpoints(app)`.
  - Tighten the regex to `\.Map(?!Endpoints\()\w*\(`.
  - Plant `app.MapAdminModule("x");` once to prove it discriminates, and record that in the ledger.

#### Minor (Nice to Have)

1. **A dead using remains in Program.cs.**
   - **Where:** `src/Kartova.Api/Program.cs:13`, `using Kartova.SharedKernel.AspNetCore.HealthChecks;`.
   - **What:** its only type, `HealthCheckJsonResponseWriter`, moved to `SystemEndpoints`. The a2a2405 cleanup missed this one line, although the deferred item is recorded as fixed.
   - **Fix:** delete it. Fold it into the I-1 commit.
2. **Ops-admin route coverage is thinner than admin-prefix coverage.**
   - **Where:** `EndpointRouteRules.cs:181-196` and `:230`.
   - **What:**
     - `Every_ops_admin_route_requires_PlatformAdminOnly` passes vacuously if `OpsAdminRoutes` is emptied. The ruling in `progress.md` accepts this, because the prefix rule then fails.
     - `Every_admin_route_combines_to_exactly_the_PlatformAdmin_scheme` filters on the `/api/v1/admin/` prefix only. So `/health/detailed`'s *combined* scheme is pinned only by the integration test `Detailed_returns_401_for_a_tenant_realm_user`.
   - **Fix:**
     - Add `Assert.IsTrue(OpsAdminRoutes.Length > 0)`.
     - Include `OpsAdminRoutes` in the combine test's endpoint filter.
3. **The ledger lacks two records the spec and CLAUDE.md require.**
   - **Where:** `docs/superpowers/verification/2026-09-29-td-015-016-017/dod.md`.
   - **What:**
     - The spec requires the planted-violation proofs "recorded in the ledger". They live only in the scratch `task-1-report.md`.
     - The CLAUDE.md E2E-impact trigger requires the full `e2e/` run to be noted in the ledger. `e2e-run.txt` shows 11/11 passing, but `dod.md` never references it.
   - **Fix:** cite both in the gate 3 and gate 9 rows when they are filled in.
4. **More config characters are unsafe than the docs say.**
   - **Where:** `web/default.conf.template:34`, `web/admin.conf.template:24`, `deploy/helm/kartova/values.yaml:45,68`, `deploy/README.md:127`.
   - **What:** nginx `return` text is a complex value, so a `$name` in an operator value becomes an nginx variable. A `\` also breaks the JS string. The documented constraint lists only quotes.
   - **Fix:** change the wording to "no quotes, backslashes or `$`". This is docs only; validation stays out of scope.
5. **"Try again" briefly shows the app before navigating away.**
   - **Where:** `web/src/shared/oidc/useApiAuthBridge.ts:82-85`.
   - **What:** `retry` sets `reauthFailed=false` before `signinRedirect` navigates. The app remounts for a moment and fires queries whose 401s the held guard ignores. It is cosmetic.
   - **Fix (optional):** keep the panel until navigation.
6. **The panel may not be vertically centred.**
   - **Where:** `web/src/shared/oidc/ReauthFailedPanel.tsx`.
   - **What:** `CenteredMessage` uses `h-full`. Rendered at the app root rather than inside a layout, it may sit at the top instead of the vertical centre.
   - **Status:** unverified; I can't see it without a browser. Confirm in the gate-9 screenshot.
7. **Existing image pipelines get no warning about the removed build args.**
   - **Where:** `web/Dockerfile`, `deploy/README.md`.
   - **What:** a pipeline still passing `--build-arg VITE_*` now gets only Docker's "build-arg not consumed" warning and a bundle with localhost defaults, unless `KARTOVA_*` is set.
   - **Fix:** add a one-line upgrade note to the README. The risk is low because there is no production deployment yet.

### Deferred-minor triage

| Item | Verdict | Reason |
|---|---|---|
| T1 dead usings in Program.cs | can-defer (but fix with I-1) | a2a2405 removed 3 of them. `using Kartova.SharedKernel.AspNetCore.HealthChecks;` (line 13) is still dead; see Minor 1. |
| T2 quote-free config values unenforced | can-defer | The spec lists validation as out of scope, and the constraint is documented in 3 places. Widen the wording per Minor 4. |
| T3 `curl \| grep -q` under pipefail | can-defer | The HTML is under 1 KB, far below the pipe buffer, and the match is near the end of the document, so SIGPIPE is practically unreachable. Aligning with the temp-file pattern is optional. |
| T6a console.error spy not restored | fixed (verified) | Both providers tests now `vi.restoreAllMocks()` in `afterEach`. |
| T6b no test for a 2nd 401 while tripped / signinRedirect swapped after trip | can-defer | Without the guard, a 2nd 401 still does not redirect, because `isRecentReauthAttempt` is still true. The live-ref swap is covered by the existing latest-`signinRedirect` test through the shared `reauthenticate` path. |
| T6c module-level `bridge` not reset in `beforeEach` | can-defer | Every test renders, which reassigns `bridge`, before reading it. There is no stale read today. |
| T6d tenant: authenticated user sees a failing app after a failed retry redirect | can-defer | This is pre-existing tenant behaviour when KeyCloak is unreachable during re-auth, not introduced here. It is recoverable: the next 401 redirects again. Admin shows "Sign-in unavailable" through AdminLayout. |
| T6e `signoutRedirect` failure is silent on the panel | can-defer | Same pattern as the existing `AdminNoAccessPage` sign-out. It needs KeyCloak down while the panel is visible. |
| T7 report labelling nit | can-defer | Cosmetic. |

### Declined to judge

- **Escaping or validating `KARTOVA_*` values** (beyond the docs wording in Minor 4): the spec puts this out of scope as trusted operator input.
- **Deriving `CSP_EXTRA_ORIGINS` from the runtime config:** out of scope per the spec.
- **Silent fallback to the localhost defaults when prod runtime config is empty:** decision D2 explicitly rejected fail-hard.
- **Clearing the marker on any authenticated non-401, including 5xx:** decision D5. A session where one authenticated call succeeds and another returns 401 in every cycle would still loop. No such route pair exists today: all tenant routes are tenant-scoped, and the missing-`tenant_id` 401 is uniform across them.
- **Sweeping the real `Program` host instead of `SystemEndpoints`:** rejected in D4.
- **Duplicate arch-test host setup in `EndpointRouteRules` and `RestVerbPolicyRules`:** pre-existing structure. The slice only kept the two in sync.
- **ADR-0113's historical wording about `VITE_*` build-time defaults:** it is an ADR record, and the spec does not ask for an edit.
- **The status of gates 3, 7, 8, 9 and 10:** not this gate's job. They are pending in the ledger.
- **The panel's visual rendering and in-browser behaviour:** I can't verify these without a browser. They belong to gate 9.

### Assessment

**Ready to merge: With fixes.** No critical defects, and the design matches the spec, with the plan-sanctioned `retry` refinement. Before merge:
- Fix I-1. It is a one-regex change, and the guard's whole purpose is the invisible-route class it currently lets through.
- Drop the leftover dead using.
- Record the planted-violation and E2E evidence in the ledger.
