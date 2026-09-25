# E-01b.F-03.S-01 — Platform Admin Identity & API Isolation — Design

**Story:** E-01b.F-03.S-01 (new — added by this slice to `docs/product/phases/phase-0-foundation.md`, `EPICS-AND-STORIES.md`, `CHECKLIST.md`, together with E-01b.F-03.S-02 `web-admin` app shell)
**ADRs:** [ADR-0118](../../architecture/decisions/ADR-0118-platform-admin-isolation.md) (realized by this slice), [ADR-0092](../../architecture/decisions/ADR-0092-rest-api-url-convention.md) (amended), [ADR-0006](../../architecture/decisions/ADR-0006-keycloak-as-identity-provider.md) (amended), [ADR-0116](../../architecture/decisions/ADR-0116-spa-holds-tokens-bff-deferred.md)
**Date:** 2026-09-25
**Status:** draft

## Context

ADR-0118 isolates platform operators from tenants at three layers: a separate KeyCloak realm, a separate API auth scheme, and a separate frontend app. This slice is **S1**, the backend half: identity + API. S2 (`web-admin`) builds on it.

Today the `platform-admin` role and the `platform-admin@kartova.local` user live in the tenant realm `kartova`. `/api/v1/admin/*` (`MapAdminModule`) and `/health/detailed` accept any token from the single `JwtBearer` scheme that carries the role. So a tenant-realm token with `platform-admin` passes. The tenant service account `kartova-admin` can grant that role, guarded only by the `KartovaRoles.All` allow-list.

Existing admin-API consumers: `POST /api/v1/admin/organizations` (`OrganizationAdminModule`, `AdminBypassTests`, `EndpointRouteRules`) and `/health/detailed` (`HealthCheckEndpointTests`). Most integration tests sign tokens with `TestJwtSigner`. Live-KC tests use `KeycloakContainerFixture`, which runs `--import-realm` over `/opt/keycloak/data/import/*.json`. `KeycloakRealmSeedRules` asserts that every `KartovaRoles` constant exists in `kartova-realm.json`.

## Scope

**In:**
- `kartova-platform` realm (dev import) and removal of `platform-admin` from `kartova`.
- `PlatformAdmin` `JwtBearer` scheme + `PlatformAdminPolicy`.
- `MapAdminModule` and `/health/detailed` bound to that policy.
- `GET /api/v1/admin/session/me`.
- Compose/test-fixture wiring. Helm is unchanged, see Components.
- Arch rules.
- Production realm runbook in `deploy/README.md`.
- Backlog entries.

**Out:**
- `web-admin` app (S2).
- CORS policy for the admin origin (S2, once the origin exists).
- OTP enforcement in the import file (dev stays without MFA; prod configured per runbook).
- E-01b.F-01/F-02 features.

## Decisions (confirmed in brainstorming 2026-09-25)

1. Admin API stays in the `Kartova.Api` process on a separate scheme. No separate host.
2. `/api/v1/admin/**` accepts **only** the `PlatformAdmin` scheme. The default scheme stays tenant-only. Isolation is by issuer/audience, not by role.
3. Admin identity endpoint is module-owned: `OrganizationAdminModule` maps a second group `app.MapAdminModule("session")` → `GET /api/v1/admin/session/me`. This is a non-slug group, with precedent in `/api/v1/auth/session`.
4. The old in-SPA shell spec (`2026-09-25-e01b-admin-console-shell-design.md`) is deleted, superseded by ADR-0118 (history kept in git).
5. **S2 constraint:** `web-admin` must reuse the tenant app's shell visually: the same sidebar + top-bar skeleton, tokens and components (master_shell), with admin nav. The sharing mechanism (a second Vite entry in `web/` vs a shared workspace package) is decided in S2's brainstorming.

## Components

### Identity (KeyCloak)

