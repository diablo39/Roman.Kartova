# Design — Environment & Deployment Tracking (E-02.F-05)

**Date:** 2026-09-18
**Feature:** E-02.F-05 — Environment & Deployment Tracking (Phase 1, Epic E-02 Core Catalog).
**Stories:** S-01 (register environments), S-02 (record deployments + history), S-03 (environment matrix).
**Type:** new subsystem in the Catalog module — 2 new persisted aggregates + 1 derived read model.
**Slicing:** exceeds the ~400-line target / ~800 ceiling as one PR → decomposed into three sequential sub-slices (A→B→C), each its own plan, PR, and DoD ledger. This document is the feature-level spec; each sub-slice's plan is written just-in-time via `writing-plans`.

## Problem

The catalog can register Applications, Services, APIs, Infrastructure (VMs), and Systems, but has no concept of **where** an application runs or **what version** is live. F-05 adds:

- **S-01** — first-class Environments (dev/staging/prod) with region/cluster/resource details, so deployment targets are cataloged.
- **S-02** — an append-only Deployment record (app version @ environment, with deployer, timestamp, replicas, resources, config) so deployment history is tracked.
- **S-03** — an environment matrix (app × environment → current version, config-diff-able) so the live state across environments is visible at a glance.

## Key decisions (brainstorm, 2026-09-18)

1. **Environment = new `EntityKind.Environment`** — joins the unified catalog entity taxonomy (ADR-0111) for consistent identity/routing and future graph participation.
2. **Environment is tenant-global, not team-owned** — `ITenantOwned` only, no `TeamId`/`ITeamScopedResource`. Environments are shared deployment targets; role-gated writes, tenant-only RLS. Deliberate deviation from Infrastructure's team-ownership (ADR-0103) — recorded in the new ADR.
3. **Deployment = append-only history aggregate, NOT a graph edge** — `Relationship` edges carry no attributes (verified `Relationship.cs`), and edges are current-state while S-02 needs history. So deployment is its own table.
4. **"App is in env" + the matrix are derived** — latest Deployment row per (app, env). No `DeployedOn`/`RunsIn` edge is created for app→environment. (`RelationshipType.DeployedOn` stays what it is today — component→VM-infra — untouched.)
5. **Deployer = free-text `DeployedBy` (from request) + `CreatedByUserId` (from JWT, audit)** — handles CI/automation deploys as well as human ones.

## Approach

Mirror the established Catalog aggregate pattern (`Application` / `InfrastructureResource`): plain-Guid PK backing field, static `Create` factory + explicit-`createdAt` overload for seed/tests, private setters, validation in-domain, jsonb payload for loose/variant attributes, Postgres `xmin` concurrency token, `AddModuleDbContext` + RLS, minimal-API delegates, ADR-0095 cursor lists.

### Domain (`Kartova.Catalog.Domain`)

- **`EntityKind`** — append `Environment` at end (stable smallint): `{ Application, Service, Api, System, Infrastructure, Environment }`. No new relationship pairs → `RelationshipTypeRules.IsAllowedPair` / `IsCreatable` unchanged (deployment is derived, not an edge).
- **`EnvironmentType`** enum — `{ Development, Staging, Production }`, append-only stable smallint (matches `InfrastructureType` convention). Confirmed value set (not free-text).
- **`Environment`** aggregate (`ITenantOwned`): `Id (EnvironmentId)`, `TenantId`, `DisplayName` (≤128, unique per tenant), `Description` (≤4096), `Type (EnvironmentType)`, `Region` (string?, ≤256), `Cluster` (string?, ≤256), `Attributes` (jsonb, loose resource details), `CreatedByUserId`, `CreatedAt`, `Xmin`. `Create` + explicit-`createdAt` overload; `Edit` (metadata only — `Type` immutable, mirrors `InfrastructureResource.Edit`).
- **`Deployment`** aggregate (`ITenantOwned`, append-only — no `Edit`): `Id (DeploymentId)`, `TenantId`, `ApplicationId (Guid)`, `EnvironmentId (Guid)`, `Version` (string, ≤128, required), `DeployedBy` (string, ≤256, required), `DeployedAt` (DateTimeOffset, request-supplied, defaults now), `Replicas` (int?, ≥0), `Resources` (jsonb), `Config` (jsonb/text), `CreatedByUserId`, `CreatedAt`. Factory validates `Version`/`DeployedBy` non-empty, `Replicas >= 0`, ids non-empty.
- New id value types `EnvironmentId`, `DeploymentId` (mirror `InfrastructureId`).

