# Slice 4 — Catalog UI: First Cut

**Date:** 2026-04-30
**Stories:** E-02.F-01.S-01 (register application — UI surface) + parts of S-02 (Application detail page)
**Phase:** 1 — Core Catalog & Notifications
**Branch (proposed):** `feat/slice-4-catalog-ui-first-cut`

---

## 1. Goal

Slice 4 lands the **first real UI** for Kartova: master shell + catalog list + Application detail + Register Application form (modal), wired against slice-3's existing endpoints. It is a deliberately frontend-heavy slice that proves five things slice-3 didn't:

1. KeyCloak OIDC SPA login works end-to-end (Authorization Code + PKCE).
2. The frontend stack from ADR-0088 (shadcn/ui + TanStack Query + react-hook-form + zod) is actually in use, not just declared in an ADR.
3. The OpenAPI codegen pipeline (ADR-0029/0034) emits typed clients that the SPA consumes.
4. Canonical mockups in `docs/ui-screens/` map cleanly onto shadcn/ui components, with Playwright MCP verifying the result (ADR-0084).
5. Slice-3's endpoint shapes actually fit real screens — and any mismatch surfaces a concrete, scoped backlog of fixes for slice 5+, instead of remaining speculative.

Slice 4 is **not** a complete S-02 feature: no edit, no lifecycle transitions, no breadcrumbs, no search/filter/pagination, no admin (register-organization) flow, no checked-in Playwright E2E, no audit log, no visual regression or a11y audit.

---

## 2. Pre-requisites

The following are **already on master** as of this spec:

- Slices 0–3 merged: walking skeleton, KeyCloak realm seeded with at least two tenant users (`user@orga.kartova.local`, `admin@orgb.kartova.local`), tenant-scope hybrid filter (ADR-0090), Catalog module with `Application` aggregate and three endpoints (`POST /api/v1/catalog/applications`, `GET /{id}`, `GET /list`).
- `web/` project scaffolded — React 19 + Vite 6 + TS strict + Tailwind v4 + react-router 7 + shadcn/ui configured (`components.json` present); `CatalogPlaceholder.tsx` is the only page today.
- OpenAPI document published at `/openapi/v1.json` by the API.
- Canonical mockups available under `docs/ui-screens/`: `master_shell_expanded`, `catalog_home_dashboard_fixed_navigation`, `entity_detail_payment_gateway_navigation_sync`, `create-application`.

---

## 3. Decisions

| # | Decision | Rationale |
|---|---|---|
| 1 | Slice scope: master shell + catalog list + Application detail + Register Application modal. No edit, no lifecycle transitions, no filter/search/pagination, no breadcrumbs. | Smallest UI cut that proves stack + auth + codegen + mockup-mapping end-to-end. |
| 2 | Auth: SPA-direct OIDC (Authorization Code + PKCE) via `oidc-client-ts` + `react-oidc-context`. No BFF. | KeyCloak is already configured for it; slice-2 already validates the JWTs it issues; no rework cost. BFF captured as a backlog story (Section 7). |
| 3 | API client: codegen via `openapi-typescript` + `openapi-fetch` from the live OpenAPI document. Generated output is `.gitignore`d; codegen runs in `predev` / `prebuild`. | One source of truth; types stay in lockstep with backend Contracts; no committed generated code in PRs. |
| 4 | Server state: TanStack Query. Form state: react-hook-form + zod. Toasts: sonner. Icons: lucide-react. | Matches ADR-0088 verbatim — first slice to actually exercise each. |
| 5 | Frontend test pyramid for slice 4: Vitest unit tests for hooks/schemas/auth interceptor + Playwright MCP manual verify (ADR-0084). No checked-in Playwright E2E. | Vitest is cheap and catches form/hook regressions; checked-in E2E needs CI compose stack + token bootstrap that doesn't exist yet — captured as backlog story (Section 7). |
| 6 | Backend changes are limited to **CORS** config + **KeyCloak realm seed** (`kartova-web` public client). No new endpoints, no DTO changes. | Slice 4 is the integration probe. Mismatches discovered while building screens are **logged, not fixed** in this slice — that is the whole point of doing this slice. Fixes ride slice 5. |
| 7 | Tenant context display: read-only org name in the topbar, sourced from `GET /api/v1/organizations/me`. No tenant-switcher. | Single-tenant-per-JWT (ADR-0090). Switcher only makes sense once a user can belong to multiple tenants — out of MVP scope. |
| 8 | Routing: `/` → redirect `/catalog`, `/catalog`, `/catalog/applications/:id`, `/callback` (OIDC). Anything else → 404 page. | Smallest router surface that supports the slice. |
| 9 | Mockup-to-component fidelity rule: nav shell follows `master_shell_expanded` + `DESIGN.md` (canonical, ADR-0088). Modal content follows `create-application/`. The sidebar variant in `create-application/screen.png` (Sovereign Architect / System Core / Services-Dependencies-Infrastructure) is **ignored** as a Stitch nav drift. | ADR-0088: "nav canonical in DESIGN.md (not Stitch)." |
| 10 | ProblemDetails (RFC 7807) is the assumed 400 error contract. SPA expects `errors: { fieldName: [messages] }`. If actual shape differs, it's the first concrete backlog item slice-4 surfaces. | Slice 1 spec mandated RFC 7807; slice 4 is the first consumer. |

