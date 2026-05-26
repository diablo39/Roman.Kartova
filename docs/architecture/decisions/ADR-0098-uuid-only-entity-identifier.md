# ADR-0098: UUIDs as the Canonical and Only Entity Identifier

**Status:** Accepted
**Date:** 2026-05-25
**Deciders:** Roman Głogowski (solo developer)
**Category:** API & Integration Architecture
**Related:** ADR-0001 (PostgreSQL), ADR-0011 (one Org = one tenant), ADR-0029 (REST), ADR-0082 (modular monolith), ADR-0092 (REST URL convention)

## Context

ADR-0092 (REST URL convention) explicitly punted on the resource-id format: *"Resource-id format (UUID vs slug-based) — separate question, currently UUID per ADR-0001."* But ADR-0001 is about choosing PostgreSQL — it says nothing about identifier format. So the rule was informally followed (slice 3's `/api/v1/catalog/applications/{id:guid}`) but never codified.

Meanwhile, slice 3 introduced `Application.Name` as a kebab-case, immutable, regex-validated slug. The slug appeared in the response payload and SPA display as a parallel identifier — never in URLs, but conceptually a "human-readable machine ref." Slice 8 (Team) would naturally inherit the same pattern: `Team.Slug` alongside `Team.Id`.

Slugs in a multi-tenant SaaS introduce a category of failure modes that don't exist with UUIDs:
- Cross-tenant collision concerns ("we both have a team called `auth`") — even when the URL uses a different identifier, the slug-as-data still creates mental confusion.
- Validation rules (regex, length, immutability) that must be enforced consistently.
- Copy-paste linking ambiguity — URLs that look portable across tenants but aren't.
- Log / trace aggregation hazards — slug strings in spans collapse across tenants.
- Bikeshedding on future external-ref formats (CLI, scorecards, webhooks) — `app:platform/auth` vs `app:auth` vs `<uuid>` vs ...

## Decision

Use UUIDs (`Guid`) as the canonical and only entity identifier across Kartova. Specifically:

1. **All entities are identified by `Guid` UUIDs.** Generated server-side at creation (`Guid.NewGuid()`).
2. **URLs use `{id:guid}` exclusively.** No slug-in-URL, no namespace-in-URL.
3. **No slug, kebab-case name, or any secondary machine-readable identifier on entities.** Display names are free text; uniqueness is *not* enforced at the DB level.
4. **Tenant scope is established from JWT claims**, never from URL path segments.
5. **Display-name duplicates** are allowed within a tenant. UI may warn ("a team named 'Platform' already exists") but doesn't block.

## Consequences

### Positive

- Eliminates cross-tenant slug-collision concerns category-wide. Forward rule for every entity (Service, API, Infrastructure, Broker, System in upcoming slices).
- URLs are globally unique without leaking tenant identity in the path (auth-required B2B; SEO N/A).
- Simpler domain validation (no slug regex, no immutability rule).
- Consistent identifier across logs, traces, audit entries, external refs, and external integrations.

### Negative / trade-offs

- UUIDs aren't human-typeable. The SPA must always provide entity-picker UX (no "go to team `auth` by typing"). Acceptable: Kartova is a navigated UI, not a CLI-first product.
- External-ref format for future CLI / scorecards / webhooks still needs design (probably `<entity-kind>:<entity-id>`). Decision deferred to the slice that introduces the first such feature.
- Existing `Application.Name` (slice 3) was a kebab-case slug. Slice 8 retrofits this — see §slice-8 Decision #17. ADR-0098 has no grandfathered exception.

### Neutral

- Performance is identical (Postgres handles UUID PKs efficiently with the right index strategy).

## Alternatives considered

- **UUID-in-URL + slug-as-data (kept for human readability).** Rejected: keeps the slug failure modes (validation, immutability, collision-within-tenant); the human-readability benefit is small in a navigated UI that already has display names.
- **Namespace-in-URL (`/orgs/{slug}/teams/{slug}`, GitHub-style).** Rejected: conflicts with existing tenant-from-JWT convention (Applications, lifecycle endpoints, `/me/permissions` all use it); flipping every existing endpoint would be a slice-wide retrofit. Discussed in §slice-8 Decision #16 alternatives.
- **Slug-only (entity identified by slug in URL and storage).** Rejected: cross-tenant collisions in logs/traces; URL portability is misleading.

## References

- ADR-0092 (REST URL convention) — this ADR resolves line 124's punt.
- Slice 3 design: introduces `Application.Name` (now retrofitted by slice 8).
- Slice 8 design: this ADR is created here.
