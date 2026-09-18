# ADR-0117: Environment and Deployment Entities — Tenant-Global Environment, Append-Only Deployment History, Derived Matrix

**Status:** Accepted (2026-09-18 — previewed and accepted by the human per the ADR working agreement)
**Date:** 2026-09-18
**Deciders:** Roman Głogowski (solo developer, AI-assisted)
**Category:** Domain Model
**Related:** ADR-0111 (entity taxonomy — discriminated/unified-aggregate precedent), ADR-0103 (team-ownership norm — this ADR deviates from it), ADR-0090 (`ITenantScope` + RLS), ADR-0095 (cursor pagination contract), ADR-0107 (list filtering / filter surface mandate)

## Context

E-02.F-05 catalogs deployment environments (dev/staging/prod) on top of the unified `EntityKind` taxonomy, with later slices adding deployment history and an application × environment matrix view. This ADR fixes how Environment and Deployment fit that taxonomy, and how the matrix is produced.

## Decision

1. **Environment is a first-class `EntityKind.Environment`** — handled uniformly by the existing entity/list/get machinery, the same as Application, Service, API, System, and Infrastructure.

2. **Environment is tenant-global, not team-owned.** It carries `ITenantOwned` only — no `TeamId`. Registration is role-gated on `catalog.environments.register`, granted to `Member` and `OrgAdmin`. This is a **deliberate deviation** from the ADR-0103 team-ownership norm: an environment (dev/staging/prod) is shared platform infrastructure with no single owning team, unlike an Application or Service.

3. **Deployment (a later slice) is an append-only history aggregate, not a relationship edge.** Relationship edges (ADR-0068's vocabulary) are attribute-less and represent current state; a deployment record needs to carry version, deployer, replica count, and config history over time, which an edge cannot express.

4. **"App in env" and the environment matrix DERIVE from the latest Deployment per (application, environment) pair.** There is no `Application → Environment` relationship edge. `RelationshipType.DeployedOn` is unaffected by this decision and continues to mean component → VM-infrastructure hosting, not application → environment.

5. **Deployer identity is free-text `DeployedBy` plus the JWT `CreatedByUserId`.** `DeployedBy` accommodates CI/automation callers (e.g., a pipeline or service name) that are not a `User` row; `CreatedByUserId` captures the authenticated human or service-account principal from the JWT for provenance, exactly as on other catalog writes.

## Consequences

**Positive.** Environment gets uniform entity handling (list/get, RLS, permissions, cursor pagination) for free. Environment names are unique per tenant with clean tenant-only RLS (no team-scoping complexity). Deployment history is accurate over time because it is append-only rather than a mutated edge. The app × environment matrix is a pure read model computed from Deployment rows — no separate write path to keep in sync.

**Negative / costs.** Environment is an `EntityKind` with **no relationship edges yet** — it does not participate in the relationship graph, dependency graph, or System membership. Deferred follow-ups: Elasticsearch search-indexing (E-05), relationship-graph edges to/from Environment, and System membership. The frontend's `EntityKind` union is widened to include `environment` for render-only purposes in the meantime (no graph/relationship UI for it). Separately, a shared resource owned by no team diverges from the ADR-0103 team-ownership norm — accepted here because environments are shared infrastructure, not team-scoped work product.
