# ADR-0116 — SPA holds KeyCloak tokens; BFF deferred as accepted risk

Status: Accepted
Date: 2026-09-16

## Context

The web SPA authenticates as a **public** OIDC client (PKCE code flow) against KeyCloak. The access token, refresh token, and id token are stored in browser `sessionStorage` via `oidc-client-ts` `WebStorageStateStore` (`web/src/shared/auth/authConfig.ts`), tab-scoped and cleared on tab close; `automaticSilentRenew` performs in-browser refresh. The API validates the access JWT with real `JwtBearer` against KeyCloak (`JwtAuthenticationExtensions.cs`).

JS-readable tokens are exposed to any XSS on the page. The IETF *OAuth 2.0 for Browser-Based Apps* BCP (`draft-ietf-oauth-browser-based-apps`) now recommends the **Backend-For-Frontend (BFF)** pattern — tokens held server-side, delivered to the browser only as an HttpOnly + SameSite cookie — as the default for browser apps. Backlog story E-01.F-04.S-05 captured BFF as post-MVP hardening.

Question: adopt BFF now, drop it, or defer it deliberately.

## Decision

**Keep the SPA-holds-tokens model. Defer BFF (S-05) as a documented, risk-accepted decision — not dropped.** Adopt cheaper interim hardening (S-06) instead.

Rationale:
- **BFF's marginal value is narrow.** An XSS foothold can drive the live authenticated session (issue requests) regardless of where tokens live. BFF closes only *token exfiltration* — replaying stolen access/refresh tokens elsewhere or long after the session. It does not close the larger XSS-rides-session risk.
- **Cost is high and wide.** API-hosted BFF is a ~4–6-slice, high-blast-radius migration: dual auth scheme (cookie for browser, JWT retained for CLI/service accounts), confidential KeyCloak client, multi-replica ticket store + shared DataProtection keys, same-origin/ingress with CORS removal, CSRF/antiforgery across all mutating endpoints, full frontend auth cutover, and real-seam test migration. Auth is a whole-system boundary; a dual-scheme or CSRF gap is itself a security regression.
- **Cheaper mitigations recover most of the value (S-06):** short access-token TTL + refresh-token **rotation with reuse-revocation** in KeyCloak (shrinks the stolen-token window to minutes and self-destructs stolen refresh tokens) and a strong **CSP** header (directly shrinks the XSS surface). Both are near-zero code. `sessionStorage` over `localStorage` is already in place.

**Revisit trigger:** an enterprise / compliance security review, or introduction of long-lived browser sessions. At that point re-open S-05 and supersede this ADR.

## Consequences

- Accepted residual risk: an XSS vulnerability can exfiltrate KeyCloak tokens from `sessionStorage`. Mitigated (not eliminated) by S-06's short TTL + refresh rotation and by XSS-prevention hygiene (React auto-escaping, CSP, dependency audit).
- The API keeps a single `JwtBearer` scheme; no cookie/CSRF machinery, no same-origin coupling, no shared ticket store. SPA and API stay separately deployable.
- CLI / service-account auth is unaffected (JWT Bearer remains the model regardless of a future BFF).
- Supersedes nothing; contextualizes ADR-0007 (short-lived tokens) as part of the accepted-risk posture. A future BFF adoption supersedes this ADR.
- **Operator how-to for the interim CSP (S-06b):** [deploy/csp-configuration.md](../../../deploy/csp-configuration.md) — setting `CSP_EXTRA_ORIGINS` per environment, the Report-Only → enforcing flip, and adding a directive when the SPA needs a new source.
