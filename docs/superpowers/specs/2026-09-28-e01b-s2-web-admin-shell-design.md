# E-01b.F-03.S-02 — `web-admin` App Shell — Design

**Story:** E-01b.F-03.S-02 (`docs/product/phases/phase-0-foundation.md`, E-01b.F-03)
**ADRs:** [ADR-0118](../../architecture/decisions/ADR-0118-platform-admin-isolation.md) (layer 3 realized + amended by this slice), [ADR-0094](../../architecture/decisions/ADR-0094-untitled-ui-component-library.md), [ADR-0116](../../architecture/decisions/ADR-0116-spa-holds-tokens-bff-deferred.md), [ADR-0084](../../architecture/decisions/ADR-0084-playwright-mcp-for-frontend-development.md) (browser verification), [ADR-0087](../../architecture/decisions/ADR-0087-google-stitch-mcp-as-design-source.md) (local mockups)
**Date:** 2026-09-28
**Status:** approved (brainstorming 2026-09-27/28)

## Context

S1 (PR #100) isolated platform operators at the identity layer (`kartova-platform` realm) and the API layer (`PlatformAdmin` scheme + `PlatformAdminOnly` policy on `/api/v1/admin/**`). It shipped `GET /api/v1/admin/session/me` → `AdminMeResponse(UserId, Email, DisplayName)` and the public PKCE client `kartova-admin-web` (redirects `http://localhost:5174/callback`, `/silent-callback`; audience mapper → `kartova-admin-api`). S1 had **no UI surface**.

S2 is ADR-0118 layer 3: the operator UI on its own origin, with its own bundle and CSP, so that a tenant-origin XSS cannot reach the operator session.

Current state that shapes the design:
- `web/src/components/layout/{AppLayout,Sidebar,TopBar}.tsx` are tenant-coupled (`usePermissions`, `useOrgProfile`, `useCurrentUser`, tenant pill).
- The Untitled UI kit + `lib` + `styles` in `web/src` is ~10k lines / 117 files. F-01 (organization list) will need `DataTable`, `FilterBar` and `useCursorList`, not only the shell.
- `buildOidcConfig`, `RequireAuth` and `resolveReturnTo` contain no tenant logic.
- CORS is one global policy `KartovaWeb` (`Program.cs:122`, `app.UseCors("KartovaWeb")` at :251).
- The Helm chart has **no** `web` deployment, only `api` + `migrator`.
- `VITE_*` values are build-time. `web` nginx injects `CSP_EXTRA_ORIGINS` via envsubst.

## Scope

**In:**
- Second Vite entry in `web/` for the admin app (own build, image, nginx CSP, origin).
- Presentational extraction of the shared shell (sidebar frame, nav primitives, top-bar frame).
- Generic OIDC helpers moved to `@/shared/oidc`. Generic authed API client factory in `@/shared/api`.
- Admin OIDC (realm `kartova-platform`, client `kartova-admin-web`). Access check via `GET /api/v1/admin/session/me`. Landing, no-access state, callback.
- Per-route CORS: `/api/v1/admin/*` admits only the admin origin.
- Docker image, compose service, Helm deployments for **both** `web` and `web-admin`, CI jobs + `scripts/ci-local.sh` mirror.
- Dev realm: 4174 container origin for `kartova-admin-web` + a dev user without the `platform-admin` role.
- New E2E spec `admin-smoke.spec.ts`. ADR-0118 amendment. TD-016. Runbook updates.

**Out:**
- E-01b.F-01 / F-02 features (Organizations nav item is disabled).
- Runtime (non-build-time) frontend config → TD-016.
- Moving the UI kit to a workspace package (possible later, see Decisions §1).
- "Back to Kartova" cross-origin link.
- CORS change for `/health/detailed` (not called by a browser; TD-015 stays open).
- Audit records for operator actions (the empty-email/displayName follow-up is untouched: S2 only displays these values).

## Decisions (confirmed in brainstorming)

1. **Sharing mechanism: second Vite entry inside `web/`** (`web/admin.html` → `src/admin/**`), not npm workspaces. Isolation from ADR-0118 is a runtime property of the origin (separate `sessionStorage`, token, issuer, CSP). Shared *source* does not weaken it. The boundary is enforced by an import-graph test. A move to a workspace package stays possible if the admin app needs its own dependency lifecycle.
2. **Helm: both apps.** The chart gains `web` and `web-admin` Deployments + Services, each behind `enabled`.
3. **Admin nav:** `Overview` (landing) + disabled `Organizations` ("Coming soon"). Top bar: "Platform Admin" badge in the identity slot, no search, user menu (display name, email, Sign out).
4. **No new Stitch mockup.** Reuse the master_shell skeleton (`docs/ui-screens/master_shell_expanded|collapsed`, `docs/design/DESIGN.md`) with admin nav, as the rejected in-SPA spec (`dbcd425`) also did.
5. **No "Back to Kartova" link.** It would be a cross-origin link to an app where the operator has a different account.
6. **Access check distinguishes 403 from other failures.** 5xx/network never renders as "no access".
7. **Build-time config** (parity with `web`). Runtime config for both apps → **TD-016**.
8. **Dev user without the role** in `kartova-platform`, so E2E and gate 9 can show the 403 surface on the live stack.

## Components

### Frontend — structure

```
web/admin.html                         # entry; <title>Kartova Admin</title>; same fonts + index.css
web/vite.admin.config.ts               # input admin.html, outDir dist-admin, port 5174, alias @ → src
web/src/admin/
  main.tsx  App.tsx  router.tsx
  providers.tsx                        # Theme + OIDC + QueryClient + AdminApiAuthBridge
  api/client.ts                        # createAuthedApiClient(VITE_ADMIN_API_BASE_URL, …)
  api/useAdminSession.ts               # GET /api/v1/admin/session/me
  layout/AdminLayout.tsx               # access gate + ShellLayout with admin nav
  layout/AdminSidebarNav.tsx           # Overview + disabled Organizations
  pages/AdminLandingPage.tsx
  pages/AdminNoAccessPage.tsx
  pages/AdminCallbackPage.tsx
  __tests__/importBoundary.test.ts
```

| File | Change |
|---|---|
| `web/src/components/layout/sidebar-nav.tsx` (new) | `NavGroup`, `NavItemLink`, `DisabledItem` moved out of `Sidebar.tsx`. Pure move. |
| `web/src/components/layout/SidebarFrame.tsx` (new) | `<aside>` chrome + "Kartova" logo header + `<nav>` wrapper (moved from `Sidebar.tsx`). Nav passed as `children`. `NavCollapsibleGroup` moves to `sidebar-nav.tsx` with the other primitives. |
| `web/src/lib/utils/initials.ts` (moved from `shared/auth/initials.ts`) | `initialsOf` is generic and `TopBarFrame` needs it; keeps `src/shared/auth/**` out of the admin import closure. |
| `web/src/components/layout/TopBarFrame.tsx` (new) | Header chrome with slots `identity`, `center`, and `user: { displayName, email } \| null` + `onSignOut`. Owns the avatar dropdown. |
| `web/src/components/layout/ShellLayout.tsx` (new) | `sidebar` + `topBar` + `<main><Outlet/></main>`. No hooks. |
| `Sidebar.tsx`, `TopBar.tsx`, `AppLayout.tsx` (modify) | Tenant compositions over the frames. Rendered DOM and behavior unchanged. Existing tests stay green without assertion changes. |
| `web/src/shared/oidc/` (new; moved from `shared/auth`) | `buildOidcConfig`, `RequireAuth`, `resolveReturnTo`. Tenant imports updated. The tenant `AuthProvider` stays in `shared/auth` (its env defaults are tenant-specific). The admin app builds its own config with `buildOidcConfig` and mounts `react-oidc-context`'s `AuthProvider` directly. |
| `web/src/shared/api/createAuthedApiClient.ts` (new) | `createAuthedApiClient(baseUrl, getToken, onUnauthorized)`: openapi-fetch + bearer middleware + 401 hook + deferred fetch. `features/catalog/api/client.ts` delegates to it. Its exported API (`setAccessTokenProvider`, `setUnauthorizedHandler`, `apiClient`, `API_BASE_URL`) is unchanged. |
| `web/package.json` (modify) | Scripts `dev:admin` (codegen + `vite --config vite.admin.config.ts`), `build:admin` (`tsc -b && vite build --config vite.admin.config.ts`), `preview:admin` (port 4174). |

**Import boundary (`importBoundary.test.ts`, vitest):**
- The **transitive import closure** of `src/admin/main.tsx` (resolving `@/…` and relative specifiers) must contain no file under `src/features/**`, `src/app/**` or `src/shared/auth/**`.
- Allowed from admin: `@/components/**`, `@/lib/**`, `@/hooks/**`, `@/styles/**`, `@/shared/oidc/**`, `@/shared/api/**`, `@/shared/forms/**`, `@/generated/**`.
- Files outside `src/admin/**` must not import `@/admin/**`.
- The test scans source text (static and dynamic `import(...)`) and fails listing each offending file + specifier.

### Frontend — auth and access flow

**OIDC:** `buildOidcConfig` with:
- authority `VITE_ADMIN_OIDC_AUTHORITY` (default `http://localhost:8180/realms/kartova-platform`);
- client `VITE_ADMIN_OIDC_CLIENT_ID` (default `kartova-admin-web`);
- redirect `${origin}/callback`, post-logout `${origin}`;
- `window.sessionStorage` (admin origin's own storage);
- renew via refresh token, same as `web`. `/silent-callback` is not used; the realm entry stays harmless.

**API client:** `src/admin/api/client.ts` uses `VITE_ADMIN_API_BASE_URL` (default `http://localhost:8080`). `AdminApiAuthBridge` sets token/redirect refs during render, following the `ApiAuthBridge` pattern, to avoid the stale-token 401 race from PR #47.

**Flow:**
```
/ (cold) → RequireAuth → signinRedirect({ state: { returnTo } }) → KC kartova-platform
  → /callback → AdminCallbackPage
       ok    → navigate(resolveReturnTo(state) ?? "/", replace)
       error → inline "Sign-in failed" panel + [Try again] (signinRedirect)
  → AdminLayout → useAdminSession (retry: false on 401/403)
       loading → skeleton
       200     → ShellLayout(AdminSidebarNav, TopBarFrame{badge, user from response}) → <Outlet/>
       403     → AdminNoAccessPage (no shell; [Sign out] to switch account)
       401     → onUnauthorized → signinRedirect (returnTo = current URL)
       other   → "Couldn't verify admin access" + [Retry]  (5xx/network ≠ no access)
```

**Routes:** `/callback`, `/` (protected landing), `*` → `/`. "No access" is a state of `AdminLayout`, not a route, so a deep link can neither reach it nor bypass it. No session bootstrap call (`POST /api/v1/auth/session` is tenant-only).

**Pages:**
- **Landing:** h1 "Platform Admin", "Signed in as {displayName} · {email}", note "Organization management is coming". Empty `displayName`/`email` render "—".
- **No access:** "No access" + "This account is not a platform operator." + [Sign out].
- **Top bar:** identity = `Badge` "Platform Admin"; no search; user menu = display name, email, Sign out → `signoutRedirect`.
- **Sidebar:** logo "Kartova" + `Overview` (`/`) + `Organizations` (disabled, "Coming soon").

### Backend — per-route CORS

| File | Change |
|---|---|
| `src/Kartova.SharedKernel.AspNetCore/CorsPolicies.cs` (new) | `const TenantWeb = "KartovaWeb"`, `const AdminWeb = "KartovaAdminWeb"`. |
| `CorsConfigKeys.cs` (modify) | `AdminAllowedOrigins = "Cors:AdminAllowedOrigins"`. |
| `CorsOriginLists.cs` (new, SharedKernel.AspNetCore) | `Normalize(string[])`: trims whitespace and a trailing `/`, drops empty entries (a browser `Origin` never carries a trailing slash, so `https://admin.example/` in config would otherwise never match). `Validate(tenant, admin)`: throws `InvalidOperationException` if an origin is in both normalized lists (case-insensitive) or the admin list contains `*`. Program.cs passes the normalized lists to `WithOrigins`. |
| `src/Kartova.Api/Program.cs` (modify) | Read both lists → `CorsOriginLists.Validate` → register `TenantWeb` (unchanged) + `AdminWeb` (`WithOrigins(admin).AllowAnyHeader().AllowAnyMethod()`, no credentials; empty list → no origins). Empty admin list outside Development → warning log. `app.UseCors(CorsPolicies.TenantWeb)` stays as the default. |
| `ModuleRouteExtensions.MapAdminModule` (modify) | `.RequireAuthorization(PlatformAdminAuth.Policy).RequireCors(CorsPolicies.AdminWeb)`. Endpoint CORS metadata overrides the middleware default and makes the endpoint accept `OPTIONS` preflight. |
| `appsettings.json` / `appsettings.Development.json` | `Cors:AdminAllowedOrigins`: `[]` / `["http://localhost:5174"]`. |

**Resulting matrix** (proven by integration tests):

| Origin → route | `/api/v1/admin/*` | tenant routes |
|---|---|---|
| web-admin (`:5174`/`:4174`) | ACAO | no ACAO |
| web (`:5173`/`:4173`) | no ACAO | ACAO |
| unknown | no ACAO | no ACAO |

### Build and deploy

- **`web/Dockerfile`:** `ARG APP=web`. The build stage runs `npm run build` or `npm run build:admin`. The runtime stage copies `dist` or `dist-admin` and `default.conf.template` or `admin.conf.template`. Build context stays `web/`.
- **`web/admin.conf.template` (new):** same structure as `default.conf.template`; CSP without the tenant exceptions:
  ```
  default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:;
  font-src 'self'; connect-src 'self' ${CSP_EXTRA_ORIGINS}; frame-src ${CSP_EXTRA_ORIGINS};
  form-action 'self' ${CSP_EXTRA_ORIGINS}; object-src 'none'; base-uri 'self'; frame-ancestors 'none'
  ```
- **`docker-compose.yml`:**
  - service `web-admin` (build arg `APP=admin`, image `kartova/web-admin:dev`, `4174:8080`, `CSP_EXTRA_ORIGINS: "http://localhost:8080 http://localhost:8180"`);
  - api env `Cors__AdminAllowedOrigins__1: "http://localhost:4174"`.
- **`deploy/keycloak/kartova-platform-realm.json`:**
  - `kartova-admin-web` adds `http://localhost:4174/callback`, webOrigin `http://localhost:4174`, post-logout `http://localhost:4174/*`;
  - new user `operator-norole@kartova.local` / `dev_password_12` with no realm roles.
  - `KeycloakPlatformRealmSeedRules` is updated where it asserts users/redirects. The "dev user holding platform-admin" rule still holds.
  - Local apply needs `docker compose down -v`.
- **Helm (`deploy/helm/kartova`):**
  - `web.yaml`, `web-admin.yaml` (each a Deployment + Service rendered from shared `_helpers.tpl` templates `kartova.spa.deployment` / `kartova.spa.service`);
  - values `web.{enabled, image.name: web, port, cspExtraOrigins, origin, resources}` and the same for `webAdmin` (`image.name: web-admin`), `enabled: true` by default;
  - probes `GET /`; ClusterIP services; no ingress (the chart has none today);
  - api env `Cors__AllowedOrigins__0` / `Cors__AdminAllowedOrigins__0` rendered only when `web.origin` / `webAdmin.origin` is set (`with`), so existing installs keep their behavior.
- **CI (`.github/workflows/ci.yml`) + `scripts/ci-local.sh`:**
  - `images`: add `docker build -f web/Dockerfile --build-arg APP=admin -t kartova/web-admin:ci web`;
  - `frontend`: add `npm run build:admin`;
  - `helm`: add a render assertion that the `web-admin` and `web` Deployments are present.
- **`e2e.yml`:** the stack includes `web-admin`.

## Error handling

- `session/me` 401 → re-auth redirect. 403 → no-access page. Any other error or network failure → retry panel, never "no access".
- OIDC callback error → inline sign-in-failed panel with retry. The error is logged to the console, as in `CallbackPage`.
- Startup: overlapping tenant/admin origins or `*` in the admin list → `InvalidOperationException` (fail fast).
- A tenant token cannot be obtained by the admin app (different realm + client). A mis-issued one is rejected 401 by the S1 scheme.

## Testing (per docs/TESTING-STRATEGY.md)

Wiring slice (HTTP + auth + CORS middleware) → real seam.

**Gate-3 deliverables:**
- **`CorsTests` (extend, `KeycloakContainerTestBase`, real JWT):**
  - preflight admin origin → `/api/v1/admin/session/me` → ACAO = admin origin;
  - preflight tenant origin → `/api/v1/admin/session/me` → no ACAO;
  - preflight admin origin → `/api/v1/organizations/me` → no ACAO;
  - real `GET /api/v1/admin/session/me` with admin origin + live `kartova-platform` token (`kartova-admin-test` password grant) → 200 + ACAO;
  - existing tenant cases unchanged.
- **Unit `CorsOriginListsTests`:** overlap → throws (incl. case/trailing-slash variants), `*` in admin → throws, disjoint → ok, empty lists → ok.
- **Arch (`EndpointRouteRules`):** every `/api/v1/admin/*` endpoint carries `CorsPolicyMetadata`/`EnableCors` for `KartovaAdminWeb`.
- **Arch (`KeycloakPlatformRealmSeedRules`):** `kartova-admin-web` redirects are exactly the 5174 + 4174 callback set; `operator-norole@kartova.local` exists and holds no realm roles.
- **Vitest:**
  - `AdminLayout`: loading / 200 / 403 / 401 / 500 → retry;
  - `AdminCallbackPage`: success → returnTo, error → panel;
  - `AdminLandingPage`: values + "—" fallback;
  - `createAuthedApiClient`: bearer header, 401 handler, no header without token;
  - `SidebarFrame` / `TopBarFrame`: slots render, sign-out calls `onSignOut`;
  - `importBoundary`: both directions;
  - existing `Sidebar` / `TopBar` / `AppLayout` tests green, assertions unchanged.
- **E2E `e2e/tests/admin-smoke.spec.ts` (new, `E2E_ADMIN_BASE_URL` default `http://localhost:4174`):**
  - `platform-admin@kartova.local` → landing shows email;
  - `operator-norole@kartova.local` → "No access".

**Gate 4:** applies (Dockerfile, new nginx template, compose service).
**Gate 9:** cold-start `dev:admin` (:5174) against the compose stack → login → landing + no-access, screenshots → `verification/2026-09-28-e01b-s2-web-admin-shell/`. Also check the browser console for CSP violations on the `:4174` container. Playwright MCP failed to connect in the brainstorming session and must be reconnected before gate 9, otherwise the gate stays *pending*.
**E2E-impact trigger:** the shell refactor touches the nav and top bar traversed by tenant specs → update if needed and run locally `smoke`, `detail-tabs`, `system-list-surface` (re-check the full `e2e/tests` list in the plan). Record in the DoD ledger.

## Impact Analysis (LSP)

To be grounded in the plan:
- **`MapAdminModule`** (C#, behavior change): `findReferences` for every admin route group.
- **`CorsConfigKeys.*`, `CorsPolicies.*`, policy name `"KartovaWeb"`:** `const`/string → grep.
- **TS moves** (`buildOidcConfig`, `RequireAuth`, `resolveReturnTo`, `NavGroup`, `NavItemLink`, `DisabledItem`, `createApiClient`): no TS language server → grep consumers.

## Size

Estimated production code:
- shell extraction net ~+80;
- admin app ~300;
- shared client/OIDC moves ~+20;
- CORS ~40.

Total **~440**. Helm/Docker/nginx/CI/realm JSON, tests and moves are excluded. Above the ~400 target, within the ~800 ceiling, so one slice.

## Documentation

- **ADR-0118 — amendment 2026-09-28** (text approved in brainstorming):
  > Layer 3 "separate app `web-admin/`" is realized as a **second Vite entry inside `web/`** (`web/admin.html` → `src/admin/**`, own `vite.admin.config.ts`, own build output, own image `kartova/web-admin`, own nginx CSP, own origin). Isolation is a runtime property of the origin (storage, token, CSP), not of source layout; shared UI kit source is permitted. A vitest import-boundary test forbids `src/admin/**` from importing tenant code (`@/features/**`, `@/app/**`, `@/shared/auth/**`) and vice versa. CORS is per route: `/api/v1/admin/*` binds policy `KartovaAdminWeb` (`Cors:AdminAllowedOrigins`); startup fails if an origin appears in both allow-lists or the admin list contains `*`. Migration to a workspace package remains possible if the admin app needs an independent dependency lifecycle.
  - The ADR README row and changelog get the amendment.
- **`deploy/README.md`:** `Cors:AdminAllowedOrigins`, admin origin + `VITE_ADMIN_*` build args, Helm `web`/`webAdmin` values, `docker compose down -v` after realm edits.
- **`docs/engineering/tech-debt.md`:** TD-016 (runtime frontend config for `web` + `web-admin`; build-time `VITE_*` forces per-environment images).
- **`docs/product/CHECKLIST.md`:** tick E-01b.F-03.S-02 at slice close.
- `list-filter-registry.md`: N/A (no list surface).

## Definition of Done

Per CLAUDE.md's ten gates. Ledger: `docs/superpowers/verification/2026-09-28-e01b-s2-web-admin-shell/dod.md` (+ `gate-findings.yaml`).

## Follow-ups

- TD-016 runtime frontend config.
- E-01b.F-01 (Organizations list) enables the Organizations nav item and reuses `DataTable`/`useCursorList` in `src/admin/`.
- Optional: workspace package for the UI kit if the admin app needs an independent dependency lifecycle.
