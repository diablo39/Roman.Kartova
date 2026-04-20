# ADR-0006: KeyCloak as Identity Provider

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Authentication & Authorization
**Related:** ADR-0007 (JWT), ADR-0008 (RBAC roles), ADR-0009 (service accounts), ADR-0010 (status page auth)

## Context

Kartova requires OIDC-compliant authentication for web UI, REST API, and CLI across 1000+ tenants, with enterprise SSO federation (SAML, LDAP, social providers). Self-rolling an IdP is outside the solo-dev scope. A managed vendor would add cost per user at scale and complicate residency for EU-target-market compliance (PRD §7.3).

## Decision

Use KeyCloak, self-hosted on Kubernetes, as the identity provider. KeyCloak issues OIDC JWTs, handles user registration/invites, password policies, MFA, SSO federation (SAML/LDAP/social), and realm-per-deployment. One realm covers all Kartova tenants; tenant assignment is a JWT claim (ADR-0014).

## Rationale

- OIDC/SAML/LDAP/social federation out of the box — major time saver for a solo dev.
- Self-hostable and open source — no per-user SaaS cost, keeps data inside the EU residency boundary.
- Strong integration with ASP.NET Core (ADR-0027) via standard OIDC middleware.
- Battle-tested at enterprise scale.

## Alternatives Considered

- **Auth0 / Okta / Azure AD B2C / AWS Cognito** — per-user pricing erodes margin at 1000+ tenants × users each; vendor/cloud lock-in.
- **FusionAuth** — viable but smaller ecosystem than KeyCloak.
- **Ory Kratos/Hydra** — more composable but more pieces to operate.
- **Self-rolled IdentityServer** — too much auth surface area for a solo dev.

## Consequences

**Positive:**
- No per-user auth cost
- Enterprise federation available on day one
- Standards-based JWTs consumable by all clients

**Negative / Trade-offs:**
- Operational burden: KeyCloak cluster needs HA, backups, version upgrades
- JVM-based stack alongside .NET (minor ops diversity)
- KeyCloak UX for admin flows is functional but dated — may need custom wrappers

**Neutral:**
- Storage is KeyCloak's own PostgreSQL schema (can reuse ADR-0001 cluster)

## References

- PRD §5.1, §8
- Phase 0: E-01.F-04.S-01
