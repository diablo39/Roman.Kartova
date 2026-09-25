# E-01b.F-03.S-01 — Platform Admin Console Shell — Design

**Story:** E-01b.F-03.S-01 (new — added by this slice to `docs/product/phases/phase-0-foundation.md`, `EPICS-AND-STORIES.md`, `CHECKLIST.md`)
**ADRs:** [ADR-0092](../../architecture/decisions/ADR-0092-rest-api-url-convention.md) (`/api/v1/admin/*` gated by `platform-admin`), [ADR-0084](../../architecture/decisions/ADR-0084-playwright-mcp-for-frontend-development.md) (browser verification), [ADR-0094](../../architecture/decisions/ADR-0094-untitled-ui-component-library.md) (Untitled UI)
**Date:** 2026-09-25
**Status:** draft

## Context

E-01b (Platform Admin Console) has 7 stories, all unbuilt. Backend admin plumbing exists — `platform-admin` realm role, `MapAdminModule` (`/api/v1/admin/{slug}`, `RequireRole(PlatformAdmin)`), `POST /api/v1/admin/organizations` — but there is **no admin UI** and no way for a platform admin to land anywhere in the SPA:

- The OIDC callback (`OidcCallbackHandler`) always runs the tenant session bootstrap (`POST /api/v1/auth/session`). A tenantless platform admin (`platform-admin@kartova.local`, no `tenant_id`) fails it → `/login-error`.
- `AppLayout` / `Sidebar` / `TopBar` are tenant-coupled (`usePermissions`, `useOrgProfile`).

The owner also wants **one account that is both a tenant user and a platform admin** (track own systems in the catalog + administer the platform). That exposes a latent bug: `SessionStartHandler.cs:83` resolves the tenant role as `_tenant.Roles.FirstOrDefault()` over *all* realm roles; with `[platform-admin, OrgAdmin]` it can pick `platform-admin` → `users.realm_role = "platform-admin"`, empty permission set → broken tenant session.

Verified safe (no change): `KeycloakRoleChange.RolesToRemove` only strips roles in `KartovaRoles.All`, so an OrgAdmin changing a member's role never removes `platform-admin`; `platform-admin` has no entry in `KartovaRolePermissions.Map`, so it grants nothing inside a tenant.

## Scope

**In:** URL-split entry to an admin console shell (`/admin`), admin identity endpoint, access-denied surface, admin layout with placeholder nav, mixed-account role-resolution fix, dev fixture user for the mixed account, backlog entry for this story.