---

## 4. Architecture

### 4.1 Frontend module layout

```
web/src/
  app/
    router.tsx                      # react-router 7 routes
    providers.tsx                   # QueryClientProvider, AuthProvider
  features/
    catalog/
      api/
        client.ts                   # openapi-fetch instance + auth interceptor
        applications.ts             # useApplications, useApplication, useRegisterApplication
      components/
        ApplicationsTable.tsx
        RegisterApplicationDialog.tsx
        ApplicationDetail.tsx
      pages/
        CatalogListPage.tsx
        ApplicationDetailPage.tsx
      schemas/
        registerApplication.ts      # zod
  shared/
    auth/
      authConfig.ts                 # OIDC config
      AuthProvider.tsx              # wraps react-oidc-context
      RequireAuth.tsx               # route guard
      useCurrentUser.ts             # JWT profile claims helper
    layout/
      MasterShell.tsx
      Topbar.tsx
      Sidebar.tsx
    ui/                             # shadcn primitives (existing dir)
  generated/
    openapi.ts                      # codegen output — .gitignored
```

### 4.2 Endpoint topology (unchanged from slice 3)

```
GET   /api/v1/version                      (system, anonymous)
GET   /api/v1/organizations/me             (tenant-scoped) — used by topbar
POST  /api/v1/admin/organizations          (admin) — not consumed by slice 4

POST  /api/v1/catalog/applications         (tenant-scoped) — RegisterApplicationDialog
GET   /api/v1/catalog/applications/{id}    (tenant-scoped) — ApplicationDetailPage
GET   /api/v1/catalog/applications         (tenant-scoped) — CatalogListPage
```

### 4.3 Auth flow (SPA-direct OIDC + PKCE)

```
SPA boot → AuthProvider (react-oidc-context)
        → silent SSO check
        → if not authed: redirect to KeyCloak /auth?response_type=code&pkce
        → user logs in → callback /callback?code=...
        → tokens stored in memory (NOT localStorage)
        → openapi-fetch interceptor reads access_token → Bearer header on every API call
        → silent token refresh on near-expiry
        → 401 from API → AuthProvider triggers re-login
```

OIDC config (dev): `authority=http://keycloak:8080/realms/kartova`, `client_id=kartova-web`, `redirect_uri=http://localhost:5173/callback`, `scope="openid profile email"`, public client + PKCE. Token storage: `WebStorageStateStore({ store: in-memory })` — never `localStorage` or `sessionStorage`.

### 4.4 OpenAPI codegen pipeline

- Backend emits `/openapi/v1.json` at runtime (slice 1).
- `web/scripts/codegen.mjs` fetches from `${VITE_API_BASE_URL}/openapi/v1.json` (default `http://localhost:5080`) and runs `openapi-typescript` → `web/src/generated/openapi.ts`.
- `package.json` scripts: `"codegen": "node scripts/codegen.mjs"`, `"predev": "npm run codegen"`, `"prebuild": "npm run codegen"`.
- `web/src/generated/` is `.gitignore`d. CI runs codegen against the freshly built backend container before `vite build`.
- Failure mode: if backend isn't reachable during codegen, fall back to a checked-in `web/openapi-snapshot.json` and warn — keeps offline development unblocked.

### 4.5 Backend changes

Two and only two:

1. **CORS** — `Kartova.Api` adds `app.UseCors("KartovaWeb")` policy that allows configured origins (dev: `http://localhost:5173`; prod: read from config). Covered by `Preflight_FromConfiguredOrigin_AllowsRequest` and `Preflight_FromUnknownOrigin_DoesNotEchoOrigin` integration tests.
2. **KeyCloak realm seed** — add `kartova-web` public client with `redirect_uris: ["http://localhost:5173/callback"]`, `web_origins: ["+"]`, PKCE required, no client secret. Covered by `KartovaWebClientIsRegistered` realm-import test.

No new endpoints. No new Contracts. No DTO changes.

---

## 5. Components & data flow

### 5.1 Component-to-mockup mapping

