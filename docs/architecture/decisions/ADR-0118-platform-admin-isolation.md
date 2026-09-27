# ADR-0118 — Platform admin isolation: separate realm, separate auth scheme, separate app

Status: Accepted
Date: 2026-09-25

## Context

E-01b (Platform Admin Console) needs an operator surface that administers organizations **across** tenants. Today the platform-operator identity is co-located with tenant identities:

- The `platform-admin` realm role and the `platform-admin@kartova.local` user live in the **tenant realm** `kartova` (ADR-0006: one realm for all tenants).
- The `kartova-admin` service account, driven by **tenant-initiated** flows (invite — `CreateInvitationHandler`, change role — `ChangeMemberRoleHandler`, offboard — `OffboardMemberHandler`), holds realm-wide user-management rights. The only barrier between a tenant OrgAdmin and granting `platform-admin` is the application-level allow-list `KartovaRoles.All` (`Invitation.cs`, `CreateInvitationHandler.cs`, `ChangeMemberRoleHandler.cs`). One missed check = privilege escalation to platform operator.
- A tenant flow can mutate an operator identity: `OffboardMemberHandler` hard-deletes the KeyCloak user (ADR-0102), so an OrgAdmin could delete an operator account that also belongs to their tenant.
- The admin API (`MapAdminModule`, `/api/v1/admin/*`, ADR-0092) authenticates with the same `JwtBearer` scheme as tenant routes and discriminates by role only.
- The SPA keeps tokens in `sessionStorage` (ADR-0116); tenant users author content rendered in the SPA (descriptions, docs in Phase 3). An XSS on a shared origin rides — or exfiltrates — an operator session.

An in-SPA `/admin` area was designed first (spec `2026-09-25-e01b-admin-console-shell-design.md`) and rejected in brainstorming: it mixes tenant and operator concepts, and the worst outcome — a non-operator acting in operator context — would be guarded by a single layer.

## Decision

Isolate the platform operator at three independent layers.

1. **Identity — separate KeyCloak realm `kartova-platform`** for platform operators only. The tenant realm `kartova` loses the `platform-admin` role and user. The tenant service account `kartova-admin` has **no** rights in `kartova-platform`. An operator who is also a product user holds **two accounts** (one per realm); mixed tenant+operator accounts do not exist.
2. **API — separate `JwtBearer` scheme `PlatformAdmin`** (issuer = `kartova-platform`, own audience). The `/api/v1/admin/*` group (`MapAdminModule`) and `/health/detailed` accept **only** this scheme plus the operator role. Tenant routes stay on the default (tenant) scheme. A tenant token is rejected (401) on admin routes and an operator token is rejected on tenant routes, independent of any role claim. Same `Kartova.Api` process; no separate host.
3. **UI — separate app `web-admin/` on its own origin**, with its own public OIDC client (PKCE) in `kartova-platform`, own image and Helm release. CORS for `/api/v1/admin/*` admits only the `web-admin` origin.
4. **MFA (OTP) required** in `kartova-platform` in every non-dev environment. The dev realm import does not enforce it.

## Consequences

- ➕ Escalating from tenant to operator requires crossing a realm boundary and a scheme boundary, not a single allow-list bug.
- ➕ Tenant flows cannot create, grant, or delete operator identities by construction.
- ➕ A tenant-origin XSS cannot reach the operator session (different origin, different storage, different token issuer).
- ➕ No mixed-account role resolution (`SessionStartHandler` single-role assumption stays valid for the tenant realm).
- ➖ Second realm to maintain across docker-compose, Testcontainers fixtures, Helm, and realm-seed arch tests.
- ➖ Second frontend app (build, image, CI job, E2E target).
- ➖ Operator-developers juggle two accounts.
- Amends ADR-0006 ("one realm for all tenants" holds for tenants; operators live in a separate realm).
- Amends ADR-0092 (`/api/v1/admin/*` is bound to the `PlatformAdmin` scheme, not just the role).
- Contextualized by ADR-0116 (token-in-browser risk motivates the separate origin).
- Rollout: sub-slice S1 (realm + scheme + `/api/v1/admin/session/me` + migration of existing admin endpoints + arch test that every `/api/v1/admin/*` endpoint binds the `PlatformAdmin` scheme), S2 (`web-admin` scaffold), then E-01b features.