### Persistence / RLS (`Kartova.Catalog.Infrastructure`)

- Two tables: `catalog_environments`, `catalog_deployments`. Registered via `AddModuleDbContext` (never raw `AddDbContext`), RLS `SET LOCAL app.current_tenant_id` on `Begin` (ADR-0090). Unique index `(tenant_id, display_name)` on environments; index `(tenant_id, application_id, environment_id, deployed_at DESC)` on deployments for the matrix/history queries.
- Migrations authored for `Kartova.Migrator` (ADR-0085) — never at app startup.
- FK validation for Deployment (app + env exist, same tenant) is RLS-safe: a cross-tenant target is hidden by RLS → resolves as not-found. Discriminative isolation tests assert `ProblemDetails.Type` (per CLAUDE.md), not status alone.
- **Matrix read model (S-03):** `DISTINCT ON (application_id, environment_id) ... ORDER BY application_id, environment_id, deployed_at DESC` — latest deployment per (app, env). Returns `Config` so the UI can highlight config differences. No new stored state.

### API (REST + OpenAPI, ADR-0095 list conventions)

| Method | Route | Story | Notes |
|--------|-------|-------|-------|
| POST | `/catalog/environments` | S-01 | register |
| GET | `/catalog/environments` | S-01 | `CursorPage<T>`; default sort `displayName asc`; filters `type`, `region` (ADR-0107 surface confirmed at plan time) |
| GET | `/catalog/environments/{id}` | S-01 | |
| PUT | `/catalog/environments/{id}` | S-01 | metadata edit; `Type` immutable |
| DELETE | `/catalog/environments/{id}` | S-01 | **block with 409 `environment-has-deployments`** if any deployment references it (no cascade) |
| POST | `/catalog/deployments` | S-02 | record deployment |
| GET | `/catalog/applications/{id}/deployments` | S-02 | history; `CursorPage<T>`; default sort `deployedAt desc` |
| GET | `/catalog/deployment-matrix` | S-03 | app × env latest-version grid + config |

OpenAPI snapshot + typed client regenerated (predev/prebuild) and committed per slice.

### Permissions (5 synced touchpoints each, per CLAUDE.md)

New `KartovaPermission` constants: `CatalogEnvironmentsRegister`, `CatalogEnvironmentsEdit`, `CatalogEnvironmentsDelete`, `CatalogDeploymentsRecord`. Reads (list/detail/matrix/history) use the existing catalog-read gate. Each new permission touches all 5: (1) `KartovaPermissions.cs` const + `All`; (2) `KartovaRolePermissions.cs` role map; (3) `permissions.snapshot.json`; (4) `permissions.ts`; (5) `usePermissions.test.tsx` OrgAdmin full-set. Grep the blast radius (const refs under-report).

### Frontend (`web/`)

- **S-01:** Environments list screen (`useCursorList` + `useListUrlState` + `<DataTable>`; `<FilterBar>` type + region) + create/edit dialog + detail page. Read local mockup first (`docs/ui-screens/…`); escalate to Stitch only if missing. Map to Untitled UI / react-aria-components (ADR-0094); `<Table>` needs exactly one `isRowHeader` column.
- **S-02:** Deployment-history section/tab on the Application detail page + "Record deployment" dialog (version, environment picker, deployedBy, replicas, resources, config).
- **S-03:** Environment matrix view (app rows × env columns, version cells, config-diff highlight). Distinct from E-06.F-04 "Environment Map Dashboard" — this is the tabular matrix, not the topology map.

### New ADR

