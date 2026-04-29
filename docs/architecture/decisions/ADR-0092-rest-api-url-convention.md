# ADR-0092: REST API URL Convention — Module-Prefixed with Admin-First and Skip Rule

**Status:** Accepted
**Date:** 2026-04-29
**Deciders:** Roman Głogowski (solo developer)
**Category:** API & Integration Architecture
**Related:** ADR-0029 (REST), ADR-0034 (OpenAPI), ADR-0082 (modular monolith), ADR-0090 (tenant scope)

## Context

Kartova is a modular monolith (ADR-0082) with multiple bounded contexts that each own one or more entity types:

- `Organization` module: Organization (today), Team, Member (later), Invitation (later)
- `Catalog` module: Application, Service, API, Infrastructure, Broker, Environment, Deployment
- Future: `Notification`, `Search`, `Webhook`, etc.

Each module exposes HTTP endpoints. With Phase 1 expected to ship 7+ entity types across two modules, a URL convention needs to be in place **before** the first non-Organization entity ships, otherwise the pattern is set ad-hoc by whichever endpoint lands first.

Three convention candidates were considered during the slice-3 brainstorming:

1. **Flat per-entity collections** at `/api/v1/<collection>` (e.g., `/api/v1/applications`, `/api/v1/services`).
2. **Module-prefixed** at `/api/v1/<module>/<collection>` (e.g., `/api/v1/catalog/applications`).
3. **Type-grouped** at `/api/v1/entities/<collection>` (e.g., `/api/v1/entities/applications`).

Existing endpoints (slice 1 + slice 2) currently use a flat shape:

- `GET /api/v1/version`
- `GET /api/v1/organizations/me`
- `POST /api/v1/admin/organizations`

Whatever convention is adopted has to be retro-compatible with these — they are already documented and consumed by the docker-compose smoke checks and KeyCloak realm seed flow.

## Decision

Adopt **module-prefixed URLs with admin-first prefix and a primary-collection skip rule**.

### Rule 1 — Module prefix

Every tenant-scoped or unauthenticated-module endpoint lives under `/api/v1/<module-slug>/`. The `<module-slug>` is a stable lowercase kebab-case identifier (regex `^[a-z][a-z0-9-]*$`) declared by each `IModule` implementation as `string Slug { get; }`.

Examples:

```
/api/v1/catalog/applications
/api/v1/catalog/services
/api/v1/organization/teams
/api/v1/organization/members
```

### Rule 2 — Admin-first prefix

Endpoints scoped to **platform-admin only** live under `/api/v1/admin/<module-slug>/`. Admin comes before module so a single auth gate (`/api/v1/admin/*` requires `platform-admin` role) covers the entire admin URL space.

Examples:

```
/api/v1/admin/organizations
/api/v1/admin/catalog/applications     (hypothetical — no current need)
```

### Rule 3 — Primary-collection skip

When a module's slug is the plural form of its primary entity's collection name, the module segment **collapses into the collection name** to avoid awkward duplication like `/api/v1/organization/organizations/me`.

Concretely:

- Organization module's `Slug = "organizations"` (plural form of its primary entity, `Organization`). Endpoint URLs read as `/api/v1/organizations/me` rather than `/api/v1/organization/organizations/me`.
- Catalog module's `Slug = "catalog"` (singular module name, no primary entity collision since Catalog has no `Catalog` entity). Endpoint URLs read as `/api/v1/catalog/applications`.

The skip rule is **not** an exception — it follows mechanically from the slug-equals-URL-segment principle. The module's slug *is* the URL segment, regardless of whether it's a singular or plural noun.

### Rule 4 — System endpoints are unprefixed

Endpoints that aren't owned by any module (e.g., `/api/v1/version`, future `/api/v1/health`) live at the API root, not under any module slug. These are wired directly in `Program.cs`, not through `IModule.MapEndpoints`.

### Rule 5 — Enforcement

Two enforcement layers:

