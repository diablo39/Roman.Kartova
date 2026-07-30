# Design — System membership assignment (assign / change / clear)

**Story:** E-03.F-03.S-01 closeout — "Create System **and assign components**". S-01 shipped the `CatalogSystem` aggregate + `PartOf` edges backend-only (2026-07-21) and the read-only UI surface (2026-07-22) with assignment explicitly out of scope. This slice closes the `[~]`.
**Date:** 2026-07-30 · **Author:** Roman Głogowski (AI-assisted)
**ADRs touched:** **ADR-0111 amendment (cardinality)** — previewed in §2, applied in A1. Applies ADR-0095 (cursor list surface), ADR-0096 (PUT = idempotent replacement), ADR-0107 (list columns/sort/filters), ADR-0108 (either-endpoint authority), ADR-0084 (browser verification), ADR-0090 (`ITenantScope`), ADR-0094 (Untitled UI / react-aria), ADR-0114 (tabbed detail layout).

---

## 1. Goal

Let a user put an Application or Service **into** a System, **move** it to another System, and **take it out** — from both sides of the relationship (the System's Members tab and the component's own detail page). Today `PartOf` edges can only be created by calling `POST /catalog/relationships` directly; the Systems UI is read-only and shows "No components assigned yet" forever.

Two sequentially-shippable sub-slices:

- **A1 — write path + both-side UI** (this is the shippable closeout of S-01).
- **A2 — list surface**: `System` column + System filter on the Applications and Services lists.

Each is its own plan and its own PR. A2 depends on A1 (nothing to filter by until membership is writable through the product).

## 2. Locked decisions

| Decision | Choice | Rationale |
|---|---|---|
| **Cardinality** | A component is in **at most one** System. **ADR-0111 amendment.** | Backstage's model; makes the hierarchy browse (S-02) a tree rather than a DAG, and makes "which System owns this?" answerable. Enforced at both write paths (§3.3) |
| Model | Stays an **edge** (`PartOf`), not a `SystemId` FK on the component | ADR-0111's all-edge decision holds; S-01 already ships graph/relationship visibility over the edge. At-most-one is a *cardinality* constraint on the edge, not a modelling change |
| Write path | Dedicated setter `PUT /catalog/{applications\|services}/{id}/system` | Atomic set/replace/clear in one call; mirrors the existing `PUT .../team` (E-03.F-02.S-02) and `PUT .../successor` (ADR-0110) setters. Both UI sides call the same endpoint |
| Generic path | `POST /catalog/relationships` with `PartOf` from a source that already has one → **409** | Keeps the invariant true regardless of which door the write comes through (API clients, CLI later) |
| Permission | Reuse `KartovaPermissions.CatalogRelationshipsWrite` | The operation *is* a relationship write. No new permission ⇒ **no 5-touchpoint sync** |
| Authorization | ADR-0108 either-endpoint: OrgAdmin, or member of the **component's** team **or** the **System's** team | Identical to `POST /relationships`; reuses `AuthorizeEitherTeamAsync` |
| Audit | Reuse `CatalogAuditActions.RelationshipCreated` / `RelationshipDeleted` with `RelationshipAuditData` | A membership change *is* edge create/delete; the payload already carries `type=partOf` + both endpoints, so the trail stays queryable without new action constants |
| Component-side read | Reuse `GET /relationships?entityKind=…&entityId=…&direction=outgoing`, filtered to `partOf` client-side | No new GET. Same drift-tolerant filter `SystemMembersSection` already applies on the incoming side |
| FE kind model | New narrow `EntityKind = RelationshipKind \| "system"` for **rendering + search only** | Lets `system` render with a label and a working link, and lets the combobox search Systems, **without** widening `RelationshipKind` (graph URL tokens, creatable-edge matrix, graph filters stay untouched) |
| Graph rendering of System nodes | Still **FU-A** | Out of scope here; A1 only fixes the *relationships-list* rendering of `system` refs |
| Sort by System | **No** | Derived over an edge join; not worth an indexable sort key. Recorded in the registry |

