# ADR-0010: Internal Status Page Auth via KeyCloak

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Authentication & Authorization
**Related:** ADR-0005 (status page replica), ADR-0006 (KeyCloak), ADR-0023 (status page topology)

## Context

Tenants can publish two kinds of status pages: public (unauthenticated, customer-facing) and internal (employees-only, more detail) (PRD §5.3). The internal page must respect tenant RBAC and must not be accessible to public visitors, yet it must also survive platform outages per ADR-0023/ADR-0076.

## Decision

Internal status pages authenticate users via the existing KeyCloak IdP using OIDC. Public status pages remain unauthenticated. The status-page service validates JWTs locally against a cached JWKS so that transient KeyCloak unavailability does not immediately break internal access.

## Rationale

- Reuses the tenant's existing identity — no separate credentials or user list to manage.
- RBAC (ADR-0008) naturally applies to internal page visibility.
- Local JWKS caching preserves the 99.99% status-page SLA (ADR-0076) during IdP hiccups.

## Alternatives Considered

- **Shared-link tokens** — usable but less secure and no RBAC.
- **IP allow-list** — brittle for modern distributed workforces.
- **Separate lightweight password** — extra auth surface, bad UX.
- **Embedded iframe with host auth** — leaks host cookies, poor portability.

## Consequences

**Positive:**
- One auth model, one set of users
- Internal page respects existing permissions

**Negative / Trade-offs:**
- Status page depends on JWT validity; short-lived tokens may expire during long outages (must allow grace period)
- Public/internal split requires careful routing/domain design

**Neutral:**
- Public pages continue to be unauthenticated and cache-friendly

## References

- PRD §5.3
- Phase 4: E-12.F-01.S-05