- `MapTenantScopedModule(slug)` and `MapAdminModule(slug)` extension methods in `SharedKernel.AspNetCore` are the only blessed way to declare module routes. They mechanically apply rules 1, 2, and 3.
- A new architecture test `IModuleRules` (slice 3) asserts every `IModule` implementation declares a non-empty `Slug` matching the kebab-case regex, and overrides `MapEndpoints`. Reflection-based, runs in CI.

## Examples — full retroactive map

| Method | Path | Owner | Auth |
|---|---|---|---|
| GET | `/api/v1/version` | system | anonymous |
| GET | `/api/v1/organizations/me` | Organization | tenant-scoped |
| POST | `/api/v1/admin/organizations` | Organization | platform-admin |
| POST | `/api/v1/catalog/applications` | Catalog (slice 3) | tenant-scoped |
| GET | `/api/v1/catalog/applications/{id}` | Catalog (slice 3) | tenant-scoped |
| GET | `/api/v1/catalog/applications` | Catalog (slice 3) | tenant-scoped |
| (future) | `/api/v1/organization/teams` | Organization | tenant-scoped |
| (future) | `/api/v1/catalog/services` | Catalog | tenant-scoped |
| (future) | `/api/v1/admin/catalog/applications` | Catalog (admin) | platform-admin |

All existing slice-1/2 endpoints are already convention-compliant under rule 3. **No URL changes are required as part of this ADR's adoption** — slice 3 is the first endpoint set to apply the convention from inception.

## Rationale

- **Module visibility in URLs.** Reviewers and consumers see module boundaries directly. When a feature spans modules (rare in this design — most cross-module work goes through Wolverine bus, ADR-0028), the URL spelling makes the boundary obvious.
- **Single admin auth gate.** `/api/v1/admin/*` can be guarded once at the routing layer; admin endpoints don't need per-route role attributes.
- **Discipline over prose.** Slugs are the URL contract; the architecture test pins the spelling. New modules can't ship malformed routes.
- **Skip rule avoids ugliness.** `/api/v1/organization/organizations/me` reads worse than `/api/v1/organizations/me`, and the rule doesn't introduce a special case — slug just *equals* what's in the URL.
- **Phase 1 scale.** With 7+ entity types coming in two modules, flat-namespace (`/api/v1/<collection>`) would force every entity collection to be unique across the whole API. Module-prefix removes that constraint per-module.

## Consequences

### Positive

- New modules have a forced shape; no bikeshedding per slice.
- Admin endpoints have a single auth-gate point.
- The `IModule` interface gains a `Slug` field that's also useful for OpenAPI grouping (ADR-0034) and structured-log enrichment (`module=<slug>`, ADR-0058).

### Negative / trade-offs

- The skip rule (rule 3) requires explanation when reviewers see `/api/v1/organizations/me` vs `/api/v1/catalog/applications` and ask "why no `organization` segment?". Mitigated by this ADR being canonical reference.
- Once a slug is assigned to a module, renaming it is a breaking API change. Slugs should be chosen carefully — ideally pluralized when there's an obvious primary collection (Organization → `organizations`), singular when the module hosts many entity types (`catalog`, `notification`, `webhook`).
- Cross-module endpoints (a hypothetical `POST /api/v1/relationships` linking Organization-Team to Catalog-Application) don't fit the module-prefix model cleanly. Resolution: cross-module endpoints belong to whichever module owns the **primary** resource being created/queried (Relationships → Catalog, since Catalog has the dependency-graph data; or its own `Relationships` module). To be revisited at the slice that introduces them (likely Phase 1 / E-04).

## Out of scope

- Versioning beyond `/api/v1` — multi-version coexistence is ADR-0029 and not affected by this ADR.
- Resource-id format (UUID vs slug-based) — separate question, currently UUID per ADR-0001.
- Content negotiation, error response shape (ADR-0091), pagination format — orthogonal.

## References

- ADR-0082 — Modular monolith with `IModule` interface
- ADR-0029 — REST (vs GraphQL)
- ADR-0034 — OpenAPI 3.x auto-generated
- Slice-3 design: `docs/superpowers/specs/2026-04-29-slice-3-catalog-application-design.md`