### ADR-0111 amendment text (preview — save during A1, subject to review)

> **Amended 2026-07-30 — `PartOf` cardinality.** A component (`Application` or `Service`) may be `PartOf` **at most one** `System`. `PartOf` remains an edge (no FK), but the edge set is constrained: writing a membership replaces any existing one. Enforced by `PUT /catalog/{applications|services}/{id}/system` (atomic replace) and by a 409 conflict on `POST /catalog/relationships` when the source already has a `PartOf` edge. Other relationship types keep their unconstrained many-to-many cardinality. Rationale: makes System a grouping *hierarchy* (E-03.F-03.S-02) instead of an overlapping tag set — tag semantics are E-03.F-04's job.

## 3. A1 — components / changes

### 3.1 Contracts (`Kartova.Catalog.Contracts`)

- `SetSystemRequest(Guid? SystemId)` — `null` clears.
- `SystemMembershipResponse(Guid? SystemId, string? SystemDisplayName)` — the post-write state; both null when cleared. Returned so the FE renders the new membership without a follow-up fetch.
- Both `[ExcludeFromCodeCoverage]` per the Contracts coverage rule.

### 3.2 Application / Infrastructure

- `SetComponentSystemCommand(EntityRef Component, Guid? SystemId)` (Application).
- `SetComponentSystemHandler` (Infrastructure), inside the ambient `ITenantScope` transaction:
  1. delete **every** existing `PartOf` edge whose source is the component (defensive: S-01's permissive window and direct DB writes could have left more than one);
  2. if `SystemId` is non-null, insert `Relationship.CreateManual(component, system, PartOf, …)`;
  3. append the audit entries for what actually changed (delete and/or create);
  4. return `SystemMembershipResponse`.
- **No-op short-circuit:** if the single existing edge already targets the requested System, write nothing (no audit noise) and return the current state — PUT stays idempotent (ADR-0096).

### 3.3 Endpoints (`CatalogModule` + `CatalogEndpointDelegates`)

`PUT /api/v1/catalog/applications/{id:guid}/system` and `PUT /api/v1/catalog/services/{id:guid}/system`, sharing one delegate parameterized by `EntityKind`:

| Outcome | Status |
|---|---|
| Assigned / changed / cleared / no-op | `200 SystemMembershipResponse` |
| Component unknown or cross-tenant | `422` (`ProblemTypes.InvalidSourceEntity`, via `ICatalogEntityLookup`) |
| `systemId` unknown or cross-tenant | `422` (`ProblemTypes.InvalidTargetEntity`) |
| Caller in neither team, not OrgAdmin | `403` (`AuthorizeEitherTeamAsync`) |
| Missing `catalog.relationships.write` | `403` (policy) |

Plus, in `CreateRelationshipAsync`: after the existing duplicate pre-check, when `req.Type == PartOf`, a source-scoped `AnyAsync` → `409` (new `ProblemTypes.ComponentAlreadyInSystem`, detail names the current System). The existing exact-duplicate 409 stays first so an identical re-POST keeps its current problem type.

Both routes carry full `Produces`/`ProducesProblem` metadata matching the `/successor` registration. Neither takes list parameters, so `CursorListQueryParameterTransformer` is not involved.

### 3.4 Frontend (`web/`)

| File | Change |
|---|---|
| `relationships/relationshipTypeRules.ts` | add `export type EntityKind = RelationshipKind \| "system"` + `isEntityKind` guard. `RelationshipKind`, `CREATABLE_TYPES`, `isRelationshipKind` unchanged |
| `relationships/graphModel.ts` | `ENTITY_KIND_LABEL.system = "System"`; `entityDetailPath` accepts `EntityKind` with segment `systems`. **Fixes today's raw `system` badge + `/catalog/undefined/{id}` link** on App/Service relationships lists |
| `api/relationships.ts` | `useEntitySearch` accepts `system` → `GET /systems?displayNameContains&sortBy=displayName&limit=10` |
| `api/systems.ts` | `useSetComponentSystem()` mutation (PUT), invalidating the component's relationships query, the target/previous System's members query, and the systems list |
| `components/EntitySearchCombobox.tsx` | `kind: EntityKind` |
| `components/AssignSystemDialog.tsx` *(new)* | Single-select. Two modes: `mode="component"` (fixed component, search Systems) and `mode="system"` (fixed System, search components with an Application/Service kind toggle). Submits the PUT; surfaces `ProblemDetails` inline; toast on success |
| `components/SystemMembershipRow.tsx` *(new)* | On App/Service Overview: `System <link>` + `Change` / `Remove`, or `Not assigned` + `Assign`. Derives state from the existing outgoing-relationships query filtered to `partOf` |
| `components/SystemMembersSection.tsx` | `Assign component` header button + per-row `Remove` (PUT `null`) with a confirm; empty state gains a CTA |
| `pages/ApplicationDetailPage.tsx`, `pages/ServiceDetailPage.tsx` | mount `SystemMembershipRow` in the Overview metadata block |

All mutating affordances gated on `catalog.relationships.write` via `usePermissions`. Generated client regenerated (new endpoints); `openapi-snapshot.json` refreshed.

### 3.5 What A1 does **not** do

- System nodes in the graph explorer / mini-graph → **FU-A** (unchanged).
- Bulk multi-select assignment (assign N components in one dialog pass) → deferred, flagged not silent.
- `memberCount` on the Systems list → deferred (needs a backend aggregate).
- Nested Systems, System-owns-API → still rejected by `RelationshipTypeRules` (unchanged).
- Backfill/repair of pre-existing multi-membership rows → not needed; A1 tolerates and collapses them on next write.

## 4. A2 — list surface (ADR-0107 / ADR-0095)

Field-addition trigger for the new System field, per CLAUDE.md, on both lists that already have a screen:

| List | Column? | Sortable? | Filter? |
|---|---|---|---|
| Applications | ✓ `System` (link, "—" when unassigned) | ✗ (derived; no index) | ✓ multi-select `systemId` |
| Services | ✓ `System` | ✗ | ✓ multi-select `systemId` |
| Systems | ✗ `memberCount` (deferred, needs aggregate) | — | — |
| APIs | ✗ — APIs cannot be `PartOf` a System | — | — |

**Backend:** `SystemId` (`Guid[]`) added to `ListApplicationsQuery` / `ListServicesQuery`; in each handler an `Any()` sub-query over `db.Relationships` (`Type == PartOf && Source == entity && Target.Kind == System && SystemId.Contains(Target.Id)`), applied **before** paging so a hidden row never becomes a cursor boundary, and encoded into the cursor `expectedFilters` map (`systemId` = ordered comma-joined ids) exactly like `teamId`. Column data comes from a **batched per-page enrichment** — one query over the page's ids returning `(componentId → systemId, systemDisplayName)` — mirroring the existing creator enrichment; no N+1.

**Frontend:** `System` column on both `DataTable`s; a System `MultiSelect` in each `<FilterBar>` sourced from `/systems` (same shape as the existing team facet); `f`-map wiring via `useListFilters`. Sort allowlists unchanged.

`docs/design/list-filter-registry.md` — update the Applications and Services rows (new field: column ✓, sort ✗, filter ✓ implemented) and add the deferral note on the Systems row.

## 5. Testing strategy (per docs/TESTING-STRATEGY.md)

**A1 — real-seam integration** (`Kartova.Catalog.IntegrationTests`, `KartovaApiFixtureBase`: real Postgres + RLS + real JWT) — new `SetComponentSystemTests`:

1. assign an Application → 200, edge exists, audit row written *(happy)*;
2. assign a Service → 200 *(happy, second kind)*;
3. re-assign to a different System → 200, **old edge gone**, exactly one `PartOf` remains;
4. `systemId: null` clears → 200 with nulls, no `PartOf` rows;
5. same-System PUT → 200, no new audit entry *(idempotence)*;
6. unknown / cross-tenant `systemId` → 422; unknown component → 422 *(negative)*;
7. caller in neither team (Member of a third team) → 403 *(negative, ADR-0108)*;
8. caller without `catalog.relationships.write` → 403;
9. pre-seeded **two** `PartOf` edges → PUT collapses to one *(defensive path)*.

Extended `CreatePartOfRelationshipTests`: second `PartOf` POST for the same source → 409 `ComponentAlreadyInSystem`, and the identical-edge re-POST still returns the existing duplicate 409.

**A1 — unit:** `SetComponentSystemHandler` (replace vs no-op vs clear; audit calls asserted with NSubstitute). **A1 — frontend:** `AssignSystemDialog` (both modes, validation, ProblemDetails surfacing), `SystemMembershipRow` (assigned/unassigned/permission-gated), `SystemMembersSection` (assign button, row remove, `getAllByRole("rowheader").length > 0`).

**A2:** handler filter tests (`ListApplicationsHandlerFilterTests` sibling) + real-seam pagination-with-filter test (cursor mismatch when the filter changes mid-pagination) + FE column/filter tests.

**Gate 6 (mutation):** **blocking** — both sub-slices touch Application/Infrastructure logic (`SetComponentSystemHandler`, list filter predicates). Target ≥80% on changed files.

**Gate 10 (ADR-0084):** cold-start dev server, authenticate, in-SPA navigation. A1: assign from a System's Members tab → verify the row and the component page's System row → Change → Remove; screenshots + 0 console errors. A2: filter the Applications list by System, confirm the column and a paged filter round-trip.

**E2E-impact trigger:** A1 adds a row to the App/Service Overview tab and mutating controls to the System Members tab. Before merge, audit `e2e/tests/relationship-drift.spec.ts` and any system/detail-tab spec for assumptions about those surfaces, update what's affected, and run the affected specs locally (`e2e/run.sh <spec>`); record the outcome in the DoD ledger. A candidate new spec (assign → reassign → clear) is the expected follow-up if gate 10 finds anything.

## 6. Definition of Done

The eleven CLAUDE.md gates apply as written (not restated). Gate 6 is **blocking** for both sub-slices (Domain/Application logic). Per-sub-slice DoD ledger + `gate-findings.yaml` under `docs/superpowers/verification/2026-07-30-catalog-system-membership/a1/` and `…/a2/`.

## 7. Impact analysis (codelens) — plan input

A1 changes **existing** C# behavior, so each plan's `## Impact Analysis (codelens)` section must be grounded, not grepped:

- `CreateRelationshipAsync` (delegate) — `find_callers` / `find_references` before adding the `PartOf` 409 branch; confirm every caller and its integration tests are covered by a task.
- `ICatalogEntityLookup.Find`, `AuthorizeEitherTeamAsync`, `Relationship.CreateManual` — reused unchanged; `find_references` to confirm no signature pressure.
- A2: `ListApplicationsQuery` / `ListServicesQuery` are **records whose shape changes** — `find_references` for every construction site (endpoint delegates, handler tests) so no positional-argument break slips through.
- Not codelens: `KartovaPermissions.CatalogRelationshipsWrite` is a `const` — use `Grep` for its blast radius (codelens under-reports const refs). No new permission ⇒ no 5-sync edit expected; the grep confirms it.

## 8. Size estimate

| Sub-slice | Production LOC (excl. tests / DTOs / generated) |
|---|---|
| A1 | ~420 (≈180 backend: command + handler + 2 routes + 409 branch; ≈240 FE: 2 new components + dialog + 4 touched files) |
| A2 | ~250 (≈140 backend filter + enrichment; ≈110 FE column + filter) |

Both inside the ~800 ceiling; A1 near the ~400 target. Split because the combined ~670 would ship two independently reviewable concerns in one PR.
