# ADR-0014: Tenant Claim Extracted from JWT on Every Request

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Multi-Tenancy
**Related:** ADR-0007 (JWT), ADR-0012 (RLS, pending)

## Context

With 1000+ tenants under RLS, every request must carry an authoritative tenant identifier that cannot be spoofed or ambiguous. Alternative sources (subdomain, header, URL path) can all be forged or desynchronized from the authenticated principal.

## Decision

The tenant identifier is read exclusively from the `tenant_id` claim in the validated JWT on every request. The API middleware sets it into an ambient request context and, for database connections, issues `SET app.current_tenant = '<id>'` before executing queries so PostgreSQL RLS policies enforce isolation.

## Rationale

- Authoritative: tenant is bound to authenticated identity, not to a URL or header.
- Cannot be spoofed — JWT is signed by KeyCloak (ADR-0006).
- Works uniformly for UI, API, CLI, and future clients.
- Composes cleanly with RLS (ADR-0012) — defense in depth.

## Alternatives Considered

- **Subdomain-based** (`acme.kartova.io`) — nicer UX but doesn't bind to identity; must still check auth token matches.
- **Header-based** (`X-Tenant-Id`) — trivially spoofable if not cross-checked against JWT.
- **URL path prefix** (`/orgs/{id}/...`) — verbose, leaks tenant IDs in logs; still not authoritative.

## Consequences

**Positive:**
- Defense in depth with RLS
- Hard to introduce cross-tenant leakage bugs

**Negative / Trade-offs:**
- Users in multiple orgs must re-authenticate (or token-switch) when changing tenant context
- URL scheme cannot unambiguously identify tenant without adding a redundant path segment (design choice)

**Neutral:**
- Friendly subdomains may still be offered for the status page and branding, but are not load-bearing for isolation

## References

- Phase 0: E-01.F-04.S-02, E-01.F-08.S-03
