# ADR-0007: JWT (OIDC) for API and CLI Auth

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Authentication & Authorization
**Related:** ADR-0006 (KeyCloak), ADR-0009 (service accounts), ADR-0014 (tenant claim)

## Context

Kartova exposes web UI, REST API, and CLI clients (PRD §4.5.1, §5.1). A single consistent auth scheme simplifies implementation, documentation, and support for a solo developer. Clients include browsers, CI/CD agents, and interactive terminals — all of which must survive stateless horizontal scaling of the API tier.

## Decision

Use JWT access tokens (OIDC) issued by KeyCloak (ADR-0006) for all clients. Access tokens are short-lived (~15 min), refresh tokens are used by web/CLI, and the `tenant_id` and role claims are carried in the JWT payload (see ADR-0014, ADR-0008). ASP.NET Core's JWT bearer middleware validates tokens without calling KeyCloak on every request.

## Rationale

- Stateless validation scales horizontally without sticky sessions.
- One auth model across UI, API, and CLI reduces cognitive load and support cost.
- OIDC is a standard — no proprietary client libraries required.

## Alternatives Considered

- **Opaque tokens + introspection** — forces an introspection call per request, hurting p95 latency targets (ADR-0075).
- **PASETO** — better security properties but smaller ecosystem, no KeyCloak native support.
- **API keys** — no delegated auth, no expiry, harder to rotate; reserved for legacy compat only.
- **mTLS M2M** — used for agent channel (ADR-0042) but impractical for CLI/UI.

## Consequences

**Positive:**
- Fast, stateless request validation
- Industry-standard tooling (jwt.io, Postman, curl support out of the box)

**Negative / Trade-offs:**
- Token revocation is not immediate (must wait for expiry or use a revocation list)
- JWT size grows with claims — must keep claims minimal
- Key rotation requires JWKS caching discipline

**Neutral:**
- Refresh token flow requires secure local storage in CLI (see ADR-0009)

## References

- PRD §4.5.1, §5.1
- Phase 0: E-01.F-04.S-02
- Phase 5: E-13.F-01.S-02