A new ADR — *"Environment & Deployment as catalog entities"* — records decisions 1–5 above (Environment = tenant-global EntityKind; Deployment = append-only history, not a graph edge; matrix derived). **Previewed to the owner before saving** (CLAUDE.md ADR rule). Authored as part of sub-slice A.

## Sub-slices (sequential; each own plan + PR + DoD ledger)

| # | Story | Scope | Notes |
|---|-------|-------|-------|
| A | S-01 | `EntityKind.Environment` + `EnvironmentType` + `Environment` aggregate + table/RLS/migration + CRUD endpoints + env permissions + list/detail/dialog FE + new ADR | walking skeleton for the subsystem |
| B | S-02 | `Deployment` aggregate + table/RLS/migration + record endpoint + history list endpoint + `CatalogDeploymentsRecord` perm + FE history section + record dialog | depends on A (env must exist) |
| C | S-03 | matrix read model + `/catalog/deployment-matrix` endpoint + matrix FE view (config-diff highlight) | depends on B (reads deployments) |

Each sub-slice is independently shippable and stays within the ~800-line ceiling; if a plan projects over, decompose further at `brainstorming`/`writing-plans` time.

## Out of scope / deferred (tracked, not slice A/B/C)

- **Elasticsearch search-indexing of Environment (E-05)** — Environment is an `EntityKind` but is not added to the shared search index in this feature. Follow-up when E-05 covers it (file as tech-debt / story reference).
- **Relationship-graph edges to/from Environment** and **Environment ↔ System membership (`PartOf`)** — no write path in F-05; future work.
- **Config-diff *rendering* sophistication** — S-03 returns config payloads and highlights that they differ; a full structured diff UI is a later enhancement.
- **Deployment delete/retention** — deployments are append-only; a retention/cleanup policy is not in scope (global 180-day retention per ADR-0106 applies at the platform level, not modeled here).
- Broker registration (E-02.F-04.S-02) and other E-02 features — separate.

## Testing strategy (per docs/TESTING-STRATEGY.md)

Every sub-slice wires HTTP + auth + DB + migration → **real-seam** gate-3/gate-4 artifacts are named deliverables in each plan:

- **Integration (real Postgres/RLS, real JWT via `KartovaApiFixtureBase`):** per slice ≥1 happy + ≥1 negative.
  - A: create/list/get/edit/delete environment; unique-name 409; delete-with-deployments 409 (slice B onward); cross-tenant get → not-found asserting `ProblemDetails.Type`; list cursor + `type`/`region` filters + default `displayName asc` sort.
  - B: record deployment (happy); deploy to cross-tenant app/env → discriminative `ProblemDetails.Type`; history list `deployedAt desc` + cursor; validation negatives (empty version/deployedBy, negative replicas).
  - C: matrix returns latest version per (app, env) after multiple deployments; empty matrix; tenant isolation.
- **Unit:** domain factory/validation for `Environment` + `Deployment`; matrix latest-per-pair query handler (filter-map/ordering).
- **Architecture:** `EntityKind`/permission snapshot sync (arch tests already guard C#↔snapshot); Contracts/DTO `[ExcludeFromCodeCoverage]`.
- **Frontend (vitest + `tsc -b` per task):** list renders + filters; record dialog submits; matrix renders cells + diff highlight; `getAllByRole("rowheader").length > 0` for any react-aria `<Table>`; permission gating.
- **Container build (gate 4):** runs whenever the slice touches migration/csproj/restore surface (it will — new EF migrations + possible package refs). Confirm per slice.
- **E2E-impact (gate 9 trigger):** F-05 adds new surfaces; if a slice changes an Application-detail tab/IA that an existing `e2e/` spec traverses (S-02 adds a deployment section), update + run the affected spec locally before merge.

## DoD

Ten always-blocking gates per CLAUDE.md, **per sub-slice**. Each slice copies the DoD ledger template to `docs/superpowers/verification/2026-MM-DD-<slice-topic>/dod.md` + `gate-findings.yaml`. Gate 9 (visual/API): drive each slice on the running stack (env CRUD screen; record-deployment flow; matrix render) and commit evidence.