| File | Change |
|---|---|
| `deploy/keycloak/kartova-platform-realm.json` (new) | Realm `kartova-platform` contents: <ul><li>realm role `platform-admin`;</li><li>user `platform-admin@kartova.local` / `dev_password_12` with that role;</li><li>public client `kartova-admin-web` (standard flow + PKCE S256; redirect URIs for local S2 origin placeholder `http://localhost:5174/*`);</li><li>audience mapper → `kartova-admin-api`;</li><li>`directAccessGrantsEnabled` only on a dev/test client `kartova-admin-test` used by live-KC tests.</li></ul> **No** service account; **no** grants for `kartova-admin`. Token settings mirror the `kartova` hardening (short access TTL, refresh rotation + reuse revocation). |
| `deploy/keycloak/kartova-realm.json` (modify) | Remove realm role `platform-admin` and user `platform-admin@kartova.local`. |
| `docker-compose.yml` (modify) | Mount `kartova-platform-realm.json` into `/opt/keycloak/data/import/`. |
| `tests/Kartova.Testing.Auth/KeycloakContainerFixture.cs` (modify) | Map both realm files into the import dir. Wait strategy covers both realms' discovery docs. Expose `PlatformKeycloakAuthority`. |
| `deploy/README.md` (modify) | Runbook for the `kartova-platform` production realm: <ul><li>create it manually (the import applies only on fresh installs);</li><li>OTP as a required action + browser flow requiring OTP;</li><li>no rights for the `kartova-admin` service account;</li><li>token hardening parity;</li><li>remove `platform-admin` from a pre-existing `kartova` realm.</li></ul> |

### API

| File | Change |
|---|---|
| `src/Kartova.SharedKernel.AspNetCore/AuthenticationConfigKeys.cs` (modify) | Keys `Authentication:PlatformAdmin:Authority` / `:Audience` / `:MetadataAddress`. |
| `src/Kartova.SharedKernel.AspNetCore/JwtAuthenticationExtensions.cs` (modify) | Second `.AddJwtBearer(PlatformAdminAuth.Scheme, …)` with the same validation as the tenant scheme: issuer, audience and lifetime validated, `ClockSkew` 30 s, `MapInboundClaims = false`, `RequireHttpsMetadata` shared. Missing authority/audience → `InvalidOperationException` at startup. Registers `PlatformAdminPolicy` = `AuthenticationSchemes = [PlatformAdmin]` + `RequireAuthenticatedUser` + `RequireRole(KartovaRoles.PlatformAdmin)`. |
| `src/Kartova.SharedKernel.AspNetCore/PlatformAdminAuth.cs` (new) | `const` scheme name `"PlatformAdmin"` + policy name. |
| `src/Kartova.SharedKernel.AspNetCore/ModuleRouteExtensions.cs` (modify) | `MapAdminModule` → `.RequireAuthorization(PlatformAdminAuth.Policy)` (replaces the role-only lambda). |
| `src/Kartova.Api/Program.cs` (modify) | `/health/detailed` → `.RequireAuthorization(PlatformAdminAuth.Policy)`. |
| `src/Modules/Organization/Kartova.Organization.Infrastructure.Admin/OrganizationAdminModule.cs` (modify) | `app.MapAdminModule("session").MapGet("/me", AdminSessionEndpointDelegates.GetMe).WithName("AdminGetSessionMe")`. |
| `…/Infrastructure.Admin/AdminSessionEndpointDelegates.cs` (new) | Builds `AdminMeResponse` from claims `sub`, `email`, `name` (fallback: email). No DB, no tenant scope. A missing or non-GUID `sub` → 401 ProblemDetails. |
| `src/Modules/Organization/Kartova.Organization.Contracts/AdminMeResponse.cs` (new) | `record AdminMeResponse(Guid UserId, string Email, string DisplayName)`, `[ExcludeFromCodeCoverage]`. |
| `src/Kartova.Api/appsettings.Development.json`, `docker-compose.yml` api env (modify) | `Authentication:PlatformAdmin:*` wiring (issuer `http://localhost:8180/realms/kartova-platform`, in-container metadata via `http://keycloak:8080`, audience `kartova-admin-api`). Helm: the chart carries **no** `Authentication:*` env today, not even for the tenant scheme; production supplies both via external config. So the chart stays unchanged, and `deploy/README.md` documents the three new required keys. |
| `web/openapi-snapshot.json` (regenerate) | New endpoint appears in the snapshot. No frontend consumer in S1. |

`KartovaRoles.PlatformAdmin` stays (`"platform-admin"`) as the role name in the new realm. `TenantClaimsTransformation` is unchanged: an operator principal has no `tenant_id` → `TenantId.Empty`, and `platform-admin` maps to no tenant permissions.

## Data flow

