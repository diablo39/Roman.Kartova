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
| **Cardinality** | A component is in **at most one** System. **ADR-0111 amendment.** | Backstage's model; makes the hierarchy browse (S-02) a tree rather than a DAG, and makes "which System owns this?" answerable. Enforced by a **partial unique index** plus clean 409s on both write paths (§3.3) — decided 2026-07-30 after review showed write-time checks alone leave a real concurrent-double-assign race |
| **Authority on a move** | Every edge the write touches must be authorized on its own endpoints | A move deletes one edge and inserts another; gating only on the destination System's team would let its steward strip a component out of a System they have no claim on — weaker than the `DELETE`+`POST` pair this endpoint replaces (ADR-0108 extension, see the amendment) |
| Model | Stays an **edge** (`PartOf`), not a `SystemId` FK on the component | ADR-0111's all-edge decision holds; S-01 already ships graph/relationship visibility over the edge. At-most-one is a *cardinality* constraint on the edge, not a modelling change |
| Write path | Dedicated setter `PUT /catalog/{applications\|services}/{id}/system` | Atomic set/replace/clear in one call; mirrors the existing `PUT .../team` (E-03.F-02.S-02) and `PUT .../successor` (ADR-0110) setters. Both UI sides call the same endpoint |
| Generic path | `POST /catalog/relationships` with `PartOf` from a source that already has one → **409** | Keeps the invariant true regardless of which door the write comes through (API clients, CLI later) |
| Permission | Reuse `KartovaPermissions.CatalogRelationshipsWrite` | The operation *is* a relationship write. No new permission ⇒ **no 5-touchpoint sync** |
| Authorization | ADR-0108 either-endpoint: OrgAdmin, or member of the **component's** team **or** the **System's** team | Identical to `POST /relationships`; reuses `AuthorizeEitherTeamAsync` |
| Audit | Reuse `CatalogAuditActions.RelationshipCreated` / **`RelationshipRemoved`** with `RelationshipAuditData` | A membership change *is* edge create/delete; the payload already carries `type=partOf` + both endpoints, so the trail stays queryable without new action constants. (Corrected 2026-07-30: an earlier draft named `RelationshipDeleted`, which does not exist — `CatalogAuditActions.cs:21`.) |
| Component-side read | Reuse `GET /relationships?entityKind=…&entityId=…&direction=outgoing`, filtered to `partOf` client-side | No new GET. Same drift-tolerant filter `SystemMembersSection` already applies on the incoming side |
| FE kind model | New narrow `EntityKind = RelationshipKind \| "system"` for **rendering + search only** | Lets `system` render with a label and a working link, and lets the combobox search Systems, **without** widening `RelationshipKind` (graph URL tokens, creatable-edge matrix, graph filters stay untouched) |
| Graph rendering of System nodes | Still **FU-A** | Out of scope here; A1 only fixes the *relationships-list* rendering of `system` refs |
| Sort by System | **No** | Derived over an edge join; not worth an indexable sort key. Recorded in the registry |

### ADR-0111 amendment text (reviewed 2026-07-30 — save during A1)

> **Amended 2026-07-30 — `PartOf` cardinality, write path, and authority.**
>
> **Overrides** the 2026-07-04 revision's "cardinality is intentionally not capped (max-flexibility)" statement, **for `PartOf` only**. Every other relationship type keeps its unconstrained many-to-many cardinality.
>
> **Cardinality.** A component (`Application` or `Service`) may be `PartOf` **at most one** `System`. `PartOf` remains an edge, not an FK — this is a constraint on the edge set, not a change to the all-edge model. Rationale: makes System a grouping *hierarchy* (E-03.F-03.S-02) rather than an overlapping set; overlapping-set semantics belong to tags (E-03.F-04).
>
> **Enforcement is at the database, not only at write time.** Partial unique index `ux_relationships_one_system` on `relationships (tenant_id, source_kind, source_id) WHERE type = 'PartOf'`. The application's pre-checks return a clean 409; a lost concurrent race surfaces as `23505` and is mapped to the same 409. Without the index the invariant would be advisory only — two concurrent writers naming different Systems would both pass their pre-check and both commit, because the pre-existing `ux_relationships_edge` includes the target columns and therefore blocks exact duplicates only. This amendment adds one schema migration (`AddOneSystemPerComponentIndex`); it is the first ADR-0111 amendment to do so.
>
> **Canonical write path.** `PUT /catalog/{applications|services}/{id}/system` is the canonical door: idempotent replacement (ADR-0096), `null` clears, and it atomically replaces an existing membership. `POST /catalog/relationships` with `type=PartOf` remains supported for **first assignment only** and returns `409 component-already-in-system` when the component already belongs to a System — it never moves a component. `DELETE /relationships/{id}` remains a valid way to remove a membership. New clients (CLI, auto-import) should use the PUT.
>
> **Authority (extends ADR-0108 to composite operations).** ADR-0108 authorizes each edge on its own endpoints; a *move* mutates two edges (delete old, insert new). A caller is authorized when they are OrgAdmin, or a member of the component's team (an endpoint of every edge involved), or a member of the steward team of **every** System whose edge the write touches. Being a steward of only the destination System does not permit removing a component from another System.
>
> **Hierarchy placement (input to E-03.F-03.S-02).** In the Org → Team → System → Component browse tree, a System nests under **its own steward team** (`System.TeamId`), and its members appear beneath it regardless of which team owns them. Consequence to document on that screen: per-team component counts in the tree will not match the Teams page, because a component owned by team A can sit under a System stewarded by team B.

