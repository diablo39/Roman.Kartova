# Invitation set-password flow (design)

**Date:** 2026-06-01
**Author:** Roman Głogowski (AI-assisted)
**Status:** Draft, pending user review
**Builds on:** Slice 9 — Organization & people management ([2026-05-27-slice-9-organization-people-management-design.md](2026-05-27-slice-9-organization-people-management-design.md)). Closes the invitation-acceptance gap that slice 9 left open (slice-9 Decision #4 / #7: copy-link URL exists, but the invitee has no way to obtain a credential).
**Stories:** completes E-03.F-01.S-02 (invite users with roles) — the *accept* half.
**ADRs referenced:** ADR-0006 (KeyCloak as IdP), ADR-0082 (modular monolith), ADR-0090 (tenant scope + BYPASSRLS pool), ADR-0091 (ProblemDetails), ADR-0092 (REST URL convention), ADR-0097 (five-tier testing), ADR-0098 (UUID-only entity identifier), ADR-0100 (strict one-email-per-tenant).

---

## 1. Problem

Slice 9 provisions a KeyCloak user at invite time (`CreateInvitationHandler` → `kc.CreateUserAsync`) with **no password** and a pending `UPDATE_PASSWORD` required action, and surfaces a copy-link URL of the form:

```
{FrontendBaseUrl}/?invitation=1&email=<email>
```

Two problems make the feature non-functional and leak data:

1. **No credential path.** The invited account has no password. In production KeyCloak would email a set-password link, but the dev realm has `smtpServer: null` and `registrationAllowed: false` — so the invitee can neither be emailed a link nor self-register. There is no way to log in, therefore no way to accept. (Acceptance itself works once login succeeds: `SessionStartHandler` flips `Pending → Accepted` keyed on `KeycloakUserId == sub`.)
2. **Email in the URL.** The invitee's email is a query parameter — it lands in browser history, `Referer` headers, and server access logs.

This design closes both: the invitee sets a password on a **Kartova-hosted page**, and the link carries an **opaque random token** instead of the email.

## 2. Confirmed decisions

| # | Decision |
|---|----------|
| D1 | **Login model:** after setting the password, redirect into the standard Keycloak OIDC login with `login_hint=email`. The invitee types the password once. No ROPC/direct-grant; MFA-capable; Kartova handles the password only on one TLS `POST`. |
| D2 | **Link delivery:** copy-link only (the existing `CopyInviteLinkBox`). No SMTP/email in this slice — email delivery is a later enhancement (E-06a). |
| D3 | **Accept page collects:** password + re-type (confirm) + display name. Confirm is a client-side typo guard only and is **not** sent to the backend. |
| D4 | **Token, not Id.** A dedicated, high-entropy token — *not* the invitation's GUID `Id`. Rationale: the `Id` is an identifier that already leaks into admin responses, the revoke URL, and logs; it cannot be hashed-at-rest (it is the PK) and cannot be burned on use. `Guid.NewGuid()` is also not a contractual CSPRNG. Identifier ≠ credential. |
| D5 | **Token in the URL query** (`?token=`), mitigated by `Referrer-Policy: no-referrer` on the page, access-log query scrubbing on the route, single-use, and the 7-day expiry. (The fragment+POST hardening variant was considered and declined.) |
| D6 | **Acceptance flip stays at first login** (`SessionStartHandler`). The accept endpoint never touches `Status`; it only provisions the credential/name and burns the token. Minimal blast radius on the working `/welcome` path. |

## 3. Approach (chosen: A)

Kartova-owned opaque token → Kartova set-password page → KeyCloak admin `reset-password`, then redirect to normal OIDC login. Rejected alternatives: **B** Keycloak native action-token / `execute-actions-email` (requires SMTP — violates D2 — and a KC-hosted page that can't capture a display name or be Kartova-branded — violates D3); **C** hybrid bounce to a KC-minted set-password action (same KC-hosted-UI limitations as B).

## 4. Data model & token lifecycle

`Invitation` aggregate (`Kartova.Organization.Domain`) gains two fields:

| Field | Type | Purpose |
|-------|------|---------|
| `TokenHash` | `string?` | base64url(SHA-256(token)). The only persisted form of the token; nulled on use. |
| `CredentialSetAt` | `DateTimeOffset?` | Stamped when the invitee sets their password — single-use guard + audit. |

- **Issuance** (in `CreateInvitationHandler`, *before* the DB row is built — the hash is a column on the row): `RandomNumberGenerator.GetBytes(32)` → base64url = plaintext token (~43 chars, 256-bit); compute `TokenHash` and pass it into `Invitation.Create`, so it persists in the same `SaveChangesAsync` as the rest of the row. The plaintext is held in a local only, returned **once** in the create response after persistence succeeds, and is never stored or logged. Deterministic hashing via a shared helper `InvitationToken.Hash(string)` used by both issuance and validation.
- **Link** changes to `{FrontendBaseUrl}/accept-invitation?token=<plaintext>` — no `email`, no `invitation=1` sentinel.
- **DB:** unique partial index on `TokenHash WHERE token_hash IS NOT NULL`. Doubles as the global lookup key for the tenant-less accept path. Added via a `Kartova.Migrator` migration.
- **Validity predicate:** hash matches ∧ `Status == Pending` ∧ `ExpiresAt > now` ∧ `TokenHash != null`.
- **Single-use:** on successful accept, `CredentialSetAt = now` and `TokenHash = null` → the link dies. `Status` stays `Pending` (flips at login per D6). Reuses the existing 7-day `ExpiresAt`; the background expirer continues to flip stale `Pending → Expired`, which the predicate rejects.

## 5. Backend — endpoints, handler, KeyCloak client

Both endpoints are **anonymous and tenant-less**. They run on the existing BYPASSRLS `AdminOrganizationDbContext` (same pool used by `ExpireInvitationsHostedService`) and resolve the invitation by the globally-unique `TokenHash`.

### 5.1 `GET /api/v1/invitations/accept?token=<opaque>`
Validate the token and return page context. The **only** place the email surfaces — in the response body, over TLS, to the token holder.
```
200 InvitationAcceptContext { orgDisplayName, invitedByDisplayName, email, defaultDisplayName, role, expiresAt }
404  unknown token                 (generic; no enumeration — a valid-format token is required to get past this)
410  { reason }  reason ∈ expired | revoked | alreadyUsed
```

### 5.2 `POST /api/v1/invitations/accept`
Body `{ token, password, displayName }` (no `confirmPassword`).
1. Re-validate the token (same predicate as §4).
2. Validate `password` against Kartova's policy (§7) and `displayName` (trimmed, 1–128 chars).
3. **KeyCloak mutations first** (so a retry is safe): `SetPasswordAsync`, then `UpdateUserAsync` to clear `UPDATE_PASSWORD`, set `emailVerified=true`, and set the name.
4. **Compare-and-swap** DB update: `… SET credential_set_at=now, token_hash=NULL WHERE token_hash=@hash` — a concurrent second `POST` affects 0 rows and no-ops. `Status` stays `Pending`.
5. `200 { email }` → the SPA uses it as the OIDC `login_hint`.

Failure mapping uses the existing `KeycloakAdminException` taxonomy: a vanished KC user → `NotFound` → `410`; policy failure → `400` (ProblemDetails per ADR-0091).

### 5.3 Handler
`AcceptInvitationHandler` in `Kartova.Organization.Infrastructure.Admin` (same module as `AdminOrganizationDbContext` + `ExpireInvitationsHostedService` — consistent BYPASSRLS placement). Methods: `GetContextAsync(token, ct)`, `AcceptAsync(token, password, displayName, ct)`. Registered in DI against the bypass context.

### 5.4 `IKeycloakAdminClient` additions
| Method | Maps to |
|--------|---------|
| `SetPasswordAsync(userId, password, temporary:false, ct)` | `PUT /admin/realms/{realm}/users/{id}/reset-password` `{type:"password", value, temporary:false}` |
| `UpdateUserAsync(userId, UpdateKeycloakUserRequest, ct)` | `PUT /admin/realms/{realm}/users/{id}` `{ emailVerified:true, requiredActions:[], firstName, lastName }` |

### 5.5 Routing
Mapped on the raw `IEndpointRouteBuilder` (like `AuthRoutes`), **no** `RequireAuthorization()`, **no** `RequireTenantScope()`. `Referrer-Policy: no-referrer` set on the accept response(s); the route is excluded from access-log query-string capture.

## 6. Acceptance flip & display-name flow

**Flip stays at login (D6).** `Pending → Accepted` remains in `SessionStartHandler`, keyed on `KeycloakUserId == sub`. The accept endpoint never sets `Status`. The working `/welcome` celebration (keyed on "we just flipped one in this hop") is untouched.

**Display name → KeyCloak `firstName`.** `UserProjectionUpdater.UpsertAsync` rebuilds `DisplayName` from JWT claims on every login via `User.ComputeDisplayName(given, family, email)`. So the chosen name must live in KeyCloak to persist. `UpdateUserAsync` writes the whole `displayName` into `firstName` (lastName empty); the login JWT then carries `given_name = displayName`, and the projection reflects it — single source of truth = KC claims, no projection-clobber. (Alternative if preferred: split on first whitespace into first/last — round-trips through `ComputeDisplayName`. Default is whole-string-in-firstName.)

**End-to-end sequence:**
1. Admin invites → KC user (no password, `UPDATE_PASSWORD`) + `Invitation`(`Pending`, `TokenHash`) → copy `…/accept-invitation?token=`.
2. Invitee opens link → anonymous page → `GET …/accept?token` → shows org · inviter · email (read-only) + password×2 + displayName.
3. Submit → `POST …/accept` → KC: set password, clear `UPDATE_PASSWORD`, `emailVerified=true`, firstName=displayName; burn token; **Status still Pending** → `200 {email}`.
4. SPA → `auth.signinRedirect({ login_hint: email })` → invitee types the password they just set → JWT (`sub`=pre-created id, `given_name`=displayName).
5. `OidcCallbackHandler` → `POST /api/v1/auth/session` → flip `Pending → Accepted` by `sub` → `AcceptedInvitationInfo` → `/welcome`.

## 7. Security

- **Token:** 256-bit `RandomNumberGenerator` bytes, base64url; stored only as SHA-256 hash; never logged. Lookup by hash-equality on an indexed column (no per-byte timing channel). Single-use + 7-day bound.
- **Password policy (server-side — admin `reset-password` bypasses the realm policy):** NIST 800-63B-aligned — **12–128 chars, no forced composition**, mirrored in zod and re-checked in the handler (`400` on fail). Passwords are not trimmed. Recommend also adding `passwordPolicy: "length(12)"` + `bruteForceProtected: true` to `deploy/keycloak/kartova-realm.json` so direct/federated KC logins enforce the same baseline.
- **Rate limiting:** ASP.NET fixed-window per-IP limiter scoped to the two accept routes (no limiter exists today). Guessing a 256-bit token is infeasible; this caps abuse/DoS.
- **No enumeration:** generic `404` for unknown token; `410` reasons reachable only *with* a valid token; email returned only to the token holder.
- **URL leakage (D5):** token in query is mitigated by `Referrer-Policy: no-referrer` (so it isn't sent to Keycloak on the login redirect), access-log query scrubbing, single-use, and short TTL.
- **Partial-failure consistency:** KC-then-DB ordering is retry-safe — password-set is idempotent and the token stays live until the CAS nulls it; logged on partial failure.
- **`emailVerified=true` on accept** is justified: possessing the high-entropy token (mailed to that address in production) proves mailbox control.

## 8. Frontend

- **New anonymous route** in `web/src/app/router.tsx`, sibling of `/welcome` / `/login-error` (outside `<ProtectedShell>`): `<Route path="/accept-invitation" element={<AcceptInvitationPage />} />`.
- **New feature slice** `web/src/features/invitations/`: `pages/AcceptInvitationPage.tsx`, `api/acceptInvitation.ts`, `schemas/acceptInvitation.ts` (zod).
- **Page behavior:**
  1. Read `token` from `useSearchParams`; missing → "invalid link" (no request fired).
  2. On mount → `GET …/accept?token`: `200` → form with context (org name, "Invited by {inviter}", email read-only, display-name prefilled, role badge); `404` → invalid; `410 {reason}` → reason-specific copy (expired / revoked / already-used → "try signing in").
  3. Form (react-hook-form + zod): `password`, `confirmPassword`, `displayName`. zod = password policy (§7) + `confirm === password` refine + displayName trimmed 1–128. Reuses the Untitled UI password input (show/hide) + Button, styled like `WelcomePage`'s centered card.
  4. Submit → `POST …/accept {token, password, displayName}`: `200 {email}` → `auth.signinRedirect({ login_hint: email })`; `410` (raced) → gone state; `400` → field error.
- **Anonymous fetch:** the two accept calls must use an unauthenticated fetch (no `Authorization` header), unlike every other SPA call which attaches the bearer via `openapi-fetch-helpers`. The accept API module uses a plain fetch client.

## 9. Testing (ADR-0097 five-tier)

- **Architecture (NetArchTest):** new DTOs (`InvitationAcceptContext`, accept request/response) live in `*.Contracts` and **must carry `[ExcludeFromCodeCoverage]`** — `ContractsCoverageRules` enforces this. `AcceptInvitationHandler` stays in `…Infrastructure.Admin`, no cross-module refs.
- **Unit (MSTest + NSubstitute), mirroring `CreateInvitationHandlerTests`:** `InvitationToken.Hash` deterministic + distinct issuance; `AcceptInvitationHandler` — valid accept (KC calls ordered, token burned, Status stays Pending), unknown → not-found, expired/revoked/already-used → gone, policy fail → 400, KC `NotFound` → gone, CAS race → second call no-ops; zod schema tests (mirror `inviteUser.test.ts`).
- **Integration (Testcontainers — real Postgres + real Keycloak, extending `KeycloakAdminClientIntegrationTests` / `SessionBootstrapTests`):** `GET accept` context + 404/410; `POST accept` → KC password set, `UPDATE_PASSWORD` cleared, `emailVerified=true`, firstName set, token nulled, Status still Pending; accept → OIDC login → session bootstrap flips → Accepted; two concurrent POSTs → exactly one `200`.
- **Frontend (vitest + RTL):** `AcceptInvitationPage` states (200/404/410), validation, submit→`signinRedirect({login_hint})`, 410/400 handling; `acceptInvitation` api asserts **no `Authorization` header**.
- **E2E / DoD real-HTTP (Playwright + docker-compose):** full happy path (invite → copy link → set password+name → KC login → `/welcome`) + negative (expired/used link). DoD step-5 evidence.
- **Mutation (Stryker, DoD step 7):** run on changed files (handler, token helper, entity) to ≥80%.

## 10. Files touched (impact)

**Backend**
- `…/Organization.Domain/Invitation.cs` — `TokenHash`, `CredentialSetAt` fields; `Create` accepts the hash.
- `…/Organization.Domain/InvitationToken.cs` *(new)* — `Hash(string)` helper.
- `…/Organization.Infrastructure/CreateInvitationHandler.cs` — issue token, persist hash, build `…/accept-invitation?token=` URL.
- `…/Organization.Infrastructure.Admin/AcceptInvitationHandler.cs` *(new)*.
- `…/Organization.Infrastructure.Admin/` route registration *(new or extend the admin endpoint module)* + DI.
- `…/Organization.Contracts/` — `InvitationAcceptContext`, accept request/response DTOs *(new, `[ExcludeFromCodeCoverage]`)*.
- `Kartova.SharedKernel.Identity/IKeycloakAdminClient.cs` + `KeycloakAdminClient.cs` — `SetPasswordAsync`, `UpdateUserAsync` (+ `UpdateKeycloakUserRequest` DTO).
- `Kartova.Migrator/` — migration: add columns + partial unique index.
- `Kartova.Api/Program.cs` — rate-limiter registration for the accept routes (if not folded into the route module).

**Frontend**
- `web/src/app/router.tsx` — anonymous `/accept-invitation` route.
- `web/src/features/invitations/{pages/AcceptInvitationPage,api/acceptInvitation,schemas/acceptInvitation}` *(new)*.

**Config**
- `deploy/keycloak/kartova-realm.json` — (recommended) `passwordPolicy` + `bruteForceProtected`.

## 11. Out of scope / future

- **Email delivery** of the invite link (SMTP / Mailpit + template) — deferred to E-06a, per slice-9 Decision #4.
- **Entra ID federation.** Under Keycloak↔Entra identity brokering the credential is owned by Entra and this Kartova set-password step would be skipped (the provisioning would be an Entra B2B guest invite). This design targets the local-Keycloak credential model and should sit behind a provisioning seam so a future brokered mode can bypass it. Likely a future ADR.
- **Breached-password check** (k-anonymity / HIBP) on the accept endpoint — future hardening.
