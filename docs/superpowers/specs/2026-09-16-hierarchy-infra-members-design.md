# Infrastructure in the Catalog Hierarchy read model + System Members table

**Date:** 2026-09-16
**Slice:** TD-005 + TD-006 (bundled) — infrastructure membership surfacing
**Origin:** E-02.F-04.S-01 slice 2b (VM linking) deferred follow-ups; see `docs/engineering/tech-debt.md` TD-005, TD-006.
**Owner:** Roman Głogowski (AI-assisted)

## Problem

Slice 2b made `PartOf: Infrastructure → System` a real writable edge (`POST /relationships`, `PUT /catalog/infrastructure/{id}/system`). Two read surfaces were left inconsistent:

- **TD-005** — `GET /catalog/hierarchy` builds its component set from Applications + Services only, so a VM assigned to a System is silently dropped from the hierarchy tree (visible on System detail via the generic `/relationships` read and on `/graph`, but missing from the hierarchy read model). Silent read-model omission, no crash.
- **TD-006** — a VM in a System's Members table (read path applies no type filter) renders as a plain unlinked `<span>` with no Remove button, because `SystemMembersSection.asComponentKind` returns `null` for `"infrastructure"`. Membership is only removable from the VM's own detail page. `asComponentKind` is a stale copy of the "which kinds can be PartOf" list.

Both were ruled deferred because the design scoped VM System membership to the VM side + `/graph`, leaving infrastructure render-only in the FE relationship model. This slice completes that surfacing.

## Scope decision

De-duplication scope (confirmed with owner 2026-09-16): **one shared FE constant** for the PartOf-source-kind list, pointing `asComponentKind` and `ComponentKind` at it, mirroring the backend's single `RelationshipTypeRules.IsPartOfSourceKind`. Not a full cross-stack sync gate — there is no automated C#↔TS check for this list; the shared FE constant + a source-of-truth comment is the agreed level.

## Current state (verified 2026-09-16)

Already infra-aware (no change needed):
- `EntityKind.Infrastructure` exists (`Kartova.Catalog.Domain/EntityKind.cs`).
- `RelationshipTypeRules.IsPartOfSourceKind` = `Application | Service | Infrastructure` — canonical backend PartOf-source list.
- `GetCatalogHierarchyHandler.partOf` dict filters relationships on `type == PartOf` only, so it already ingests infra PartOf edges; infra rows just never enter `components`.
- FE `ComponentKind` (`api/systems.ts`) already = `"application" | "service" | "infrastructure"`; `useSetComponentSystem` already routes infra to `PUT /catalog/infrastructure/{id}/system`.
- FE `entityDetailPath` / `ENTITY_KIND_LABEL` / `ENTITY_PATH_SEGMENT` / `isEntityKind` all support `"infrastructure"`.
- `/graph` infra node uses the `HardDrive` icon (`EntityGraphNode.tsx`).

Gaps this slice closes:
- `GetCatalogHierarchyHandler` — no `db.Infrastructure` source.
- `HierarchyAssembler.KindWire` — throws for anything but Application/Service.
- `CatalogHierarchyPage.tsx:87` — hardcoded `member.kind === "application" ? "applications" : "services"` path ternary (mis-routes infra to `/services`).
- `hierarchyNodeMeta.ts` / `HierarchyNodeType` / `buildHierarchyView.MemberView.kind` — no `"infrastructure"`.
- `SystemMembersSection.asComponentKind` — returns `null` for infra; link gated on `isRelationshipKind` (excludes infra).

## Design

### 1. Backend — hierarchy read model (TD-005)

`GetCatalogHierarchyHandler.Handle`:
- Materialize `db.Infrastructure` → `ComponentRow(EntityKind.Infrastructure, i.Id.Value, i.DisplayName, i.TeamId)`; concat into `components` alongside apps + services.
- `partOf` dict unchanged (already infra-capable).

`HierarchyAssembler.KindWire`:
- Add `EntityKind.Infrastructure => "infrastructure"`.
- Keep the throw default — it guards a genuinely unexpected kind, not a known-but-unmapped one.

Wire contract: `HierarchyMemberDto.kind` is a free string; adding a value is additive — no DTO shape change, no breaking change. Note in the hierarchy contract comment.

### 2. Frontend — hierarchy page (TD-005 render)

- `hierarchyNodeMeta.ts`: add `"infrastructure"` to `HierarchyNodeType`; add `infrastructure: { label: "Infrastructure", Icon: HardDrive }` (consistent with `/graph` + `ENTITY_KIND_LABEL`).
- `buildHierarchyView.ts`: widen `MemberView.kind` to the shared PartOf-source-kind type (§4); `mapMembers` already passes the wire kind through.
- `CatalogHierarchyPage.tsx`: replace the `:87` path ternary with `entityDetailPath(member.kind, member.id)`.

