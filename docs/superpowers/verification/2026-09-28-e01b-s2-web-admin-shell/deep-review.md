# Gate 8 — `/deep-review` — E-01b.F-03.S-02 web-admin app shell

**Range:** `2c03a2b...42d599a` (branch `feat/e01b-s2-web-admin-shell`, status OPEN, pre-merge gate) · **Date:** 2026-09-28
**Read against:** spec `docs/superpowers/specs/2026-09-28-e01b-s2-web-admin-shell-design.md`, plan `docs/superpowers/plans/2026-09-28-e01b-s2-web-admin-shell-plan.md` (local), ADR-0118 (+ 2026-09-28 amendment), ADR-0116/0094/0084/0087/0092/0006, ADR-0097 + `docs/TESTING-STRATEGY.md`, CLAUDE.md §Definition of Done, prior-gate artifacts (`dod.md`, `gate-findings.yaml`, `simplify.md`, `requesting-code-review.md`, `review-pr.md`, gate 1–4 evidence, `e2e-run.txt`).
**Method:** grep/read only (no LSP), per the brief. Prior-gate "fixed" claims were re-checked against the tree. Gate-6 I1/I2/M1/M3/M4/M5/M7/M8 and gate-7 F2, the session-error logging, and the 5 test gaps were all confirmed present. The gate-7 redirect-rejection fix is present but does nothing at runtime (Should-fix #1).

### Overview
The slice adds the platform-operator console as a second Vite entry in `web/`: `admin.html` → `src/admin/**`, with its own build (`dist-admin`), image (`APP=admin`), nginx CSP and origin (5174 dev / 4174 container). The tenant shell is split into hook-free frames (`SidebarFrame`, `TopBarFrame`, `ShellLayout`, `sidebar-nav`) that both apps use. The OIDC helpers move to `@/shared/oidc`, and a shared authed API client factory is added. On the API, `/api/v1/admin/*` gets its own CORS policy (`KartovaAdminWeb`), with a startup guard that fails when the two origin lists overlap or contain a wildcard. Also included: realm updates (4174 origin + a no-role dev user), a compose service, Helm `web` + `web-admin` Deployments, CI jobs, E2E admin smoke specs, the ADR-0118 amendment, and TD-016/TD-017. The branch also changes CLAUDE.md, the impact-analysis template and the dev-hm agents (LSP → grep); gate 6 M9 already flagged this for the PR description.

### Blocking-class issues
None.

DoD status, for the record rather than as a finding: gate 8 (this report), the terminal re-verify, gate 9 and gate 10 are ⏳ in `dod.md`. Gate 3's full-suite evidence (`gate1-3-build-suite.txt`) is at `4fca365`. The fixes in `d337205`, `5e7b770` and `453193d` changed C# (`CorsOriginLists.cs`, `CorsTests.cs`) and TS, and have only scoped re-runs. So the terminal re-verify must be a full `dotnet test Kartova.slnx -m:1` + `npm test` + `tsc -b` + `e2e/run.sh` on the final commit, as gate 6 I3 already requires. `docs/product/CHECKLIST.md:133` is correctly still unticked until close.

### Should-fix issues

- **1. The gate-7 "redirect rejection handling" fix does nothing: `react-oidc-context` 3.3.1 navigator methods never reject.**
  - **Evidence:**
    - The library wraps `signinRedirect`/`signoutRedirect` in `try { return await userManager[key](args) } catch (error) { dispatch({ type: "ERROR", … }); return null; } finally { dispatch({ type: "NAVIGATOR_CLOSE" }) }` (`web/node_modules/react-oidc-context/dist/esm/react-oidc-context.js:170-190`; installed version 3.3.1).
    - So the `.catch` branches in these places never run:
      - `web/src/shared/oidc/useApiAuthBridge.ts:45-55`;
      - `web/src/admin/layout/AdminLayout.tsx:37`;
      - `web/src/admin/pages/AdminCallbackPage.tsx:45-47`.
    - `review-pr.md` (silent-failure High, "fixed: `.catch` + `console.error`; the 401 guard resets on rejection") and `dod.md` gate 7 both claim this fix.
    - The test `web/src/shared/oidc/__tests__/useApiAuthBridge.test.tsx:59-80` mocks a *rejecting* `signinRedirect`, which the real library never produces. The test is green for a path that cannot happen.
  - **Impact:** say KeyCloak is unreachable when a mid-session 401 triggers re-auth.
    - The failure lands in `auth.error` and the promise resolves to `null`.
    - Nothing is logged, and `redirectingRef` (`useApiAuthBridge.ts:34-39`) stays `true` for the rest of the page's life, so every later 401 is ignored.
    - `AdminLayout` shows "Signing in…" forever (`AdminLayout.tsx:53`) with no retry.
    - The ledger records a High finding as fixed when it is not.
  - **Fix:**
    - In `useApiAuthBridge.ts`, treat `auth.error` as the failure signal: `useEffect(() => { if (auth.error?.source === "signinRedirect") { redirectingRef.current = false; console.error("Re-authentication redirect failed:", auth.error); } }, [auth.error])`. Alternatively, `.then(r => { if (r === null) … })` on the resolved value.
    - In `AdminLayout`, render `SessionErrorPanel` (or a "Sign-in unavailable" panel) when the 401 state coincides with an `auth.error` whose source is `signinRedirect`.
    - Replace the rejecting mock in `useApiAuthBridge.test.tsx:59-80` with the real contract: `signinRedirect` resolves `null` and the next render carries `auth.error`.
    - Correct the gate-7 row in `gate-findings.yaml` and `dod.md`.

- **2. The shared `RequireAuth`, which the admin app now depends on, loops `signinRedirect` without bound when KeyCloak is unreachable, and never shows an error.**
  - **Evidence:**
    - `web/src/shared/oidc/RequireAuth.tsx:7-17`: the effect `[auth]` fires `signinRedirect` whenever `!isLoading && !isAuthenticated && !activeNavigator`.
    - After a failed redirect, the library dispatches `ERROR` (`isLoading: false`, lines 47-54) and then `NAVIGATOR_CLOSE` (`activeNavigator: undefined`, lines 41-46). The `auth` identity changes, so the condition holds again and the effect fires again.
    - `RequireAuth.tsx:21-23` renders "Signing in…" and never reads `auth.error`.
    - The admin cold-start path goes through it: `web/src/admin/router.tsx:9-13`.
  - **Impact:**
    - On a cold start with KeyCloak down or misconfigured (wrong `VITE_ADMIN_OIDC_AUTHORITY`, which TD-016's per-environment build makes more likely), the console spins on "Signing in…".
    - It also hammers the discovery endpoint in a tight retry loop, because oidc-client-ts caches metadata only on success.
    - The spec's error-handling rule (§Error handling: failures get a retry panel and never a silent state) holds for the callback page but not for the entry path. The behavior is pre-existing in the tenant SPA; S2 extends it to a new app.
  - **Fix:**
    - In `RequireAuth`, if `auth.error && !auth.isAuthenticated`, stop auto-redirecting and render `CenteredMessage` "Sign-in unavailable" with a [Try again] button that calls `signinRedirect` once.
    - Add vitest cases in `web/src/shared/oidc/__tests__/RequireAuth.test.tsx`: after an error render, `signinRedirect` is not called again, and the panel shows.
    - If deferred, extend TD-017's scope (it covers the persistent-401 loop, not this entry-path loop) and cite it in `dod.md`.

### Nits

- **ADR-0118 body still says "own image and Helm release", and the amendment does not correct it.**
  - **Evidence:** `docs/architecture/decisions/ADR-0118-platform-admin-isolation.md:24` vs spec Decision 2 ("Helm: both apps" in the one chart). Gate 6 M7 fixed only the README row (`docs/architecture/decisions/README.md:249`). The phase acceptance criteria also still name a `web-admin/` directory (`docs/product/phases/phase-0-foundation.md:163`).
  - **Fix:** add one sentence to the 2026-09-28 amendment: "Deployed as the `web-admin` Deployment in the `kartova` chart, not a separate release". Reword the phase acceptance criteria to "admin console (second Vite entry in `web/`)".

- **The rewritten README row for 0118 dropped content the plan did not ask to remove.**
  - **Evidence:** `docs/architecture/decisions/README.md:249` lost "tenant tokens 401 on admin routes and vice versa" and "Rejects the in-SPA `/admin` area". Plan Task 10 Step 2 said to replace only the operator-UI clause.
  - **Fix:** restore both clauses.

- **The admin app has no coverage threshold.**
  - **Evidence:** `web/vitest.config.ts:26-33` includes `src/shared/oidc/**` and `src/shared/api/**`, but not `src/admin/api/**` (the equivalent of `src/features/**/api/**`).
  - **Fix:** add `"src/admin/api/**"` to `coverage.include`.

- **The spec's Impact Analysis heading contradicts the branch's own process change.**
  - **Evidence:**
    - The spec, `docs/superpowers/specs/2026-09-28-e01b-s2-web-admin-shell-design.md:209-212`, says "Impact Analysis (LSP)" / "`findReferences` for every admin route group".
    - This branch's CLAUDE.md and `docs/superpowers/templates/plan-impact-analysis.md` make impact analysis grep-only, and the plan follows that correctly.
  - **Fix:** rename the heading to "Impact Analysis" and replace "`findReferences`" with "grep `MapAdminModule\(`".

- **The tenant top-bar DOM changed, although the spec says it would not.**
  - **Evidence:** `web/src/components/layout/TopBarFrame.tsx:46-47` applies `orDash` to the tenant user menu too, so a blank name now renders "—". Spec line 81 says "Rendered DOM and behavior unchanged". The change was an intentional gate-6 M4 fix, but it is not recorded in the spec.
  - **Fix:** add a one-line spec note ("empty name/email render '—' in both apps, gate-6 M4").

### Missing tests

- **Criterion (spec §Frontend — auth and access flow, "401 → onUnauthorized → signinRedirect"):** the admin SPA can only tell 401 from a network error if the API's 401 on an admin route carries `Access-Control-Allow-Origin` for the admin origin. Otherwise the browser reports a CORS failure, `fetch` rejects, and the console shows the retry panel instead of re-authenticating.
  - **Test:** in `tests/Kartova.Api.IntegrationTests/CorsTests.cs`, add `Get_from_admin_origin_without_token_returns_401_with_admin_origin`. Send `GET /api/v1/admin/session/me` with `Origin: http://localhost:5174` and no bearer. Assert 401 and `Access-Control-Allow-Origin == AdminOrigin`. Add a variant with a live *tenant* token; the expected result is the same (401 + ACAO).

- **Criterion (gate-7 claim "guard resets on rejection", Should-fix #1):** a failed re-auth redirect releases the guard and is surfaced.
  - **Test:** in `web/src/shared/oidc/__tests__/useApiAuthBridge.test.tsx`, add "failed redirect (resolves null + auth.error) releases the guard and logs".
    1. Make `signinRedirect` resolve `null`.
    2. Re-render with `auth.error = { source: "signinRedirect", … }`.
    3. Assert `console.error` was called.
    4. Assert a second handler call invokes `signinRedirect` again (2 calls in total).

- **Criterion (spec §Error handling; Should-fix #2):** a sign-in failure on the entry path is shown, not looped.
  - **Test:** in `web/src/shared/oidc/__tests__/RequireAuth.test.tsx`, add "does not re-redirect after a signinRedirect error; renders the retry panel". Render with `{ isLoading: false, isAuthenticated: false, error: {…} }`, then assert that `signinRedirect` was not called and that the "Sign-in unavailable" heading is visible.

- **Criterion (spec §Testing, Arch `EndpointRouteRules`):** "every `/api/v1/admin/*` endpoint carries `KartovaAdminWeb`" must not pass vacuously if endpoint discovery breaks.
  - **Evidence:** `tests/Kartova.ArchitectureTests/EndpointRouteRules.cs:210-221` has no non-empty guard. The web boundary test has guards (`web/arch/adminImportBoundary.test.ts:141-143`).
  - **Test:** in the same method, assert that the admin-prefixed endpoint set contains `/api/v1/admin/session/me` before checking offenders.

### What looks good

- **Fail-fast CORS guard with normalization and proven wiring.**
  - `src/Kartova.SharedKernel.AspNetCore/CorsOriginLists.cs:150-187` trims trailing slashes and whitespace, rejects case-insensitive overlap, rejects an admin `*`, and rejects a tenant `*` while the admin list is non-empty.
  - `tests/Kartova.Api.IntegrationTests/CorsTests.cs:345-376` boots the real host with overlapping config and asserts the `InvalidOperationException`, so the `Program.cs` call site is pinned rather than assumed.
- **Per-route CORS bound in one helper, with coverage in both directions.**
  - `ModuleRouteExtensions.cs:223-226` (`RequireCors(CorsPolicies.AdminWeb)` on the group) and `EndpointRouteRules.cs:210-234` (admin routes must carry the policy, and non-admin routes must not) cover the pattern.
  - The real-seam matrix in `CorsTests.cs:278-336` includes the discriminating case: a real GET from the tenant origin with a *valid* operator token gets no ACAO (Review Focus #5).
- **The import boundary is tested on the transitive closure, with positive controls.**
  - `web/arch/adminImportBoundary.test.ts:137-182` resolves `@/` and relative specifiers across static, re-export and dynamic imports.
  - It asserts that known files are in the closure (the anti-vacuity guard) and tests the regex and the resolver directly. This enforces the ADR-0118 amendment instead of just describing it.
- **The access gate separates 403 from 401 from 5xx/network, and each case is pinned.**
  - `web/src/admin/layout/AdminLayout.tsx:50-55` maps each status to its surface.
  - `web/src/admin/__tests__/AdminLayout.test.tsx:225-267` asserts call counts: 401 and 403 fire exactly one fetch, with no retry. It also asserts that a network rejection shows the retry panel, never "No access" (spec Decision 6).
- **Build/deploy seams that fail closed and keep backward compatibility.**
  - `web/Dockerfile:27-31`: the `APP` switch exits non-zero on an unknown value.
  - `web/Dockerfile:38` + `web/admin.conf.template:9`: the dedicated nginx root means no stock `index.html` is served on the admin origin.
  - `deploy/helm/kartova/templates/api-deployment.yaml:33-40`: the CORS env vars are rendered only `with` an origin, so existing installs keep their behavior. `deploy/helm/render-check.sh` asserts every enable/disable combination.