- `kartova-platform` token → `/api/v1/admin/**` → `PlatformAdmin` scheme validates → policy checks role → handler.
- `kartova-platform` token → a tenant route → default scheme rejects the issuer → **401**.
- `kartova` token (even with a forged or leftover `platform-admin` role) → `/api/v1/admin/**` → the `PlatformAdmin` scheme rejects the issuer → **401**.

## Error handling

- 401: anonymous, wrong issuer, wrong audience, or expired token. ProblemDetails from the existing auth pipeline.
- 403: valid `kartova-platform` token without the `platform-admin` role.
- Startup: missing `Authentication:PlatformAdmin:Authority`/`Audience` → fail fast (matches the tenant scheme).

## Testing (per docs/TESTING-STRATEGY.md)

This is a wiring slice (HTTP + auth), so tests use the real seam: `KartovaApiFixtureBase` + real `JwtBearer`, and real Postgres where the route touches the DB.

Gate-3 deliverables:
- **`TestJwtSigner` extension:** a second issuer/audience + `IssueForPlatformAdmin(roles?)`. The fixture sets `Authentication__PlatformAdmin__*` to it.
- **`AdminSchemeIsolationTests` (new, `Kartova.Organization.IntegrationTests`, which owns the admin routes and has the `TestJwtSigner` fixture; live-KC variants in `Kartova.Api.IntegrationTests/PlatformRealmLiveTokenTests`):**
  - platform token → `/api/v1/admin/session/me` 200 + body shape;
  - **tenant token carrying `platform-admin` → `/api/v1/admin/session/me` 401** (key regression);
  - platform token → a tenant route (`/api/v1/catalog/applications`) 401;
  - platform token without the role → 403;
  - anonymous → 401.
  - 401 cases are multi-cause, so assert the response discriminator (`WWW-Authenticate` error / ProblemDetails type), not the status alone.
- **Live KC (`KeycloakContainerFixture`):** password grant on `kartova-admin-test` in `kartova-platform` → `/api/v1/admin/session/me` 200. Proves the real issuer and the audience mapper from the realm file.
- **`AdminBypassTests` + `HealthCheckEndpointTests` (migrate):** switch to platform tokens. Add a tenant-token-with-`platform-admin` → 401 case to each.
- **Unit:** `AdminSessionEndpointDelegates` claim mapping — name fallback, missing/invalid `sub`.
- **Arch (`EndpointRouteRules`):**
  - every endpoint under `/api/v1/admin/` carries `PlatformAdminPolicy`;
  - no endpoint outside that prefix references the `PlatformAdmin` scheme or policy.
- **Arch (`KeycloakRealmSeedRules`):**
  - `PlatformAdmin` is validated against `kartova-platform-realm.json`, the other roles against `kartova-realm.json`;
  - assert `kartova-realm.json` contains no `platform-admin` role or user.

Gate 4: applies. `docker-compose.yml` mounts change; no Dockerfile or restore change, but the compose surface changes → run the `images` job.
Gate 9: live stack. `curl` with tokens from each realm against the admin and tenant routes. Request/response saved in `verification/`.
E2E-impact: N/A. A grep of `e2e/` (2026-09-25) finds no `platform-admin@kartova.local` / `/api/v1/admin` usage; re-check in the plan.

## Impact Analysis (LSP)

To be grounded in the plan:
- **`MapAdminModule`:** behavior change (policy instead of role lambda) → `findReferences` for all admin groups.
- **`JwtAuthenticationExtensions.AddKartovaJwtAuth`:** callers → `incomingCalls`.
- **`TestJwtSigner`:** consumers → `findReferences` (the fixture base and its derivatives).
- **`KartovaRoles.PlatformAdmin`:** `const` → grep (23 files matched `api/v1/admin|platform-admin|KartovaRoles.PlatformAdmin` at design time).

## Size

~200–300 lines of production code (auth extension, policy/const, route extension, endpoint + DTO, config). Realm JSON, Helm values and tests are excluded. Within the ~400 target.

## Definition of Done

Per CLAUDE.md's ten gates. Ledger: `docs/superpowers/verification/2026-09-25-e01b-s1-platform-admin-isolation/dod.md`.

## Follow-ups

- **S2 (E-01b.F-03.S-02):** `web-admin` app on its own origin — the same visual shell as the tenant app, access-denied surface, landing, image, Helm, CI job, CORS for `/api/v1/admin/*`.
- E-01b.F-01 / F-02 on top of S1 + S2.