### 3. Frontend — System Members table (TD-006)

`SystemMembersSection.tsx`:
- `asComponentKind` → `isPartOfSourceKind(kind) ? (kind as ComponentKind) : null` (§4) so Remove works for infra (`useSetComponentSystem` already supports it).
- Member-name link: change the condition from `isRelationshipKind(m.kind)` to `isEntityKind(m.kind)` so infra (and system) members link via `entityDetailPath`. `isRelationshipKind` is unchanged and still used by the creatable-edge callers.

### 4. Shared FE PartOf-source-kind constant (DRY)

In `web/src/features/catalog/relationships/relationshipTypeRules.ts` (same module — avoids a new coupling):
```ts
export const PART_OF_SOURCE_KINDS = ["application", "service", "infrastructure"] as const;
export type PartOfSourceKind = (typeof PART_OF_SOURCE_KINDS)[number];
export function isPartOfSourceKind(k: string): k is PartOfSourceKind;
```
Mirrors backend `RelationshipTypeRules.IsPartOfSourceKind`; carries a comment naming that predicate as the source of truth (no automated C#↔TS sync gate).

Wire-up:
- `ComponentKind` (`api/systems.ts`) → `= PartOfSourceKind` (identical members; keeps them provably in sync).
- `asComponentKind` (`SystemMembersSection.tsx`) → uses `isPartOfSourceKind`.
- `MemberView.kind` (`buildHierarchyView.ts`) → `PartOfSourceKind`.
- Not touched: `isRelationshipKind` (creatable-edge kinds = app/service/api — a deliberately different set).

## Testing strategy

Per `docs/TESTING-STRATEGY.md`. No new HTTP/auth/middleware seam (existing endpoint, additive read) — hierarchy handler already covered by the real-seam fixture.

- **Backend integration** (real Postgres/RLS, `KartovaApiFixtureBase`): a VM PartOf a System appears under that System in `GET /catalog/hierarchy` (happy); a VM with no membership lands in its owning-team Ungrouped bucket (negative/branch).
- **Backend unit**: `HierarchyAssembler` — infra `ComponentRow` routes to system-members when PartOf, to ungrouped otherwise; `KindWire` maps infra.
- **Frontend unit (vitest)**:
  - `isPartOfSourceKind` — accepts the three kinds, rejects `api`/`system`/garbage.
  - `buildHierarchyView` — infra member survives mapping.
  - `CatalogHierarchyPage` — infra member renders infra icon + links to `/catalog/infrastructure/{id}`.
  - `SystemMembersSection` — infra member renders as a link and has a working Remove; app/service rows unchanged.
- **Type gate**: `tsc -b` per FE task (binding gate; vitest/esbuild does not typecheck).

## Definition of Done

The ten always-blocking gates in `CLAUDE.md` apply. Notable per-gate calls:
- **Gate 4 (container build)**: expected **N/A-with-reason** — diff touches no Dockerfile / `COPY` / restore surface. Confirm against the final diff.
- **Gate 9 (visual/API)**: cold-start web, register a VM + assign it to a System (DevSeed has no VMs — 120 apps only, per project setup), screenshot the Hierarchy page (VM under its System) and the System detail Members table (VM linked + removable). Evidence under `docs/superpowers/verification/2026-09-16-hierarchy-infra-members/`.
- **E2E-impact trigger**: check `e2e/` for any spec traversing the Hierarchy page or System detail Members table; if traversed, update + run its spec locally (gate 9 note in the DoD ledger). Resolve during planning.
- DoD ledger + `gate-findings.yaml` at `docs/superpowers/verification/2026-09-16-hierarchy-infra-members/`.

## Impact Analysis (LSP)

To be completed in the plan's `## Impact Analysis (LSP)` section. Existing C# symbols whose behavior changes:
- `HierarchyAssembler.Build` / `KindWire` (private local) — additive branch; callers = `GetCatalogHierarchyHandler` only. Confirm blast radius with `findReferences` on `Build`.
- `GetCatalogHierarchyHandler.Handle` — additive query; single Wolverine endpoint consumer. Confirm with `findReferences`.
No shared-const or interface signature change on the backend. FE changes are not C# (grep/read).

## Acceptance

- A VM assigned to a System appears under that System in `GET /catalog/hierarchy` and on the Hierarchy page, covered by tests; existing hierarchy behavior unchanged. (TD-005)
- A VM member in a System's Members table renders as a link and has a working Remove; covered by a test; the PartOf-eligible kind list has one FE source of truth aligned to the backend predicate. (TD-006)

## Out of scope

- TD-007 (VM entity-search text filter), TD-008 (vitest flake) — separate items.
- Any automated C#↔TS sync gate for the PartOf-source-kind list.
- `/graph` and VM-detail membership UIs (already shipped in slice 2b).