**Out:** any admin business feature (org directory etc. — E-01b.F-01/F-02); a "Platform Admin" link in the tenant user menu (would need an `/admin/me` probe on every tenant page load — the URL is the switch); redirecting a tenantless admin who opens `/` or `/catalog` (keeps today's `/login-error`); impersonation / entering a tenant as admin.

## Decisions (confirmed in brainstorming 2026-09-25)

1. Same SPA (`web/`), routes under `/admin/*` — no separate app/image/helm.
2. **Mode is chosen by entry URL, not by role.** `/admin/**` → login → admin access check → admin shell or access-denied. Everything else → today's tenant flow, unchanged.
3. Access check is server-authoritative: `GET /api/v1/admin/me`.
4. Visual: reuse the master-shell skeleton (sidebar + top bar) with admin nav + "Platform Admin" badge; no new Stitch mockup.
5. Mixed account after login via a tenant URL lands on the tenant (`/catalog` or `returnTo`) as today.

## Components

### Backend

| File | Responsibility |
|---|---|
| `src/Kartova.Api/Admin/AdminMeEndpoint.cs` (new) | `GET /api/v1/admin/me` → `AdminMeResponse`. Built from JWT claims only (`sub`, `email`, `name`), no DB, no tenant scope. Mapped system-level in `Program.cs` next to `/health/detailed` (admin identity is platform-level, not Organization-module-owned); `.RequireAuthorization(p => p.RequireRole(KartovaRoles.PlatformAdmin))` — same gate as `ModuleRouteExtensions.MapAdminModule`. Missing `sub` → 401 (not a valid principal). |
| `src/Kartova.Api/Admin/AdminMeResponse.cs` (new) | `record AdminMeResponse(Guid UserId, string Email, string DisplayName)`, `[ExcludeFromCodeCoverage]`. `DisplayName` falls back to `Email` when `name` is absent. |
| `src/Modules/Organization/Kartova.Organization.Infrastructure/SessionStartHandler.cs` (modify, line 83) | Role = first of `_tenant.Roles` contained in `KartovaRoles.All`; none → `KartovaRoles.Viewer` (unchanged default). |
| `deploy/keycloak/kartova-realm.json` (modify) | New dev user `platform-dev@orga.kartova.local` — `tenant_id` = Org A, realm roles `Member` + `platform-admin`, `dev_password_12`. A new user rather than adding the role to `admin@orga`, so existing tests asserting `admin@orga` is *not* a platform admin keep their meaning. No DevSeed change: session bootstrap upserts the `users` row on first tenant login. |

### Frontend

| File | Responsibility |
|---|---|
| `web/src/features/auth/components/OidcCallbackHandler.tsx` (modify) | If `returnTo` starts with `/admin` (segment match: `/admin` or `/admin/…`, not `/administrator`) → `navigate(returnTo)` **without** session bootstrap. Else unchanged. |
| `web/src/features/admin/api/adminMe.ts` (new) | `useAdminMe()` — TanStack Query over `GET /api/v1/admin/me` via the generated client (regenerate OpenAPI types). No retry on 401/403. |
| `web/src/features/admin/components/RequireAdmin.tsx` (new) | Loading → spinner; 200 → children; 403 → `<AdminAccessDenied/>`; other error → retryable error panel ("Couldn't verify admin access"). |
| `web/src/features/admin/pages/AdminAccessDenied.tsx` (new) | "You don't have access to the Platform Admin console." Actions: **Go to Kartova** (`/catalog`), **Sign in with a different account** (`signoutRedirect` then return to `/admin`). |
| `web/src/features/admin/layout/AdminLayout.tsx`, `AdminSidebar.tsx`, `AdminTopBar.tsx` (new) | Same skeleton/classes as `AppLayout`. Sidebar: "Kartova" + "Platform Admin" badge; nav: `Overview` (`/admin`), `Organizations` (disabled). Footer link **Back to Kartova** (`/catalog`, static). TopBar: badge, user menu (display name from `useAdminMe`, sign out). No `useOrgProfile` / `usePermissions`. |
| `web/src/components/layout/sidebar-nav.tsx` (new, extracted) | Move `NavGroup`, `NavItemLink`, `DisabledItem` out of `Sidebar.tsx` so both sidebars share them. Pure move — no behavior change. |
| `web/src/features/admin/pages/AdminOverviewPage.tsx` (new) | Landing: heading "Platform Admin", signed-in-as line, short note that organization management is coming. |
| `web/src/app/router.tsx` (modify) | `<Route element={<AdminShell/>}>` with `/admin` → `AdminOverviewPage`; `AdminShell = RequireAuth > RequireAdmin > AdminLayout`. Unknown `/admin/*` → redirect `/admin`. |

## Data flow

- **Cold entry `/admin`:** `RequireAuth` → `signinRedirect({ returnTo: "/admin" })` → KC → `/callback` → `OidcCallbackHandler` sees admin `returnTo` → `/admin` → `RequireAdmin` → `GET /api/v1/admin/me` → 200 shell | 403 access-denied.
- **Already signed in** (e.g. tab on `/catalog`) → opening `/admin` skips login, goes straight to `RequireAdmin`.
- **Tenant entry:** unchanged (session bootstrap → `/welcome` | `returnTo` | `/catalog`).

## Error handling

- `/admin/me`: anonymous → 401, authenticated without `platform-admin` → 403 (existing auth pipeline, ProblemDetails).
- `RequireAdmin` distinguishes 403 (access denied, terminal) from network/5xx (retry panel) — a 5xx must never render as "no access".
- 401 inside the admin shell (expired token) → existing OIDC renewal / re-login path; no admin-specific handling.
- `returnTo` still validated by `resolveReturnTo` (open-redirect guard) before the admin-prefix check.

## Testing (per docs/TESTING-STRATEGY.md)

Wiring slice (HTTP + auth) → real seam: `Kartova.Api.IntegrationTests`, `KartovaApiFixtureBase` + real KeyCloak JWT, real Postgres.

Gate-3 artifacts (deliverables):
- `AdminMeEndpointTests` — platform-admin (tenantless) → 200 + body shape; mixed user `platform-dev@orga` → 200; OrgAdmin `admin@orga` → 403; anonymous → 401.
- `SessionBootstrapTests` (extend) — mixed user → session 200, `role = Member`, permission set equals `KartovaRolePermissions.ForRole(Member)`; `users.realm_role = 'Member'`.
- Unit (`SessionStartHandlerTests`, extend) — role order `[platform-admin, OrgAdmin]` and `[OrgAdmin, platform-admin]` both → `OrgAdmin`; `[platform-admin]` only → `Viewer`.
- Vitest — `OidcCallbackHandler`: `/admin`, `/admin/x` skip bootstrap; `/administrator`, `/catalog`, undefined run it. `RequireAdmin`: 200 / 403 / 500 branches. `AdminLayout`: nav + Back-to-Kartova link, Organizations disabled. `Sidebar` regression tests still green after extraction.
- `tsc -b` per frontend task.

E2E-impact trigger: the callback is traversed by every login in `e2e/` → run the auth-touching specs locally (gate 9), and add `e2e/admin-shell.spec.ts`: mixed user opens `/admin` → shell; OrgAdmin opens `/admin` → access denied; mixed user logs in via `/catalog` → tenant catalog.

Gate 4: `images` job N/A unless a Dockerfile / restore surface changes (none expected). Realm JSON change affects KC containers in tests, not image builds.

## Impact Analysis (LSP)

To be grounded in the plan: `SessionStartHandler.HandleAsync` behavior change (role resolution) — `findReferences`/`incomingCalls` for its callers; `KartovaRoles.All` is a `static readonly` set (LSP-queryable); `KartovaRoles.PlatformAdmin` is `const` → grep. Frontend changes are TS (no LSP) → grep `OidcCallbackHandler`, `NavItemLink`, `DisabledItem`, `NavGroup` consumers.

## Size

~250–300 lines production code (backend ~60, frontend ~200–240 incl. the pure extraction). Within the ~400 target.

## Out of scope / follow-ups

- E-01b.F-01 (org directory) replaces the disabled "Organizations" item.
- Optional later: "Platform Admin" entry in tenant user menu; friendly landing for a tenantless admin opening `/` instead of `/login-error`.
