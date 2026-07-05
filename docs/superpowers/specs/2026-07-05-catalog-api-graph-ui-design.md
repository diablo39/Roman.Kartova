# Slice — Catalog: API graph UI (render · navigate · author)

**Date:** 2026-07-05
**Story / work item:** E-02.F-03 · **FU-A** (registered in `2026-07-04-catalog-api-connectivity-edges-design.md` §9) — folds in **FU-A1** (API-detail relationships list)
**Phase:** 1 — Core Catalog & Notifications
**Branch (proposed):** `feat/catalog-api-graph-ui`
**Governing decisions:** [ADR-0111](../../architecture/decisions/ADR-0111-api-first-class-entity-provider-instance-fields.md) (**Revised 2026-07-04** — provider/instance/consumer are all edges), [ADR-0068](../../architecture/decisions/ADR-0068-relationship-vocabulary.md) (relationship vocabulary), [ADR-0094](../../architecture/decisions/ADR-0094-frontend-ui-stack.md) (Untitled UI / react-aria), [ADR-0084](../../architecture/decisions/ADR-0084-frontend-verification.md) (browser verification), [ADR-0108](../../architecture/decisions/ADR-0108-either-endpoint-relationship-authority.md) (either-endpoint authority)

---

## 1. Goal

Make the API connectivity edges shipped in the connectivity slice (PR #58) **visible, navigable, and authorable** in the web UI. Today the frontend is deliberately blind to `api`: two `isRenderableKind` guards filter API edges out of the detail-page relationship tables and the graph model, and ~10 graph-subsystem files hardcode `application|service`. This slice teaches the frontend the `api` kind and the three creatable edge types (`instanceOf`, `providesApiFor`, `consumesApiFrom`) end-to-end:

1. **Render + navigate** — the graph explorer and detail-page relationship tables show `Api` nodes/rows and route to `/catalog/apis/:id`.
2. **Author** — `AddRelationshipDialog` offers the new types with an Api entity picker, driven by the existing generic pair-matrix machinery.
3. **API-detail relationships (FU-A1)** — the Api detail page gets a read-only "Providers & consumers" list.

## 2. Nature: frontend-only

The backend already ships everything: `EntityKind.Api`, `RelationshipType.{InstanceOf,ProvidesApiFor,ConsumesApiFrom}`, per-type `RelationshipTypeRules.IsAllowedPair`, `CatalogEntityLookup` Api branch, and both the relationships-list and graph endpoints return API edges/nodes **unfiltered**. **No migration, no contract, no C# production change.** This is a pure web slice — which shifts the DoD weight from backend seams (gates 3/4/6) to frontend component tests + Playwright (ADR-0084).

## 3. Pre-requisites (already on master)

- `EntityKind { Application, Service, Api }`; `RelationshipType` incl. `InstanceOf`/`ProvidesApiFor`/`ConsumesApiFrom`; `RelationshipTypeRules.IsCreatable`/`IsAllowedPair` per-type (verified in `RelationshipTypeRules.cs`).
- `ListRelationshipsForEntityHandler` and `GraphTraversalHandler` emit any-kind nodes/edges (no kind filter); `CatalogEntityLookup` enriches Api displayName/teamId.
- Api list + detail routes exist (`/catalog/apis`, `/catalog/apis/:id`) with `useApisList` (supports `displayNameContains`, `sortBy=displayName`) and `useApi` (FU-9, PR #57).
- Generated `ListRelationships.entityKind` is typed `string` (backend validates) → `entityKind=api` passes typecheck with no codegen change.

## 4. Design decisions (locked in brainstorming 2026-07-05)

1. **Full FU-A** — render + navigate **and** authoring, one slice (~200–250 prod LOC, under the 400 target; no decomposition).
2. **Generic Outgoing/Incoming framing** — the single detail-page relationship section is reframed from "Dependencies/Dependents" to **"Relationships"** with **Outgoing/Incoming** groups; every row carries a type badge (Depends on / Provides API for / Consumes API from / Instance of). The dialog is already type-driven; only copy changes.
3. **FE pair matrix = stricter creatable subset of the backend.** `dependsOn` deliberately does **not** offer `api` as a target in the UI (API links use provides/consumes); the backend's `DependsOn ⇒ true` still permits it at the API layer. This replaces the placeholder "dependsOn allows every pair" oracle with a real per-type matrix.

   | type | source kinds | target kinds |
   |------|------|------|
   | `dependsOn` | application, service | application, service |
   | `instanceOf` | service | application |
   | `providesApiFor` | application, service | api |
   | `consumesApiFrom` | application, service | api |

4. **No new visual system for `api` nodes.** `EntityGraphNode` already renders kind-agnostically (application/service look identical today); API parity needs only `ENTITY_KIND_LABEL.api = "API"` + a filter-chip option. (A per-kind color/icon is out of scope — it would be a new axis for all kinds, not API-specific.)
5. **FU-A1 folded in — read-only, incoming-only section variant.** An `Api` is never a source, so its "Outgoing" group is always empty and it has no authorable types. `RelationshipsSection` grows a mode that (a) hides the Outgoing group and (b) suppresses add buttons for such focus. Mounted on `ApiDetailPage` as a single "Providers & consumers" (incoming) list.
6. **No new permission.** `CatalogRelationshipsWrite` already authorizes all creatable types on the create endpoint (ADR-0108 either-endpoint authz). No 5-sync.

## 5. Changes

### 5.1 Frontend (production)

| File | Change |
|------|--------|
| `relationships/relationshipTypeRules.ts` | `RelationshipKind` += `"api"`; new `ALL_KINDS = ["application","service","api"]`; `CreatableRelationshipType` += `instanceOf`/`providesApiFor`/`consumesApiFrom`; add `relationshipTypeLabel` entries; `CREATABLE_TYPES` += the three; replace `isAllowedPair` with the §4 #3 per-type matrix; `allowedOtherKinds` iterates `ALL_KINDS`. **Remove** now-unused `isRenderableKind` after its consumers are gone. |
| `relationships/graphModel.ts` | Drop the `isRenderableKind` skip in `toGraphModel` (render `api` neighbours); `ENTITY_KIND_LABEL.api = "API"`; `parseEntityRef` accepts `api`; `entityDetailPath` maps `api → /catalog/apis/:id`. `GraphNodeData.kind`/`FocusedEntity.kind` widen via `RelationshipKind`. |
| `relationships/graphMerge.ts`, `relationships/graphFilter.ts`, `relationships/useGraphFilters.ts` | `api` flows through generically once the kind union widens; remove any `application\|service` hardcoding that blocks `api` (per grep). |
| `api/graph.ts` | `GraphFocus.kind` widens via `RelationshipKind`; `parseNode` already casts — no logic change. |
| `components/GraphFilterControls.tsx` | `KIND_OPTIONS` += `{ label: "API", value: "api" }`. |
| `components/RelationshipsSection.tsx` | Remove both `isRenderableKind` filters; titles → **"Relationships"** / **Outgoing** / **Incoming**; buttons → **"Add outgoing"/"Add incoming"**; generic help tooltip copy; `entityLink` maps `api → /catalog/apis/:id`; gate add buttons on `offerableTypes(role, kind).length > 0`; add **read-only/incoming-only mode** (prop, e.g. `variant="incoming-only"`) that hides the Outgoing group + all add buttons. |
| `components/AddRelationshipDialog.tsx` | Generic dialog title + subtitle (drop the "X depends on…" wording); Type dropdown + reactive kind-select/picker already handle the new types via the matrix — no structural change. |
| `api/relationships.ts` (`useEntitySearch`) | Add an `api` branch → `GET /catalog/apis { displayNameContains: query, sortBy: "displayName", sortOrder: "asc", limit: 10 }`, map items to `{ kind: "api", id, displayName }`. |
| `components/EntitySearchCombobox.tsx` | Already generic over `kind`; placeholder copy handles `api` (`Search apis…`). No logic change expected. |
| `pages/ApiDetailPage.tsx` | Mount `<RelationshipsSection entityKind="api" entityId={api.id} entityTeamId={api.teamId} entityDisplayName={api.displayName} variant="incoming-only" />` below the details card. |

`EntityGraphNode.tsx`/`DependencyMiniGraph.tsx`: no change beyond the label they already read from `ENTITY_KIND_LABEL`.

### 5.2 Tests

| File | Change |
|------|--------|
| `relationships/__tests__/relationshipTypeRules.test.ts` | Replace placeholder oracle with per-type matrix: `instanceOf` service→application only; `providesApiFor`/`consumesApiFrom` {app,service}→api only; `dependsOn` app/service→app/service and **not** →api; `offerableTypes` from an application (no `instanceOf`) vs a service (all four); an `api` fixed entity yields empty `offerableTypes` (both roles). |
| `relationships/__tests__/graphModel.test.ts` | `toGraphModel` now includes `api` neighbours; `parseEntityRef("api:…")` resolves; `entityDetailPath("api", id)` → `/catalog/apis/:id`; `ENTITY_KIND_LABEL.api`. |
| `relationships/__tests__/graphFilter.test.ts` (+ `useGraphFilters`) | `api` kind filters/dims correctly. |
| `components/__tests__/AddRelationshipDialog.test.tsx` | Offers `providesApiFor`/`consumesApiFrom` from an application; `instanceOf` from a service; selecting an API-target type forces `otherKind=api` and the picker searches APIs; submit payload shape per role. |
| `components/__tests__/RelationshipsSection.test.tsx` | Generic Outgoing/Incoming titles + type badges; api row links to `/catalog/apis/:id`; add buttons hidden when `offerableTypes` empty; **incoming-only variant** hides Outgoing + add buttons; `getAllByRole("rowheader").length > 0` (ADR-0084 isRowHeader guard). |
| `pages/__tests__/ApiDetailPage.test.tsx` | Renders providers/consumers list; no add buttons; empty state when no edges. |
| `components/__tests__/GraphFilterControls.test.tsx` | API chip present + selectable. |

**Gate 5 real-seam artifacts: N/A — reason: no HTTP/auth/DB/middleware seam is touched (backend unchanged; the create/list/graph seams were covered by the connectivity slice's integration tests).** Frontend component tests (vitest + RTL) + Playwright are the test deliverables. No Dockerfile/`COPY` change → container-build gate runs unaffected.

**Playwright (ADR-0084, cold-start first):** (a) focus a service in the graph explorer, expand to an `Api` node, click it → lands on `/catalog/apis/:id`; (b) open "Add outgoing", choose "Provides API for", pick an API in the picker, save → the row appears with the correct badge; (c) open an API detail page → its consumers list renders. Open the dialog in a real browser (react-aria table blank-page risk).

### 5.3 Docs / hygiene

| File | Change |
|------|--------|
| `CLAUDE.md` (Architectural guardrails, ADR-0111 bullet) | **Fix stale line.** It still reads "provider (`Api.implementedByApplicationId`) + instance (`Service.applicationId`) are nullable **FK fields** … **not** graph edges". ADR-0111 was **revised 2026-07-04 to an all-edge model**. Rewrite to reflect: provider = `provides-api-for` edge ({App,Service}→Api), instance = `instance-of` edge (Service→Application), consumers = `consumes-api-from` edge; exposure/depends-on derived over edges (FU-B, deferred). CLAUDE.md's own rule mandates fixing stale guardrail lines. |
| `docs/design/list-filter-registry.md` | No change — no new list endpoint/screen; the relationships list default sort is unchanged. |
| `docs/product/CHECKLIST.md` | On completion, note FU-A + FU-A1 done under E-02.F-03. |

## 6. Impact Analysis (codelens/LSP)

**N/A — frontend-only.** No existing **C# symbol** signature or behavior changes; the backend enums/rules/handlers this slice consumes are unchanged (verified on master: `EntityKind.Api`, the three types, `IsAllowedPair`, `CatalogEntityLookup` Api branch, unfiltered list/graph handlers all pre-exist). TypeScript has no codelens/LSP MCP in this repo; FE blast radius (the `application|service` hardcoding across ~10 graph files, the two `isRenderableKind` guards, `isRenderableKind` removal) was established by grep and enumerated in §5.1 — every hit maps to a task. Confirm at plan time with a fresh `grep -rn "isRenderableKind\|\"application\"\|\"service\"" web/src/features/catalog`.

## 7. Definition of Done

The eight always-blocking gates + conditional mutation gate in **CLAUDE.md → Working agreements → Definition of Done** apply verbatim. Mutation gate (6) is **N/A/should-do** — the diff touches **no** Domain/Application C# logic (frontend-only); note the reason in the ledger rather than running Stryker. Frontend verification (ADR-0084 Playwright) is the load-bearing gate here and is **not** optional. Run `scripts/ci-local.sh frontend` (Release build + typecheck + vitest) green before push; stop the dev server first (npm ci EPERM-vs-5173 lock). DoD ledger + `gate-findings.yaml` under `docs/superpowers/verification/2026-07-05-catalog-api-graph-ui/`.

## 8. Out of scope (explicit deferrals)

| Item | Owning follow-up |
|------|------|
| Derived `exposes` (Service→Api via `instance-of ∘ provides-api-for`) + service↔service `depends-on` | **FU-B** (ADR-0111 §Decision 3/5 revised) |
| Async API entity + `publishes-to`/`subscribes-from` + Broker | **FU-C** / E-02.F-04 |
| System grouping (`part-of`/`contains`) + derived System API surface | **FU-D** / E-03.F-03 |
| Unified sync/async API view per service | **FU-E** / E-02.F-03.S-03 |
| Per-kind node color/icon in the graph | none — deferred; parity uses labels only |
| "View in graph" entry point focused on an `Api` from `ApiDetailPage` | none — API nodes reachable by expansion from providers/consumers; add later if desired |

## 9. Self-review

- No placeholders/TBD; every §5 file maps to a concrete change.
- Consistent: §4 decisions ↔ §5 changes ↔ §5.2 tests (matrix, incoming-only variant, generic framing all covered).
- Scope: single slice, ~200–250 prod LOC, frontend-only — no decomposition.
- Ambiguity resolved: FE matrix is intentionally stricter than backend (dependsOn↛api); incoming-only variant is the only new component axis; `api` needs no visual system.