| Component | shadcn / Radix primitives | Source mockup |
|---|---|---|
| `MasterShell` | custom `Sidebar` + grid layout | `master_shell_expanded/` + `DESIGN.md` |
| `Topbar` | `Avatar`, `DropdownMenu`, `Button` | `master_shell_expanded/` |
| `ApplicationsTable` | `Table`, `Badge`, `Skeleton` | `catalog_home_dashboard_fixed_navigation/` (entity row pattern; full dashboard widgets deferred) |
| `RegisterApplicationDialog` | `Dialog`, `Form`, `Input`, `Textarea`, `Button`, `Badge` | `create-application/` (modal content only) |
| `ApplicationDetail` | `Card`, `Badge`, `Separator` | `entity_detail_payment_gateway_navigation_sync/` (header + metadata block only; tabs deferred) |

shadcn components added to `web/src/components/ui/`: `dialog`, `form`, `input`, `textarea`, `table`, `badge`, `card`, `separator`, `skeleton`, `dropdown-menu`, `avatar`, `sonner`, `tooltip`. Each scaffolded via `npx shadcn add <name>`.

### 5.2 Routing

```
/                          → redirect to /catalog
/callback                  → OIDC callback handler
/catalog                   → CatalogListPage         (RequireAuth)
/catalog/applications/:id  → ApplicationDetailPage   (RequireAuth)
*                          → NotFoundPage
```

### 5.3 Register happy path

```
User clicks "+ Register Application" in CatalogListPage header
  → RegisterApplicationDialog opens
  → Form (react-hook-form + registerApplicationSchema)
       Name (required, kebab-case, 1–64 chars)
       DisplayName (required, 1–128 chars)
       Description (optional, 0–512 chars)
       Owner pre-filled from useCurrentUser() (read-only pill)
       Lifecycle = Active (read-only badge)
  → Submit → useRegisterApplication() mutation
            → openapi-fetch POST /api/v1/catalog/applications
            → Bearer token attached by interceptor
  → 201 → invalidate ['applications'] query
        → toast "Application registered"
        → dialog closes
        → table re-fetches → new row visible
  → 400 (ProblemDetails) → field-level setError() calls; dialog stays open
  → 401 → AuthProvider triggers re-login (handled globally, no toast)
  → 5xx → sonner error toast; dialog stays open
```

### 5.4 Error & loading conventions

- **Loading:** `Skeleton` rows in tables; button spinner (lucide `Loader2`) inside dialogs. No global blocking spinner.
- **Validation errors (400):** field-level inline messages mapped from ProblemDetails `errors`.
- **Server errors (5xx):** `sonner` toast. Form/page state preserved.
- **Auth errors (401):** intercepted globally in `client.ts`. Never surface as toasts; trigger silent re-auth or KeyCloak redirect.
- **Empty states:** centered illustration + CTA ("No applications yet — register your first one").

---

## 6. Testing

### 6.1 Unit (Vitest) — runs in CI

- `useApplications`, `useApplication`, `useRegisterApplication`: assert query keys, request shape, cache invalidation on mutation success.
- `registerApplicationSchema` (zod): required-field rejection, kebab-case rule on Name, length bounds.
- `mapProblemDetailsToFormErrors` helper: given a ProblemDetails payload, expects matching `setError` calls.
- Auth interceptor (`client.ts`): Bearer header attached when token present; not attached when fetching `/openapi/v1.json`; 401 invokes the re-auth callback exactly once.

Coverage target: ≥ 80% lines on `web/src/features/catalog/api/`, `web/src/features/catalog/schemas/`, and `web/src/shared/auth/`. No coverage gate on visual components — Playwright MCP is the right verification layer.

### 6.2 Architecture & lint

- `tsc --noEmit` strict, zero errors.
- `eslint .` clean, zero warnings.
- New ESLint rule (custom or `no-restricted-imports`): forbid direct `fetch` outside `web/src/features/*/api/` and `web/src/shared/auth/` — forces all HTTP through the typed client.

### 6.3 Backend integration tests added

- `Preflight_FromConfiguredOrigin_AllowsRequest` — `OPTIONS` preflight from configured origin returns `Access-Control-Allow-Origin` echoing the origin.
- `Preflight_FromUnknownOrigin_DoesNotEchoOrigin` — `OPTIONS` preflight from an unconfigured origin does not echo `Access-Control-Allow-Origin`.
- `KartovaWebClientIsRegistered` — realm-import JSON contains `kartova-web` public client with PKCE required and the expected redirect URIs.

### 6.4 Manual verification (Playwright MCP — ADR-0084)

Cold-start `docker compose up`, then via MCP browser:

