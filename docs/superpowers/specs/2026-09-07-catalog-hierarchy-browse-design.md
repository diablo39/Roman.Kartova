# Design — Catalog Hierarchy Browse (Org → Team → System → Component)

**Story:** E-03.F-03.S-02 — "As a developer, I want to browse the catalog by Organization → Team → System → Component hierarchy so that I can navigate the service landscape." Acceptance (`docs/product/phases/phase-1-core-catalog.md:87`): *Tree navigation in UI; expandable nodes; entity count per level; breadcrumb trail.* This is the only open story in E-03.F-03 — S-01 fully closed (A1 + A2 + FU-A merged, PR #82/#83/#85).
**Date:** 2026-09-07 · **Author:** Roman Głogowski (AI-assisted)
**ADRs touched:** **none new.** Hierarchy semantics are pinned by the ADR-0111 amendment (2026-07-30): a System nests under its **steward** team; members appear beneath it regardless of owning team, so per-team counts in the tree deliberately will not match the Teams page (`docs/product/CHECKLIST.md:168`). Works inside ADR-0095 (`[BoundedListResult]` for a bounded whole-tree read), ADR-0094 (Untitled UI / react-aria), ADR-0090 (`ITenantScope` + RLS), ADR-0082 (module boundaries), ADR-0084 (browser verification).

---

## 1. Goal

A new read-only browse surface at `/catalog/hierarchy` that renders the whole organization as an expandable tree — **Org → Team → { System, Ungrouped } → Component** — with a per-node component count and a breadcrumb trail for the selected node. Navigation aid only; no mutation, no new authority.

## 2. Starting state (verified 2026-09-07, not assumed)

| Fact | Location |
|---|---|
| `CatalogSystem` carries `TeamId` = **steward** team; members via the `PartOf` edge | `CatalogSystem.cs:9,21` |
| Teams live in the **Organization** module (`ListTeamsQuery`), not Catalog | `src/Modules/Organization/Kartova.Organization.Application/ListTeamsQuery.cs` |
| `PartOf` is `{Application,Service}→System`, **at-most-one, optional** (partial unique index `ux_relationships_one_system`) | ADR-0111 amended 2026-07-30 |
| `SystemResponse` returns `TeamId` (Guid), **not** a team name | `SystemResponse.cs:16` |
| Every Catalog surface resolves team **names FE-side** via `useTeamsList` + a `teamNameById` map — no cross-module name lookup on the backend | `SystemsListPage.tsx:9,29-32` |
| Bounded-graph read precedent: RLS BFS, node-cap 200 + `truncated` flag | `GetCatalogGraphAsync` / `GraphTraversalHandler` |
| Pure-assembler precedent (mutation-testable): `DerivedDependencies.Compute`, `ImpactAnalysis.Compute` | Catalog.Application |
| Routes under `/catalog/*`; sidebar in `Sidebar.tsx` | `web/src/app/router.tsx:52-63`, `web/src/components/layout/Sidebar.tsx` |

**Consequence for module boundary.** Because team-name resolution is already an FE concern everywhere, the hierarchy endpoint stays **Catalog-local**: it assembles structure + counts from Catalog-owned data (systems, `PartOf` edges, App/Service `id`+`displayName`+`teamId`) and returns team **IDs**. The FE joins names and injects empty teams from `useTeamsList`. This avoids a Wolverine cross-module query entirely and matches the established convention. (The bus route — a `GetTeamNames` query to Organization — remains ADR-0082-legal and is the fallback if a future requirement needs server-side team names; not adopted here.)

## 3. Locked decisions

| # | Decision | Why |
|---|---|---|
| 1 | **Backend assembles** the tree structure + all counts; returns team **IDs** only | Honest counts under RLS in one round trip; no N+1. Team names are an FE join, per §2. |
| 2 | Under each **Team**: its stewarded **Systems** + one **"Ungrouped"** node | Realizes Org→Team→System→Component; the ungrouped node is where optional-`PartOf` components land. |
| 3 | A component **with** a System appears **only** under that System (in the steward team's subtree), even when its owning team differs | ADR-0111 amendment. Each component appears exactly once. Per-team counts intentionally diverge from the Teams page. |
| 4 | A component **without** a System appears under **its owning team's** Ungrouped node | The one place a team-less-of-system component belongs; owning team is its only grouping. |
| 5 | **Leaf kinds = Application + Service only** | `PartOf` is `{App,Service}→System`. APIs are contracts, not system members — excluded. |
| 6 | **Count = descendant leaf-component count**, per node, computed server-side | The acceptance "entity count per level". Org = all components; Team = its subtree's components; System / Ungrouped = their member count. |
| 7 | **Empty teams** (no stewarded system, no ungrouped component) are shown, injected **FE-side** from `useTeamsList`, count 0 | Tree completeness — "navigate the service landscape". Backend can't enumerate all teams (Org-owned); FE already loads the full team list. |
| 8 | Bounded whole-tree read: **node cap + `truncated` flag** (reuse the graph cap), `[BoundedListResult]` + inline justification | ADR-0095. A tree is not a cursor list; it is bounded by org size and capped defensively. |
| 9 | Auth = existing **`catalog.read`**; no new permission | Pure read over data the user can already see via lists/graph. |
| 10 | Expand state in **`sessionStorage`** (keyed to the page), not the URL | Matches the graph-explorer sidebar precedent; survives token-refresh re-auth. URL deep-link is a possible later refinement, not now. |
| 11 | **Org root** = the single tenant's org; name from the existing org-profile context/hook | One tenant = one org (ADR-0001/0012). Single root node. |

### Rejected alternatives
- **Frontend-only composition** (FE calls teams + systems + memberships and builds the tree): rejected — N+1 fan-out, counts assembled in TS (drift risk), harder to cap/paginate a large org honestly.
- **Systems-only tree** (omit ungrouped components): rejected — hides every component without a System; fails "browse … the service landscape".
- **Org-level flat "Unassigned" bucket** (all system-less components in one node): rejected — loses the owning-team grouping the tree exists to show.
- **Component under its own owning team with the System as a child there**: rejected — contradicts the ADR-0111 amendment and duplicates a System across teams.
- **Wolverine `GetTeamNames` cross-module query** for server-side names: not adopted — team-name join is an established FE concern; the bus adds coupling for no user-visible gain.

## 4. Backend (Catalog module) — ~250 lines prod

### 4.1 Contract — `CatalogHierarchyResponse` (`Kartova.Catalog.Contracts`)
Nested, name-free (team names resolved FE-side). All DTOs `[ExcludeFromCodeCoverage]` per the Contracts coverage rule.

```
CatalogHierarchyResponse(
    int TotalComponentCount,          // org root count
    bool Truncated,
    IReadOnlyList<HierarchyTeamDto> Teams)

HierarchyTeamDto(
    Guid TeamId,
    int ComponentCount,
    IReadOnlyList<HierarchySystemDto> Systems,
    HierarchyBucketDto Ungrouped)

HierarchySystemDto(
    Guid SystemId, string DisplayName, int ComponentCount,
    IReadOnlyList<HierarchyMemberDto> Members)

HierarchyBucketDto(int ComponentCount, IReadOnlyList<HierarchyMemberDto> Members)

HierarchyMemberDto(string Kind /* "application" | "service" */, Guid Id, string DisplayName)
```
`Teams` contains only teams that steward ≥1 system **or** own ≥1 ungrouped component (backend can't know empty teams). Members are ordered `displayName asc`; teams/systems ordered `displayName asc` by their FE-resolved / stored name respectively (systems have a stored `DisplayName`; teams sorted FE-side after the name join).

### 4.2 Pure assembler — `HierarchyAssembler.Build(...)` (`Kartova.Catalog.Application`)
Signature over already-loaded rows (mirrors `DerivedDependencies.Compute` — no I/O, fully unit-testable, mutation target):

```
static CatalogHierarchyResponse Build(
    IReadOnlyList<SystemRow> systems,        // id, displayName, stewardTeamId
    IReadOnlyList<ComponentRow> components,  // kind, id, displayName, owningTeamId
    IReadOnlyDictionary<(EntityKind,Guid), CatalogSystemId> partOf, // component → system
    int nodeCap)
```
Logic: place each component under its `PartOf` system if present (system's steward team decides the team bucket), else under its owning team's Ungrouped bucket. Roll counts bottom-up. Enforce `nodeCap` over total component nodes → set `Truncated`, stop adding members past the cap (deterministic order so truncation is stable). No component appears twice (invariant asserted in tests).

### 4.3 Handler — `GetCatalogHierarchyHandler` (`Kartova.Catalog.Infrastructure`)
Runs inside `ITenantScope` (RLS). Three RLS-scoped reads: systems (`id, displayName, teamId`); `PartOf` edges for App+Service; App + Service (`id, displayName, teamId`). Hands rows to `HierarchyAssembler.Build`. No cross-module call.

### 4.4 Endpoint
`GET /api/v1/catalog/hierarchy` → `CatalogHierarchyResponse`. `catalog.read`. Decorated `[BoundedListResult]` with justification: *whole-org tree, bounded by org size, capped at `nodeCap` with `truncated`; not a paged list.* OpenAPI documented; codegen client regenerated (rebuild API image to expose it — see project note on codegen).

## 5. Frontend — ~250 lines prod

- **`useCatalogHierarchy`** (`web/src/features/catalog/api/hierarchy.ts`) — typed fetch of the endpoint via the generated client.
- **`CatalogHierarchyPage`** at `/catalog/hierarchy`:
  - Loads hierarchy + `useTeamsList({ limit: 200 })`; builds `teamNameById`; **injects empty teams** (in the team list, absent from the response) with count 0.
  - Renders a tree (react-aria-components `<Tree>` if it fits the count-badge/link rows cleanly, else nested disclosure rows — decided in the plan against the actual primitive). Each node: expand/collapse chevron, label, **count badge**. Leaf component rows link to the existing detail page (`/catalog/{applications|services}/:id`); System nodes link to `/catalog/systems/:id`.
  - **Breadcrumb trail** reflects the selected node's ancestry (Org / Team / System|Ungrouped / Component).
  - Expand state persisted in `sessionStorage`. `truncated` → a banner ("Showing first N components").
  - Empty/loading/error states surfaced (never blank-page — ADR-0084).
- **Sidebar**: add "Hierarchy" entry under the Catalog section (`Sidebar.tsx`).

## 6. Testing Strategy (per docs/TESTING-STRATEGY.md; gate 3 / gate 4 artifacts)

This slice wires **HTTP + auth + DB** → real-seam integration is mandatory (`KartovaApiFixtureBase`, real Postgres/RLS + real `JwtBearer`/KeyCloak — never the fake auth handler or a mocked DbContext).

Named deliverables (one plan task each):
- **Integration — happy** (`Catalog.IntegrationTests`): org with 2 teams; a System stewarded by team A with a **cross-team member** (a component owned by team B); an ungrouped component under team B; assert tree shape, member placement (cross-team member under the System, not under team B), and every count (org / team / system / ungrouped).
- **Integration — negatives**: 401 unauthenticated; 403 without `catalog.read`; **RLS isolation** (a second tenant's systems/components are absent from tenant 1's tree); **truncation** (seed > `nodeCap` components → `truncated == true`, capped payload).
- **Unit — `HierarchyAssembler.Build`**: placement (with/without `PartOf`), cross-team member, ungrouped-by-owning-team, count roll-up, single-appearance invariant, deterministic truncation at the cap.
- **Frontend** (Vitest/RTL): tree renders from a fixture; expand/collapse; empty-team injection; breadcrumb ancestry; leaf/system links; `truncated` banner. Assert `getAllByRole` for the tree items (jsdom-recovery footgun — real-browser check in gate 9).
- **Architecture**: DTO `[ExcludeFromCodeCoverage]` coverage (`ContractsCoverageRules`) stays green.
- **Container** (gate 4): no Dockerfile/`COPY` change expected; if the codegen step touches the web image, the `images` job covers it.

## 7. DoD

The ten always-blocking gates in `CLAUDE.md` apply as written — not restated here. Slice-specific notes:
- **Gate 9 (visual/API):** UI slice → cold-start dev server, authenticate, navigate in-SPA, screenshot the tree (expanded team → system → cross-team member, count badges, breadcrumb, truncated banner if seeded). New surface — **no existing E2E spec traverses it**, so the E2E-impact trigger is N/A this slice; a `/catalog/hierarchy` nav+expand regression spec is a candidate follow-up, not blocking.
- **Gate 6 (mutation):** target `HierarchyAssembler.Build` (pure, high-value) — run or owner-waive per current DoD policy.
- **List surfaces / filter registry:** N/A — this is a tree, not an ADR-0107 list; no new queryable entity field → no field-addition-trigger row.

## 8. Follow-ups (out of scope)
- URL-deep-linkable expand/selection state (replace `sessionStorage`).
- `/catalog/hierarchy` Playwright E2E regression spec.
- Tag-based browse (E-03.F-04) and search (E-05) are separate features, not this tree.
- Lazy per-team subtree loading if a real org exceeds the node cap in practice.

## 9. Size
~250 backend + ~250 FE ≈ **500 lines prod business code** (excl. tests/DTOs/migrations — none needed; no schema change). Under the ~800 ceiling → single slice.