## 3. A1 — components / changes

### 3.1 Contracts (`Kartova.Catalog.Contracts`)

- `SetSystemRequest(Guid? SystemId)` — `null` clears.
- `SystemMembershipResponse(Guid? SystemId, string? SystemDisplayName)` — the post-write state; both null when cleared. Returned so the FE renders the new membership without a follow-up fetch.
- Both `[ExcludeFromCodeCoverage]` per the Contracts coverage rule.

### 3.2 Application / Infrastructure

- `SetComponentSystemCommand(EntityRef Component, Guid? SystemId)` (Application).
- `SetComponentSystemHandler` (Infrastructure), inside the ambient `ITenantScope` transaction, executing `SystemMembership.Decide`:
  1. delete every existing `PartOf` edge whose source is the component **except one already pointing at the requested System** (which is kept, making the write a true no-op — see the idempotence bullet). Deleting *all* would churn the edge id and audit trail on every repeat PUT. Multiple stray edges from S-01's permissive window collapse here;
  2. if no kept edge, insert `Relationship.CreateManual(component, system, PartOf, …)`;
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
| Caller not OrgAdmin, not in the component's team, and not in the steward team of every System whose edge is touched | `403` (per-edge `AuthorizeTargetTeamAsync`) |
| Missing `catalog.relationships.write` | `403` (policy) |
| Unauthenticated | `401` |
| Lost a concurrent membership race (`23505` on `ux_relationships_one_system`) | `409` (`ProblemTypes.ComponentAlreadyInSystem`) |

Plus, in `CreateRelationshipAsync`: after the existing duplicate pre-check, when `req.Type == PartOf` **and the source is an Application or Service**, a source-scoped nullable projection → `409` (new `ProblemTypes.ComponentAlreadyInSystem`, detail names the current System by display name). The existing exact-duplicate 409 stays first so an identical re-POST keeps its current problem type. The kind scoping keeps the guard from silently imposing at-most-one-parent on `System → System` edges if S-02 ever enables nesting.

**Schema (decided 2026-07-30):** one migration, `AddOneSystemPerComponentIndex` — partial unique index `ux_relationships_one_system` on `relationships (tenant_id, source_kind, source_id) WHERE type = 'PartOf'`, written as raw SQL in the migration because EF 10 cannot express `HasIndex` over `ComplexProperty` columns (`EfRelationshipConfiguration.cs:55-56`). Pre-flight check for existing duplicate memberships before applying.

Both routes carry full `Produces`/`ProducesProblem` metadata matching the `/successor` registration. Neither takes list parameters, so `CursorListQueryParameterTransformer` is not involved.

### 3.4 Frontend (`web/`)

