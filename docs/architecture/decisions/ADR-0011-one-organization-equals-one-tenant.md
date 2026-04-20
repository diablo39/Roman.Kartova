# ADR-0011: One Organization Equals One Tenant

**Status:** Accepted
**Date:** 2026-04-17
**Deciders:** Roman Głogowski (solo developer)
**Category:** Multi-Tenancy
**Related:** ADR-0012 (RLS isolation, pending), ADR-0061 (per-user billing)

## Context

Kartova must define the unit of isolation, billing, and administration (PRD §6.1). A clear one-to-one mapping simplifies billing, RBAC, data export, GDPR-erasure scope, and support. Enterprise-style parent/child tenancy adds significant complexity that is unjustified for MVP.

## Decision

Each organization is a single tenant with full data isolation. One tenant = one billing entity = one RBAC scope = one data-residency record = one GDPR/DPA boundary. Users may belong to multiple organizations via separate memberships but each session is scoped to exactly one organization.

## Rationale

- Simplest possible multi-tenancy model — least room for isolation bugs.
- Clean billing boundary (ADR-0061).
- Clean GDPR boundary — erasure and export apply to a single tenant cleanly.
- Matches customers' mental model of "our company on Kartova."

## Alternatives Considered

- **Sub-tenants / workspaces within an org** — useful for huge enterprises but adds hierarchy to every authorization decision; defer.
- **Shared tenant for small orgs** — breaks isolation promises; unacceptable.
- **Enterprise-with-children hierarchy** — Backstage-like; defer to v2 / enterprise tier.

## Consequences

**Positive:**
- Simpler authorization, billing, and data-management code
- Clean compliance story per tenant

**Negative / Trade-offs:**
- Multi-division enterprises may need multiple tenants and cross-tenant navigation (not supported at MVP)
- Tenant merges/splits not supported

**Neutral:**
- Cross-org user experience (role switcher) is a UI concern, not a model concern

## References

- PRD §6.1