1. Navigate `http://localhost:5173` → redirected to KeyCloak → log in as `user@orga.kartova.local` → land on `/catalog`.
2. List shows empty state. Click "+ Register Application".
3. Submit empty form → field errors visible. Submit `payment-gateway` / `Payment Gateway` / description → toast + dialog closes + row appears in table.
4. Click row → `/catalog/applications/{id}` shows the same data; topbar shows the user's tenant name from `/organizations/me`.
5. Console clean (no errors/warnings); snapshots captured into PR description.
6. Negative path: refresh during a live session → still authed (silent SSO). Manually invalidate token in KeyCloak admin → next API call triggers re-login redirect.

Steps 1–6 are the **DoD point 5 evidence** for slice 4 (auth + HTTP + middleware wiring slice). Output captured in PR description.

### 6.5 Out of scope (captured as backlog — Section 7)

- Checked-in Playwright E2E suite.
- Visual regression (Chromatic / Percy).
- Accessibility audit.

---

## 7. Backlog additions

To be added to `docs/product/EPICS-AND-STORIES.md` and `docs/product/CHECKLIST.md` as un-ticked items:

1. **E-01.F-04.S-05 — BFF cookie-session auth (security hardening).**
   *Description:* Replace SPA-direct OIDC token handling with a backend-for-frontend pattern: ASP.NET sets an HttpOnly session cookie, exchanges cookie for JWT server-side when proxying to the API. Eliminates token exposure in browser memory; removes client-side refresh logic.
   *Acceptance:* SPA never sees the access token; cookie HttpOnly + Secure + SameSite=Lax; CSRF protection on state-changing requests; existing JWT bearer scheme remains for non-browser API clients (CLI, agents, webhooks).
   *Phase:* Post-MVP (or pulled forward if a security review demands).

2. **E-01.F-02.S-03 — End-to-end test infrastructure (checked-in Playwright suite).**
   *Description:* CI-friendly Playwright spec suite that boots `docker compose up` with seeded data, drives KeyCloak login → catalog flows → entity creation → detail navigation. Runs in GitHub Actions on PRs. Mirrors the backend integration tier and extends ADR-0083's five-tier pyramid on the frontend.
   *Acceptance:* `npm run test:e2e` runs locally and in CI; KeyCloak token bootstrapping handled via realm-admin API or test-only password grant; deterministic test data via per-run tenant; flaky-test budget ≤ 1%.
   *Phase:* Post-slice-4 (timing: when 2–3 stable flows exist worth automating).

3. **STITCH-PROMPTS.md Screen 10 parity note (bundled into slice 4, no story).**
   *Action:* Add a one-line consistency note to the Screen 10 prompt: "Modal content only is canonical; sidebar follows Screen 1 / DESIGN.md." Prevents future regenerations of `create-application/` from reintroducing the Sovereign-Architect sidebar drift.

---

## 8. Risks

| Risk | Mitigation |
|---|---|
| OpenAPI codegen requires a running backend during `npm run dev` — friction for frontend-only iteration. | Fallback to a checked-in `web/openapi-snapshot.json` on codegen fetch failure (warning only, not error). Snapshot regenerated whenever backend changes Contracts. |
| ProblemDetails shape from slice-3 may not match `errors: { fieldName: [messages] }`. | Treat as the **first concrete backlog item** slice 4 is meant to surface — log, don't fix in slice 4. Fix in slice 5 alongside `PUT` / lifecycle. |
| KeyCloak silent SSO across origins (KeyCloak `:8080`, SPA `:5173`) requires correct `web_origins` + check session iframe config. | Realm seed sets `web_origins: ["+"]` (mirror redirect_uris) for dev; integration test asserts the seed; manual Playwright check covers silent refresh. |
| Tailwind v4 + shadcn/ui (Tailwind v4 support is recent) — token mapping in `index.css` may need adjustment. | Pin shadcn CLI to a Tailwind v4-compatible version; verify each generated component renders in dark mode against `master_shell_expanded` mockup before composing pages. |
| Sidebar nav drift between mockups (ADR-0088 says DESIGN.md wins, but `create-application/` shows a different variant). | Decision #9 makes the rule explicit; `MasterShell.tsx` reads from a single canonical nav config; tests assert nav items are sourced from that config (no inline strings in `Sidebar.tsx`). |

---

## 9. Definition of Done

Slice 4 is "complete" only when **all** of the following are green and citable in the PR description:

1. Full solution + `web/` build with `TreatWarningsAsErrors=true` (.NET) and zero TS/ESLint warnings.
2. Per-task subagent reviews (spec-compliance + code-quality) executed for each task in the plan.
3. `superpowers:requesting-code-review` invoked at slice boundary against the full branch diff with this spec + plan as context.
4. Full test suite green: backend unit + architecture + integration (Testcontainers); frontend Vitest unit.
5. Playwright MCP verification (Section 6.4) executed against `docker compose up`, with screenshots and console-clean evidence captured in PR description.

Until all five are green, status is "implementation staged, verification pending" — never "slice 4 complete."