| File | Change |
|---|---|
| `relationships/relationshipTypeRules.ts` | add `export type EntityKind = RelationshipKind \| "system"` + `isEntityKind` guard. `RelationshipKind`, `CREATABLE_TYPES`, `isRelationshipKind` unchanged |
| `relationships/graphModel.ts` | `ENTITY_KIND_LABEL.system = "System"`; `entityDetailPath` accepts `EntityKind` with segment `systems`. **Fixes today's raw `system` badge + `/catalog/undefined/{id}` link** on App/Service relationships lists |
| `api/relationships.ts` | `useEntitySearch` accepts `system` → `GET /systems?displayNameContains&sortBy=displayName&limit=10` |
| `api/systems.ts` | `useSetComponentSystem()` mutation (PUT), invalidating the component's relationships query, the target/previous System's members query, and the systems list |
| `components/EntitySearchCombobox.tsx` | `kind: EntityKind` |
| `components/AssignSystemDialog.tsx` *(new)* | Component-side: fixed component, searches Systems, single-select; also carries the "Remove from System" action. Submits the PUT; surfaces `ProblemDetails`; toast on success |
| `components/AddSystemMemberDialog.tsx` *(new)* | System-side: fixed System, Application/Service kind toggle, searches components, calls the same PUT. **Revised 2026-07-30:** originally specced as one dialog with a `mode` prop; split into two because the two sides differ in which endpoint is fixed and only the system-side needs a kind toggle — a `mode` prop would branch nearly every line. Same behavior, clearer types, independently testable |
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
6. unknown `systemId` → 422; unknown component → 422; **genuinely cross-tenant System** (seeded in tenant B, PUT from tenant A) → 422, so the test distinguishes "absent" from "RLS hid it" per TESTING-STRATEGY.md §5 *(negative)*;
7. caller in neither team (Member of a third team) → 403 *(negative, ADR-0108)*; **plus** a member of only the *destination* System's team moving a component **out** of another System → 403 (per-edge authority, see the authorization row in §2);
8. caller without `catalog.relationships.write` → 403 — implemented as two new rows in `CatalogPermissionMatrixTests`, this repo's canonical mechanism, not a bespoke test; **and** an unauthenticated request → 401;
9. the DB itself refuses a second `PartOf` edge for one component (seeded past every application guard) → `ux_relationships_one_system` violation. **Not** an endpoint test that pre-seeds two edges — the index makes that precondition unseedable; the collapse logic is covered by the pure `SystemMembership.Decide` tests instead. Plus: a membership write leaves an unrelated `dependsOn` edge intact (pins the `Type == PartOf` filter, which nothing else does);
10. audit read-back: an assign followed by a move writes both `relationship.created` and `relationship.removed` rows (`Fx.ReadAuditLogAsync`) — no integration test otherwise touches `audit_log`;
11. the idempotent re-PUT asserts the **response body** still carries the System id *and* display name, not just a 200.

Extended `CreatePartOfRelationshipTests`: second `PartOf` POST for the same source → 409 `ComponentAlreadyInSystem`, and the identical-edge re-POST still returns the existing duplicate 409.

**A1 — unit:** `SystemMembership.Decide` (8 cases incl. both multi-edge collapses). `SetComponentSystemHandler` (assign · clear · same-System no-op · **move, asserting BOTH the removal and the creation audit call** · clear-when-unassigned writes nothing · a write for one component leaves another's membership intact — that last one kills the "drop the `Source.Id` filter" mutation, which would otherwise wipe every membership in the tenant). **A1 — frontend:** `AssignSystemDialog` (both modes, validation, ProblemDetails surfacing), `SystemMembershipRow` (assigned/unassigned/permission-gated), `SystemMembersSection` (assign button, row remove, `getAllByRole("rowheader").length > 0`).

**A2:** handler filter tests (`ListApplicationsHandlerFilterTests` sibling) + real-seam pagination-with-filter test (cursor mismatch when the filter changes mid-pagination) + FE column/filter tests.

**Gate 6 (mutation):** **blocking** — both sub-slices touch Application/Infrastructure logic (`SetComponentSystemHandler`, list filter predicates). Target ≥80% on changed files.

**Gate 10 (ADR-0084):** cold-start dev server, authenticate, in-SPA navigation. A1: assign from a System's Members tab → verify the row and the component page's System row → Change → Remove; screenshots + 0 console errors. A2: filter the Applications list by System, confirm the column and a paged filter round-trip.

**E2E-impact trigger — one break is already diagnosed, not merely possible.** `useComponentSystem` on the Overview tab uses the *same* TanStack query key as `RelationshipsSection`'s outgoing call on the Dependencies tab, and the global `staleTime` is 30 s (`web/src/app/providers.tsx`). The Overview mount therefore warms that cache and the later tab click fires **no** request — so `e2e/tests/relationship-drift.spec.ts`'s `page.waitForResponse`, registered before that click, times out. Fix the spec (assert rendered state), not the app: one request instead of two is the desired behavior. Also: the Members-tab Remove goes through `window.confirm`, which Playwright auto-dismisses — any spec clicking it must register `page.on("dialog", …)`. `detail-tabs.spec.ts` covers only the API detail page and is unaffected. Run every touched spec locally (`e2e/run.sh <spec>`) and record it in the DoD ledger. A candidate new spec (assign → reassign → clear) is the expected follow-up if gate 10 finds anything.

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
| A1 | **~510–570** (≈180 backend: command + handler + 2 routes + per-edge authz + 409 branch; ≈330–390 FE: 3 new components ≈250 plus the `SystemMembersSection` rework and 5 touched files) |
| A2 | ~250 (≈140 backend filter + enrichment; ≈110 FE column + filter) |

**Revised 2026-07-30 after review:** A1 was first estimated at ~420, which undercounted the frontend by omitting the `SystemMembersSection` rework. A1 now sits over the ~400 target and under the ~800 ceiling. The A1/A2 split stands — combined they would land at ~760–820, at or past the ceiling, and would mix two independently reviewable concerns in one PR.
