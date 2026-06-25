# Slice 1b — Catalog: Relationships UI surface (+ either-endpoint authority)

**Date:** 2026-06-25
**Stories:** E-04.F-01.S-01 (create manual relationship) + E-04.F-01.S-02 (list an entity's relationships) — **frontend portion**; closes the "consumers deferred → E-04" thread on the Service/Application detail pages.
**Phase:** 1 — Core Catalog & Notifications
**Branch:** `feat/catalog-relationships-ui-surface`
**Follows:** Slice 1a (`2026-06-24-catalog-relationships-design.md`, backend, PR #42 — on master).

---

## 1. Goal

Give manual relationships a usable web surface and, in the same slice, **widen edge-write authority to either endpoint** (ADR-0108). On both the Application and Service detail pages a developer can see an entity's **Dependencies** (outgoing) and **Dependents** (incoming), **add** a relationship from *either* side (declare "we depend on X" *or* "X depends on us"), and **delete** an edge — all bounded by team membership and the `catalog.relationships.write` permission, with the backend authoritative.

Slice 1a shipped the three endpoints with **source-side** write authority. This slice changes that to **either-endpoint** authority so a provider team can record incoming dependencies rather than waiting on a possibly-lazy consumer (full rationale: ADR-0108). That makes this slice **backend + frontend**, not frontend-only.

The novel surface vs. the Application/Service UI slices: a **directed, two-grouping relationship section** rendered identically on two entity types via one shared component, plus a **server-search typeahead** entity picker and a **client-side mirror** of the directionality matrix.

---

## 2. Pre-requisites (already on master)

- **Relationship backend (Slice 1a, PR #42):** `POST /api/v1/catalog/relationships`, `GET /api/v1/catalog/relationships?entityKind=&entityId=&direction=` (`CursorPage<RelationshipResponse>`, default sort `createdAt desc`, allowlist `{createdAt, type}`), `DELETE /api/v1/catalog/relationships/{id}`. Creatable types `DependsOn`, `PartOf`; entity kinds `Application`, `Service`. `RelationshipResponse = { id, source:{kind,id,displayName}, target:{kind,id,displayName}, type, origin, createdByUserId, createdAt }`. Real-seam integration tests exist (`CreateRelationshipTests`, `DeleteRelationshipTests`, `ListRelationshipsTests`, `CatalogPermissionMatrixTests`).
- **Authorization (1a):** create/delete gate on the **source** team via `AuthorizeTargetTeamAsync` in `CatalogEndpointDelegates`; both delegates already inject `ClaimsPrincipal caller` + `IAuthorizationService auth`. `CreateRelationshipAsync` already resolves the target (`lookup.Find(target)`) for existence + display, *after* the source gate.
- **Permission parity:** `KartovaPermissions.CatalogRelationshipsWrite = "catalog.relationships.write"` exists in C#, `web/src/shared/auth/permissions.ts`, and `permissions.snapshot.json` (commit `74bb1bf`) — **no permission work in this slice**.
- **Frontend list/detail stack:** `useCursorList` (`@/lib/list/useCursorList`, internal cursor stack — usable with no URL state); `Table` (`@/components/application/table/table`); `TableSkeleton`, `TablePager` (`@/components/application/data-table/data-table`); `Badge` (`@/components/base/badges/badges`, colors incl. `gray`, `brand`); `CreatedByLink` + `UserDisplayInfo` (`@/features/users/components/CreatedByLink`); `usePermissions` (`@/shared/auth/usePermissions` → `{ hasPermission, role, teamIds, … }`); `toast` (`sonner`); `apiClient`/`unwrapData`/`throwWithStatus`; modal primitives (`@/components/application/modals/modal`); base `Select` (`@/components/base/select/select`).
- **Typeahead precedent:** `UserSearchCombobox` (`@/features/users/components/UserSearchCombobox`) — hand-rolled server-search combobox (250 ms debounce, min-2-char gate, WAI-ARIA listbox, outside-click close). The entity picker mirrors it.
- **Detail pages to extend:** `ApplicationDetailPage.tsx` and `ServiceDetailPage.tsx` — both `<Card><CardHeader/><CardContent className="space-y-6">…sections…</CardContent></Card>`, `id` from `useParams`, loading-skeleton + not-found cards. Both expose the entity's `teamId` in their loaded data.
- **List endpoints support `displayNameContains`:** both `GET /catalog/applications` and `GET /catalog/services` (used by the typeahead).

---

## 3. Decisions

| # | Decision | Rationale |
|---|---|---|
| 1 | **Either-endpoint write authority** (ADR-0108): create/delete require `OrgAdmin` OR membership of *either* connected entity's team; symmetric. Replaces 1a source-side authority. | A provider team must be able to record incoming dependencies without waiting on the consumer. Member-of-neither stays 403, so no fabrication between unrelated teams. (User-confirmed; see ADR-0108.) |
| 2 | **No approval/confirmation workflow.** Edges live immediately; accountability via `origin=manual` + `created_by_user_id` + audit. | Proportionate for manually declared edges; the verification handshake stays deferred to ADR-0056 / scan-agent era. (User-confirmed.) |
| 3 | **One shared `<RelationshipsSection>`** rendered on both detail pages; **no new routes**. | The surface is identical for both entity kinds; a single tested component avoids divergence. |
| 4 | **Two groupings:** *Dependencies* (`direction=outgoing`) and *Dependents* (`direction=incoming`), **two independent `useCursorList` queries** + two pagers. | Each group paginates on its own; "Dependents" is the long-deferred consumers view. Partitioning one `direction=all` page would break per-group pagination. |
| 5 | **Both groups are writable.** Dependencies' Add fixes **source = this entity**; Dependents' Add fixes **target = this entity**. One generalized `AddRelationshipDialog({fixedRole, fixedEntity})`. | Either-endpoint authority (Decision 1) means a developer declares outgoing *and* incoming edges from the entity they own. |
| 6 | **Entity picker = server-search typeahead** (`EntitySearchCombobox`), mirroring `UserSearchCombobox`; queries the other endpoint's list by `displayNameContains`. | User-chosen over a bounded `<select>`; scales past a few hundred entities. Excludes the fixed entity itself (no self-ref). |
| 7 | **Client-side mirror of the directionality matrix** (`relationshipTypeRules.ts`), advisory; backend authoritative. | Only offers valid `(type, other-kind)` combos for the fixed endpoint (e.g. `PartOf` unofferable when the fixed entity is a target Service). Avoids submit→400 round-trips; backend still rejects bad pairs. |
| 8 | **UI manage-gate** = `hasPermission(CatalogRelationshipsWrite) && (role === OrgAdmin || teamIds.includes(thisEntity.teamId))`. | You manage edges from the page of an entity you own. The far endpoint's `teamId` isn't in `RelationshipResponse`, so the client can't gate on it — backend 403 covers cross-page cases. |
| 9 | **Codegen re-run** and regenerated `openapi.ts` + `openapi-snapshot.json` committed. | Relationship types/paths are not yet in the generated client (verified absent); the web image compiles TS, so missing types break gate-4. |
| 10 | **Delete is a confirm-then-DELETE** per row (lightweight inline confirm, no full modal). | Destructive + irreversible (no edit; re-create only). Mirrors the catalog delete-affordance pattern. |

---

## 4. Architecture

### 4.1 Backend authorization change (ADR-0108)

`src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs`:

- **New private helper** `AuthorizeEitherTeamAsync(IAuthorizationService auth, ClaimsPrincipal caller, Guid teamA, Guid teamB) → IResult?` — returns `null` (authorized) if the existing per-team policy passes for `teamA` **or** `teamB` (OrgAdmin passes any team check); otherwise returns the forbidden result. Composes `AuthorizeTargetTeamAsync` twice.
- **`CreateRelationshipAsync`** — resolve `targetInfo` (`lookup.Find(target)`) *before* the authorization gate (move the existing lookup up; its 422-on-null stays), then gate on `AuthorizeEitherTeamAsync(auth, caller, sourceInfo.TeamId, targetInfo.TeamId)`. Order becomes: source lookup → 422; target lookup → 422; either-team gate → 403; duplicate pre-check → 409; handle (domain invariants → 400) + audit.
- **`DeleteRelationshipAsync`** — additionally resolve `targetInfo` and gate on `AuthorizeEitherTeamAsync(auth, caller, sourceInfo?.TeamId ?? Guid.Empty, targetInfo?.TeamId ?? Guid.Empty)`. Deleted-endpoint nuance preserved: a missing team → `Guid.Empty` (Member never passes, OrgAdmin still does); both missing → OrgAdmin-only.

No change to permission constant, role map, audit actions, the type matrix, contracts, or EF config.

### 4.2 Endpoints consumed (unchanged)

```
POST   /api/v1/catalog/relationships
GET    /api/v1/catalog/relationships?entityKind=&entityId=&direction=outgoing|incoming|all
DELETE /api/v1/catalog/relationships/{id}
```

### 4.3 Frontend routes & navigation

**None added.** The section is embedded in the two existing detail pages.

### 4.4 Data flow

```
ApplicationDetailPage / ServiceDetailPage  (id from useParams, entity loaded)
  └ <RelationshipsSection entityKind entityId entityTeamId entityDisplayName>
       canManage = hasPermission(CatalogRelationshipsWrite)
                   && (role === OrgAdmin || teamIds.includes(entityTeamId))
       ├ Dependencies  ← useRelationshipsList({entityKind, entityId, direction:"outgoing"})
       │     row: typeBadge · target link · originBadge · CreatedByLink · created · [delete*]
       │     [Add dependency*] → <AddRelationshipDialog fixedRole="source" fixedEntity={this}>
       ├ Dependents    ← useRelationshipsList({entityKind, entityId, direction:"incoming"})
       │     row: typeBadge · source link · originBadge · CreatedByLink · created · [delete*]
       │     [Add dependent*] → <AddRelationshipDialog fixedRole="target" fixedEntity={this}>
       └ * gated on canManage

AddRelationshipDialog({fixedRole, fixedEntity})
  ├ types     = offerableTypes(fixedRole, fixedEntity.kind)          // matrix mirror
  ├ Type      <Select>  → on change: otherKinds = allowedOtherKinds(type, fixedRole, fixedEntity.kind)
  ├ OtherKind <Select>  (auto-selected + disabled when single, e.g. PartOf)
  ├ Other     <EntitySearchCombobox kind={otherKind} excludeId={fixedEntity.id when same kind}>
  └ submit → assemble {sourceKind,sourceId,targetKind,targetId,type} by role
            → useCreateRelationship → success: toast + invalidate relationshipKeys.all + close
            → error: toast (409 duplicate / 422 invalid entity / 400 bad-pair·self-ref / 403)

EntitySearchCombobox({kind, excludeId, onSelect})   // mirrors UserSearchCombobox
  └ debounced q (250 ms, min 2) → useEntitySearch(kind, q) → list endpoint w/ displayNameContains
       results filtered to drop excludeId → listbox → onSelect({kind,id,displayName})
```

### 4.5 File map

**Modified — backend:**

| File | Change |
|---|---|
| `Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs` | Add `AuthorizeEitherTeamAsync`; reorder `CreateRelationshipAsync` target lookup ahead of gate + either-team gate; `DeleteRelationshipAsync` resolve target + either-team gate. |
| `Kartova.Catalog.IntegrationTests/CreateRelationshipTests.cs` | Rewrite "non-member of source → 403" to "non-member of **either** → 403"; add **target-team member → 201**. |
| `Kartova.Catalog.IntegrationTests/DeleteRelationshipTests.cs` | Add **target-team member → 204**; **neither-team member → 403**; deleted-source + target-member → 204. |

**Created — frontend (`web/src/features/catalog/`):**

| File | Purpose | ~LOC |
|---|---|---|
| `api/relationships.ts` | `relationshipKeys`; `useRelationshipsList` (`useCursorList`); `useCreateRelationship`; `useDeleteRelationship` (id as mutation arg); `useEntitySearch(kind,q,{enabled})` (switches app/service list, `displayNameContains`, `limit:10`, maps to `{kind,id,displayName}`). Types from generated client. | 95 |
| `relationships/relationshipTypeRules.ts` | `relationshipTypeLabel`; `isCreatable`; `isAllowedPair`; `offerableTypes(fixedRole, fixedKind)`; `allowedOtherKinds(type, fixedRole, fixedKind)`. Mirror of backend `RelationshipTypeRules`. | 45 |
| `components/EntitySearchCombobox.tsx` | Kind-parameterized server-search typeahead (mirror `UserSearchCombobox`); excludes `excludeId`. | 120 |
| `components/AddRelationshipDialog.tsx` | Generalized fixed-endpoint dialog (type/kind `Select` + combobox); submit→mutate→toast/invalidate. | 150 |
| `components/RelationshipsSection.tsx` | Two sub-tables (Dependencies/Dependents), Add gating, per-row delete-with-confirm, loading/empty/error states, `entityLink(kind,id)` helper. | 160 |

**Created — frontend tests (gate-5 artifacts):**

| File | Purpose |
|---|---|
| `api/__tests__/relationships.test.tsx` | list (outgoing + incoming) fetch; create invalidates `relationshipKeys.all`; delete invalidates + error; `useEntitySearch` hits the right endpoint per kind. |
| `relationships/__tests__/relationshipTypeRules.test.ts` | Exhaustive matrix: `offerableTypes`/`allowedOtherKinds` for every `(fixedRole, fixedKind)`; `isAllowedPair` truth table. |
| `components/__tests__/EntitySearchCombobox.test.tsx` | Debounced search fires after 2 chars; selecting fires `onSelect`; `excludeId` filtered out; loading/no-match/error states. |
| `components/__tests__/AddRelationshipDialog.test.tsx` | `fixedRole=source` vs `target` constrain offered types/kinds; `PartOf` forces + disables Application kind; payload assembled correctly per role; 409/422 → toast. |
| `components/__tests__/RelationshipsSection.test.tsx` | Both groups render with correct "other" endpoint link; Add buttons hidden without `canManage`; delete only when `canManage`; empty/error states; delete confirm→mutate. |

**Modified — frontend:**

| File | Change |
|---|---|
| `pages/ApplicationDetailPage.tsx` | Insert `<RelationshipsSection entityKind="Application" …>` after the metadata grid. |
| `pages/ServiceDetailPage.tsx` | Insert `<RelationshipsSection entityKind="Service" …>` after the endpoints section. |
| `generated/openapi.ts` + `web/openapi-snapshot.json` | Regenerated (`npm run codegen`) — generated, excluded from LOC. |

**Estimate ≈ 570 LOC frontend production + ≈ 30 LOC backend** (excl. tests, generated client). Comfortably under the ~800 ceiling.

---

## 5. Components

### 5.1 `api/relationships.ts`

```ts
type RelationshipResponse = components["schemas"]["RelationshipResponse"];
type ListQuery = NonNullable<operations["ListRelationships"]["parameters"]["query"]>;
type RelationshipsListParams = {
  entityKind: NonNullable<ListQuery["entityKind"]>;   // "Application" | "Service"
  entityId:   NonNullable<ListQuery["entityId"]>;
  direction:  NonNullable<ListQuery["direction"]>;     // "outgoing" | "incoming" | "all"
  limit?: number;
};
export const relationshipKeys = {
  all: ["relationships"] as const,
  list: (p?: RelationshipsListParams) => p ? [...all, "list", p] as const : [...all, "list"] as const,
};
export function useRelationshipsList(p: RelationshipsListParams) { /* useCursorList → GET /relationships */ }
export function useCreateRelationship() { /* useMutation POST; onSuccess invalidate relationshipKeys.all */ }
export function useDeleteRelationship() { /* useMutation((id) => DELETE /relationships/{id}); invalidate all */ }
export function useEntitySearch(kind, q, opts) { /* useQuery enabled:q.length>=2 → GET apps|services?displayNameContains=q&limit=10 → {kind,id,displayName}[] */ }
```

`useDeleteRelationship` takes the id as the **mutation argument** (not a hook param) so one hook instance serves every row.

### 5.2 `relationships/relationshipTypeRules.ts` (mirror of backend matrix)

Creatable subset only — `DependsOn`, `PartOf`. Matrix: `DependsOn` allows any `{App,Service} → {App,Service}`; `PartOf` allows `Service → Application` only.

```ts
export const relationshipTypeLabel = { DependsOn: "Depends on", PartOf: "Part of" } as const;

// Types creatable with `fixedKind` occupying the `fixedRole` slot.
offerableTypes("source","Application") // → ["DependsOn"]          (App can't be a PartOf source)
offerableTypes("source","Service")     // → ["DependsOn","PartOf"]
offerableTypes("target","Application") // → ["DependsOn","PartOf"]  (App is the only PartOf target)
offerableTypes("target","Service")     // → ["DependsOn"]           (Service can't be a PartOf target)

// Valid kinds for the *other* endpoint given the chosen type.
allowedOtherKinds("DependsOn", *, *)        // → ["Application","Service"]
allowedOtherKinds("PartOf","source","Service")     // → ["Application"]
allowedOtherKinds("PartOf","target","Application") // → ["Service"]
```

### 5.3 `EntitySearchCombobox.tsx`

Mirror of `UserSearchCombobox`: local `q` → 250 ms debounce → `useEntitySearch(kind, debouncedQ, {enabled: debouncedQ.length>=2})`; WAI-ARIA listbox with `aria-activedescendant`, arrow/Enter/Escape, outside-click close. Props `{ kind, excludeId?, onSelect, placeholder? }`; results drop `excludeId` (the fixed entity, preventing self-reference).

### 5.4 `AddRelationshipDialog.tsx`

Props `{ open, onOpenChange, fixedRole: "source"|"target", fixedEntity: {kind,id,displayName} }`. State `selectedType`, `selectedOtherKind`, `selectedOther`. `offerableTypes(fixedRole, fixedEntity.kind)` populates the type `Select`; changing type recomputes `allowedOtherKinds` (auto-select + disable when single). Submit assembles the payload by role — `fixedRole==="source"` → source = fixedEntity, target = selectedOther (and vice-versa) — then `useCreateRelationship`. Reset on close. No RHF/free-text fields, so all errors surface as toasts (no field mapping): 409 duplicate, 422 invalid entity, 400 bad-pair/self-ref, 403.

### 5.5 `RelationshipsSection.tsx`

Two sub-tables sharing one render helper; the "related" endpoint is `target` for the Dependencies group and `source` for the Dependents group. `entityLink(kind,id) = /catalog/${kind==="Application"?"applications":"services"}/${id}`. Origin renders as a `Badge color="gray"` ("Manual"); type as a `Badge color="brand"` (`relationshipTypeLabel`). `canManage` gates both Add buttons and all per-row delete buttons. Per group: `TableSkeleton` while loading, empty copy ("No dependencies" / "Nothing depends on this {kind}"), inline error text on fetch error, `TablePager` for prev/next.

---

## 6. Error handling (frontend surfacing)

| Backend response | UI behavior |
|---|---|
| 409 relationship-already-exists | toast "This relationship already exists." |
| 422 invalid-source/target-entity | toast "That entity no longer exists." |
| 400 bad pair / self-ref | toast with the ProblemDetails `detail` (matrix mirror should prevent reaching this). |
| 403 (lacks permission / member of neither team) | toast; Add/Delete affordances already hidden when `!canManage`. |
| list fetch error | inline error text in the affected group (the other group still renders). |

No new `ProblemDetails` types — 1a's mapping is reused.

---

## 7. List surface (ADR-0095 / ADR-0107)

This slice **consumes** the relationship list surface 1a already registered (`docs/design/list-filter-registry.md`): columns type / related entity / origin / created-by / created; default sort `createdAt desc`; **no `<FilterBar>` facets** (all deferred in 1a — single/too-few values today); `direction` is a core query param, not a facet. **No new queryable field is added**, so the field-addition trigger does not fire and there is **no registry change**. The embedded section is not a top-level list screen, so `useListUrlState` is intentionally not wired (Decision 4).

---

## 8. Testing strategy (gate-5 artifacts)

Per [docs/TESTING-STRATEGY.md](../../TESTING-STRATEGY.md).

**Backend real seam (required — authority changed).** The either-endpoint gate is new HTTP/auth logic, so real-seam coverage re-enters scope via `KartovaApiFixtureBase` (real `JwtBearer` + real Postgres/RLS):
- `CreateRelationshipTests`: **target-team member → 201** (new capability); **member of neither team → 403** (rewrites the old source-only 403 case); existing source-team-member-201 and OrgAdmin-201 retained.
- `DeleteRelationshipTests`: **target-team member → 204**; **neither-team member → 403**; **deleted-source + target-team member → 204** (deleted-endpoint fallback).
- `CatalogPermissionMatrixTests`: unchanged (claims unchanged).

**Frontend (Vitest)** — ≥1 happy + ≥1 negative per unit; the five test files in §4.5.

**Gate-4 container build:** no Dockerfile/`COPY` change, but the web image compiles TS, so the regenerated `openapi.ts` **must be committed**. Verify `npm run build` green locally.

**Gate-6 mutation:** **applies** — the diff changes Application/authorization logic (`CreateRelationshipAsync`, `DeleteRelationshipAsync`, `AuthorizeEitherTeamAsync`). Run `/misc:mutation-sentinel` → `/misc:test-generator` over the changed Catalog delegate; document survivors. (Frontend TS is out of Stryker.NET's scope.)

**Manual verification (ADR-0084):** Playwright MCP cold-start dev server → on an Application detail page add an outgoing dependency (typeahead) and on a Service detail page add a dependent → verify both groups render, delete one → console clean. Codegen + Playwright flagged *pending user verification* if the dev stack is unavailable in-session.

---

## 9. Definition of Done

The eight always-blocking gates as defined in **CLAUDE.md → Working agreements → Definition of Done** apply verbatim; this slice does not restate them. **Gate 6 (mutation) is blocking here** (Decision: authority logic changed — §8). Run `scripts/ci-local.sh` (Release mirror) green before push.

---

## 10. Out of scope (explicit deferrals)

- Approval/confirmation handshake for asserted edges → ADR-0056 / scan-agent era (Decision 2).
- Embedded React Flow mini-graph (E-04.F-02.S-01); standalone `/graph` explorer + filters + impact analysis (E-04.F-02.S-03–06).
- Pin/unpin = promote/demote origin (E-04.F-01.S-03/04) — needs `scan`/`agent` origin (E-07/E-08/E-15).
- The other 5 relationship types and entity kinds (queues/brokers/infra/environments/API entities) → E-02.F-03/F-04/F-05.
- Sort/filter the relationship list by related-entity name (cross-table keyset); search indexing (E-05).
- Editing an edge (immutable; delete + recreate); GET single relationship by id.

---

## 11. Implementation order (rough — finalised by writing-plans)

1. **Backend authority (ADR-0108):** `AuthorizeEitherTeamAsync` + reorder/gate in `CreateRelationshipAsync` and `DeleteRelationshipAsync`; update `CreateRelationshipTests` + `DeleteRelationshipTests` (RED→GREEN). Mutation loop on the changed delegate.
2. **Codegen:** run the API → `npm run codegen` → commit `openapi.ts` + `openapi-snapshot.json`; confirm `RelationshipResponse` / `ListRelationships` present.
3. `relationships/relationshipTypeRules.ts` + tests.
4. `api/relationships.ts` + hook tests.
5. `components/EntitySearchCombobox.tsx` + tests.
6. `components/AddRelationshipDialog.tsx` + tests.
7. `components/RelationshipsSection.tsx` + tests.
8. Wire `ApplicationDetailPage.tsx` + `ServiceDetailPage.tsx`.
9. `npm run build` + `scripts/ci-local.sh` green; Playwright MCP manual pass; update `docs/product/CHECKLIST.md` (mark E-04.F-01.S-01/S-02); push → PR → DoD gates.

---

## 12. Self-review

**Spec coverage:** every §3 decision traces to §4–§8; every gate-5 artifact in §8 is a named file in §4.5 that writing-plans will turn into a task.

**Placeholder scan:** no TBD/TODO. §5 code is illustrative; final code lands in executing-plans.

**Internal consistency:**
- Either-endpoint authority consistent across §1, §3 #1, §4.1, §8, ADR-0108.
- `RelationshipResponse` shape (no far-endpoint `teamId`) consistent with the §3 #8 / §4.1 gate rationale (client gates on `thisEntity.teamId`; backend gates on both).
- Matrix (`DependsOn` any-pair; `PartOf` Service→Application) consistent across §5.2, the dialog constraints (§4.4/§5.4), and backend `RelationshipTypeRules`.
- "backend + frontend, mutation gate applies" consistent across §1, §3, §8, §9 (departs from the frontend-only Service-UI precedent — called out explicitly).

**Scope check:** single PR; 5 created + 2 modified frontend production files, 1 modified backend file, 2 modified backend test files, 5 frontend test files; ≈ 600 LOC production, under the 800 ceiling.

**Ambiguity check:**
- "Both sides creatable" resolved to a fixed-endpoint dialog with role-driven payload assembly (§4.4, §5.4); the matrix mirror pins which `(type, other-kind)` combos are offered per side.
- Delete authority resolved symmetric with create (ADR-0108); deleted-endpoint fallback pinned (§4.1).

**No blocking issues found.**
