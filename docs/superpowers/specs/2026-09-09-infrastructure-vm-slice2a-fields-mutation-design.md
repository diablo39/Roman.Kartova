# Design — Infrastructure/Virtual Machine (Slice 2a: fields + mutation)

**Story:** E-02.F-04.S-01 — "As a DevOps engineer, I want to register infrastructure components (databases, caches, cloud resources) so that dependencies on them are tracked." Acceptance (`docs/product/phases/phase-1-core-catalog.md:47`): *Infrastructure entity created with **provider**, type, region; linkable to services.*
**Scope note:** slice 2a delivers the **`provider` field**, **VM update/delete**, and **JSONB-column sort**. It closes the `provider` half of the S-01 acceptance; the "linkable to services" half (`DeployedOn` / `PartOf`) is **slice 2b**. Continuation of slice 1 (`2026-09-08-infrastructure-vm-slice1-design.md`, ADR-0115).
**Date:** 2026-09-09 · **Author:** Roman Głogowski (AI-assisted)
**ADRs touched:** none new. Works inside **ADR-0115** (Infrastructure discriminated entity — this slice lifts its *JSONB-column sort* and *`provider`* deferrals), ADR-0095 (cursor/sort/filter), ADR-0107 (filter surface), ADR-0109 (camelCase wire), ADR-0094 (Untitled UI / react-aria), ADR-0090 (`ITenantScope` + RLS), ADR-0085 (migrations via `Kartova.Migrator`), ADR-0082 (module boundaries), ADR-0084 (browser verification), ADR-0097 (five-tier testing). RFC 7232 `If-Match`/`ETag` optimistic concurrency (existing `IfMatchEndpointFilter`).

---

## 1. Goal

Complete the Virtual Machine entity's write + query surface: a `provider` field (S-01 acceptance), full VM update (`PUT`) and hard delete (`DELETE`) with optimistic concurrency, and sortable VM JSONB attributes. Slice 1 shipped read + create only and deliberately deferred all three (ADR-0115 *Deferred*). No relationships/edges — those are slice 2b.

## 2. Starting state (verified 2026-09-09 against slice-1 code, not assumed)

| Fact | Location |
|---|---|
| VM detail GET returns `Results.Ok(resp)` with **no ETag**; `VmDetailResponse` has **no `version`/xmin field** | `CatalogEndpointDelegates.cs:779-787`, `VmDetailResponse.cs` |
| Sibling single-GET emits RFC 7232 ETag from the aggregate version | `EndpointResultExtensions.cs:52` |
| VM list **reuses `InfrastructureSortField`** (`CreatedAt`, `DisplayName`, `Type` only) for both generic and VM tiers; JSONB sort explicitly deferred | `ListVmsQuery.cs`, `InfrastructureSortField.cs`, `InfrastructureSortSpecs.cs` |
| `InfrastructureSortSpecs` covers typed columns only; comment: "JSONB attribute sort is deferred" | `InfrastructureSortSpecs.cs` |
| Update pattern = `MapPut("/{plural}/{id:guid}")` + `IfMatchEndpointFilter` (→ `ExpectedVersionKey` in `http.Items`) + Edit handler setting EF `OriginalValue` → `DbUpdateConcurrencyException` → 409 | `CatalogModule.cs:68-71`, `EditApplicationCommand.cs`, `EditApplicationHandler.cs`, `CatalogEndpointDelegates.cs:317` |
| **No sibling catalog entity supports DELETE** — only `DeleteRelationshipHandler` deletes (edges). No soft-delete anywhere in the module | grep: `MapDelete`/`IsDeleted`/`SoftDelete` = none for entities |
| Reverse/destructive lifecycle ops (decommission) are OrgAdmin-only + `If-Match` | `CatalogModule.cs:139` comment, `DecommissionApplicationHandler.cs` |
| VM aggregate holds opaque `Attributes` (jsonb) + typed shared columns; no `Provider`; no `Edit` method | `InfrastructureResource.cs` |
| `VmAttributes.Validate()` (enum powerState, vcpu/memoryGb>0, per-NIC IP parse, hostname) runs pre-dispatch; throws `ArgumentException` → global `DomainValidationExceptionHandler` 400 | `CatalogEndpointDelegates.cs:789-796`, `VmAttributes.cs` |

## 3. Locked decisions

| # | Decision | Why |
|---|---|---|
| 1 | **Hard delete** — `DELETE /infrastructure/vms/{id}` removes the row (EF `Remove`), gated by a **new `catalog.infrastructure.delete`** permission (**OrgAdmin-only**), `If-Match` required | No sibling delete precedent; VM is observed/imported infra, not governance-tracked (ADR-0115 decision #8 — no lifecycle). New destructive perm mirrors OrgAdmin-only reverse-lifecycle ops rather than folding into `register`. Hard (not soft) delete: no edges exist in 2a, so no dangling references; soft-delete adds a filter predicate + column with no consumer. |
| 2 | `provider` = **shared typed column** `provider text NULL` (free string) | `provider` is intrinsic infra-wide metadata (every infra type has a host), reused by future Broker/Cluster with zero duplication; visible to the generic/All-Objects tier and sortable/filterable as a typed column (no partial-index gymnastics). Free string (not enum) — open-ended (`AWS`/`Azure`/`GCP`/`on-prem`/`VMware`…), not RBAC-relevant. |
| 3 | `region` **stays in JSONB** — documented asymmetry (two acceptance fields, two zones) | Moving `region` to a column would touch slice-1 `VmAttributes`/partial-index/filter/list for no functional gain (YAGNI). Recorded as a known asymmetry, revisitable. |
| 4 | Long-tail VM fields (`diskGb`, `hypervisor`, `tags[]`) **deferred** | YAGNI — no consumer yet; auto-import (slice 4) will dictate the real field set. Keeps 2a under ceiling. |
| 5 | **JSONB-column sort, full set** — `powerState, os, vcpu, memoryGb, hostname, region` — via 6 **partial btree-expression indexes** `WHERE type = 0` | Lifts ADR-0115's JSONB-sort deferral. Cost is the one-time machinery (`VmSortSpecs` + index migration), not the field count; `hostname` sort groups cluster members (explicit user need). |
| 6 | **Separate `VmSortField` enum + `VmSortSpecs`** for the VM tier; generic `InfrastructureSortField`/`InfrastructureSortSpecs` gain **`Provider`** only | ADR-0115 two-tier separation: the generic tier must never learn VM-only sort keys. `provider` is a shared column, so the generic tier *can* sort by it. |
| 7 | Add `string Version` (base64-encoded xmin via `VersionEncoding.Encode`) to `VmDetailResponse` + **emit ETag** (`.WithEtag(resp.Version)`) on VM single-GET | Prerequisite for `If-Match` on PUT/DELETE — slice 1 shipped the GET without it. Mirrors the `GetApplicationByIdAsync` → `WithEtag(resp.Version)` sibling pattern. `IfMatchEndpointFilter` decodes the header via `VersionEncoding.TryDecode`. |
| 8 | Update = full replace (`PUT`) of `displayName`, `description`, `provider`, and the VM attribute set; optimistic concurrency via `If-Match` → `ExpectedVersion` → EF `.Property(x => x.Xmin).OriginalValue` → **412 Precondition Failed** (`ConcurrencyConflictExceptionHandler`) | Exact `EditApplication` pattern (note: Infra's concurrency token property is `Xmin`, not Application's `Version`). VM attribute validation reruns (same `VmAttributes.Validate`). |

### Rejected alternatives
- **Delete via `register` perm** — rejected: mixes create + destroy under one grant; a destructive op deserves its own OrgAdmin-only gate (matches reverse-lifecycle precedent).
- **Soft delete (`IsDeleted`/`DeletedAt`)** — rejected: no consumer, adds a filter predicate to every VM query and a column to a live table for a governance need VM doesn't have (no lifecycle, no audit-of-record requirement beyond the existing audit log). Revisit if 2b edges demand tombstones.
- **`provider` in JSONB (like `region`)** — rejected: VM-only, invisible to the generic tier, duplicated per future type; the cross-type + generic-visibility argument wins despite the one-time column migration.
- **Extend `InfrastructureSortField` with VM attrs** — rejected: leaks VM sort keys into the generic tier, violating the ADR-0115 kind-agnostic boundary.
- **Narrow JSONB sort (categorical only)** — rejected by explicit user need for `hostname` (cluster-member grouping) and numeric capacity sort (`vcpu`/`memoryGb`).

## 4. Slice scope & decomposition

Parent story E-02.F-04.S-01 remaining work exceeds the ~800-line ceiling → decomposed into **2a (this spec)** + **2b (relationships)**. This spec = 2a only; 2b gets its own spec→plan cycle.

**Slice 2a (this spec):** `provider` column + migration · 6 partial JSONB-sort indexes · `InfrastructureResource.Provider` + `Edit(...)` · `EditVm*` (PUT) + `DeleteVm*` (DELETE) with `If-Match`/409 · `catalog.infrastructure.delete` perm (5-sync) · ETag on VM GET + `version` in detail · `VmSortField`/`VmSortSpecs` + generic `Provider` sort · frontend edit page + delete + provider field + sort headers + list-surface record · real-seam tests · CI/helm/compliance touchpoints. **No edges.**

**Deferred → slice 2b:** `DeployedOn` ({App,Service}→Infrastructure), `PartOf` System membership (`PUT .../vms/{id}/system`, `SystemId` write), graph/hierarchy integration, `RelationshipsSection` on VM detail, delete cascade/guard once edges exist. Also deferred: `provider` filter (column present, filter control later), long-tail fields (§3 #4), `region`→column normalization (§3 #3).

## 5. Domain model & persistence (Catalog module)

### 5.1 `InfrastructureResource` changes
```
+ string? Provider                         // shared typed column, nullable free string
+ void Edit(string displayName,            // mutates shared metadata + opaque attributes
            string description,
            string? provider,
            string attributesJson)          // pre-validated VM attributes JSON (app-layer)
```
`Edit` validates shared invariants only (DisplayName non-empty, Description ≤4096) — variant validation stays in the VM app layer (same split as create). Delete has **no** domain method: the handler loads the aggregate (RLS-scoped) and `db.Remove(...)`.

### 5.2 Table `catalog_infrastructure` migration (`Kartova.Migrator`)
- `ADD COLUMN provider text NULL`.
- **6 partial btree-expression indexes**, all `WHERE type = 0` (VM-scoped):
  - `(attributes->>'powerState')`, `(attributes->>'os')`, `(attributes->>'hostname')`, `(attributes->>'region')`
  - `((attributes->>'vcpu')::int)`, `((attributes->>'memoryGb')::int)`
- **Byte-identical rule:** each index expression MUST equal its `VmSortSpecs` ORDER BY expression (cast-aligned). Verified by `EXPLAIN` at gate 9 — a mismatch seq-scans and breaks the ADR-0095 cursor keyset (non-deterministic paging).
- RLS unchanged (existing `tenant_isolation` `FORCE ROW LEVEL SECURITY`); no new table.

## 6. Application layer & API

### 6.1 Endpoints (slice 2a)
| Method | Route | Result | Perm | Concurrency |
|--------|-------|--------|------|-------------|
| GET | `/catalog/infrastructure/vms/{id}` *(amended)* | `VmDetail` + **ETag** | `catalog.read` | emits `.WithEtag(resp.Version)` |
| PUT | `/catalog/infrastructure/vms/{id}` | 200 + ETag | `catalog.infrastructure.register` | `If-Match` req → `ExpectedVersion` → EF `.Property(x=>x.Xmin).OriginalValue` → **412** on stale |
| DELETE | `/catalog/infrastructure/vms/{id}` | 204 | `catalog.infrastructure.delete` (**OrgAdmin**) | `If-Match` req → **412** on stale; **404** on missing/cross-tenant |

`PUT`/`DELETE` carry `.AddEndpointFilter<IfMatchEndpointFilter>()` (mirror `CatalogModule.cs:71`). Missing/malformed `If-Match` → `PreconditionRequiredException` → **428** (filter); stale `If-Match` (version mismatch) → `DbUpdateConcurrencyException` → **412** (`ConcurrencyConflictExceptionHandler`). Concurrency handler needs the current-version capture (mirror `EditApplicationHandler.TryCaptureCurrentVersionAsync`) so the 412 carries `currentVersion`.

### 6.2 CQRS handlers (Wolverine)
- `EditVmHandler` — loads aggregate (RLS), sets EF `OriginalValue = ExpectedVersion`, calls `Edit(...)`, `SaveChanges` → `DbUpdateConcurrencyException` bubbles to 409 mapping (sibling pattern). Team re-authorization mirrors `EditApplication`/`RegisterVm` (edit does not move team in 2a — team immutable, same as siblings; if team change wanted, defer).
- `DeleteVmHandler` — loads aggregate (RLS → cross-tenant = not found → 404), sets `OriginalValue`, `db.Remove`, `SaveChanges`.
- Both write an `IAuditWriter` entry (mirror register/edit audit).

### 6.3 Contracts
- `EditVmRequest` (`DisplayName`, `Description`, `Provider`, `VmAttributesDto`) — `[ExcludeFromCodeCoverage]`.
- `VmDetailResponse` **+ `uint Version`, `string? Provider`**.
- `VmListItemResponse` **+ `string? Provider`**; `InfrastructureListItemResponse` **+ `string? Provider`**.
- `EditVmCommand` / `DeleteVmCommand`.

### 6.4 Validation (`EditVmHandler` / request)
Same as create: `powerState` ∈ enum · `vcpu` > 0 · `memoryGb` > 0 · `hostname` non-empty · each `ipAddresses` entry `IPAddress.TryParse` · `provider` optional (any non-null trimmed string, length cap ≤256). Reject → 400 ProblemDetails (global handler).

### 6.5 Sort (ADR-0095)
- **Generic (`InfrastructureSortField` + `InfrastructureSortSpecs`):** add `Provider` (typed column). Allowlist → `createdAt`, `displayName` (default), `type`, `provider`.
- **VM — new `VmSortField` enum + `VmSortSpecs`:** `DisplayName` (default asc), `CreatedAt`, `Provider`, `PowerState`, `Os`, `Vcpu`, `MemoryGb`, `Hostname`, `Region`. `ListVmsQuery.SortBy` type changes `InfrastructureSortField` → `VmSortField`.
- `VmSortSpecs` JSONB `SortSpec` selectors use EF-translatable expressions **byte-identical** to the §5.2 partial indexes; numeric fields cast `((attributes->>'vcpu')::int)`. Every sort appends the `id` tiebreaker (keyset stability).

### 6.6 Filters
No new filter in 2a. `provider` filter **deferred** (column present). Existing VM filters (powerState/os/region/hostname/ipAddresses via GIN `@>`) unchanged.

### 6.7 List surface — field-addition trigger (`provider`), ADR-0107
| List | Column | Sort | Filter |
|------|--------|------|--------|
| VM (`/vms`) | ✅ | ✅ (typed column) | **defer** |
| All-Objects (`/infrastructure`) | ✅ | ✅ (typed column) | **defer** |

Mirrored into `docs/design/list-filter-registry.md`: `provider` column+sort on both lists, filter deferred; the 6 JSONB fields become **sort-only** additions to the VM list (filter set unchanged from slice 1).

All DTOs `[ExcludeFromCodeCoverage]` (Contracts coverage rule).

## 7. Frontend & nav (Untitled UI / react-aria, ADR-0094)

- **No new route.** Create is already a **dialog** (`RegisterVmDialog.tsx`) launched from the list page, not a page — so edit mirrors it: an **`EditVmDialog`** launched from `VmDetailPage`, reusing the same form (`registerVmSchema` + a `provider` field). Prefill from the detail query; capture the detail's ETag (from the `useVm` response headers / a `version` field) and send it as `If-Match` on `PUT`; on **412** surface a "changed since you loaded it" conflict message and re-fetch.
- **Delete** — button on `VmDetailPage` → confirm dialog → `DELETE` with `If-Match` → redirect to VM list. **Gotcha (CLAUDE.md):** the confirm dialog is a react-aria overlay; the VM list `<Table>` must keep exactly one `isRowHeader` column (displayName) or opening the overlay blank-pages the screen — assert `getAllByRole("rowheader")` and open the dialog in a real browser at gate 9.
- **`provider`** — field on create + edit forms; column on VM list + All-Objects list; row on both detail views.
- **Sort headers** — VM list gains sortable headers for the 6 JSONB fields + provider; generic list gains provider.
- **Generated client:** rebuild API image → regenerate web client + `openapi-snapshot.json` (new PUT/DELETE + widened DTOs; param-order churn cosmetic).

## 8. Permissions — 5-sync (`catalog.infrastructure.delete`)
1. `KartovaPermissions.cs` — `const` + add to `All`.
2. `KartovaRolePermissions.cs` — map to **OrgAdmin only** (destructive; mirror reverse-lifecycle perms).
3. `web/src/shared/auth/permissions.snapshot.json`.
4. `web/src/shared/auth/permissions.ts` — TS const.
5. `web/src/shared/auth/__tests__/usePermissions.test.tsx` — OrgAdmin full-set mock.

`catalog.infrastructure.register` reused for `PUT` (edit). Grep the blast radius — `const` refs under-report.

## 9. Testing strategy (ADR-0097 five-tier · docs/TESTING-STRATEGY.md real-seam)

Named gate-3/gate-4 artifacts (writing-plans emits one task each):
- **Architecture (NetArchTest):** module-boundary + Contracts `[ExcludeFromCodeCoverage]` rules auto-cover new DTOs.
- **Unit:** `VmSortSpecs` expression ↔ index-literal match (each of 6 + provider) · `EditVm` validation (enum, vcpu/memoryGb>0, per-NIC IP, hostname, provider length) · provider round-trip · `VmDetailResponse` version populated.
- **Integration — real seam (mandatory):** `KartovaApiFixtureBase`, real Postgres/RLS + real `JwtBearer`.
  - PUT: 200 (+ETag advances) / 400 (bad attrs) / 403 (missing register perm) / **412 (stale If-Match)** / 428 (missing/malformed If-Match) / 404 (missing) / RLS cross-tenant → 404.
  - DELETE: 204 / 403 (non-OrgAdmin) / 404 (missing / cross-tenant) / **412 (stale If-Match)** / 428 (missing If-Match); row gone on re-GET.
  - GET emits ETag; ETag round-trips into a successful PUT; a stale ETag yields 412 carrying `currentVersion`.
  - Sort: each of 6 JSONB fields + provider returns ordered rows; cursor keyset stable across a page boundary on a JSONB secondary sort; `EXPLAIN` (gate 9) confirms the partial index is used (no seq-scan).
- **Container build (gate 4):** `docker compose build` — migration container carries the new migration (provider column + 6 indexes).
- **E2E:** net-new surface (no existing `e2e/` spec traverses VM edit/delete) → **E2E-impact trigger N/A**. Optional VM edit/delete smoke → nightly, non-blocking.
- **Gate 9:** live PUT/DELETE/GET on the real stack (auth+DB), capture req/resp + ETag/409; `EXPLAIN ANALYZE` per JSONB sort confirming partial-index use; Playwright — VM edit form, delete confirm dialog (overlay does not blank-page), sorted VM list.

## 10. Impact Analysis (LSP)
Grounded in the implementation plan's mandatory `## Impact Analysis (LSP)` section. Existing-symbol changes to enumerate there via `LSP findReferences`/`incomingCalls` (not grep guesses):
- **`ListVmsQuery.SortBy` type change** (`InfrastructureSortField` → `VmSortField`) — every caller (VM list endpoint delegate, `ListVmsHandler`, existing VM list tests, any cursor-encoding of the sort field). Confirm each is retargeted.
- **`VmDetailResponse` +2 fields** — consumers: VM detail endpoint, `GetVmByIdHandler` projection, generated client, detail-page tests.
- **`InfrastructureListItemResponse` / `VmListItemResponse` +`Provider`** — list handlers/projections + list tests.
- **`InfrastructureSortField` +`Provider` member** — `InfrastructureSortSpecs.Resolve` exhaustive switch + allowlist + generic-list tests.
- **New symbols** (`InfrastructureResource.Provider`/`.Edit`, `EditVm*`, `DeleteVm*`, `VmSortField`, `VmSortSpecs`) — no blast radius.
- `const catalog.infrastructure.delete` — **grep** (const refs under-report; documented carve-out).

## 11. Out of scope / deferred (explicit, not silent)
- **Slice 2b:** `DeployedOn` ({App,Service}→Infrastructure) + `PartOf` System membership (`PUT .../vms/{id}/system`, `SystemId` write) + graph/hierarchy/system-view integration + `RelationshipsSection` on VM detail. Closes the "linkable to services" acceptance. Also: delete cascade/guard once edges exist.
- `provider` **filter** control (column + sort ship in 2a; filter later).
- Long-tail VM fields `diskGb`, `hypervisor`, `tags[]`.
- `region` → typed-column normalization (asymmetry accepted, §3 #3).
- VM **team reassignment** on edit (team immutable in 2a, matches siblings).
- **Broker** (E-02.F-04.S-02) — separate slice, reuses this aggregate with `InfrastructureType.Broker`.

## 12. Definition of Done
The ten always-blocking gates in `CLAUDE.md` (build · per-task reviews · full suite incl. real-seam integration · container build · /simplify · requesting-code-review · review-pr · deep-review · visual/API verification · CI green), tracked in the slice DoD ledger at `docs/superpowers/verification/2026-09-09-infrastructure-vm-slice2a/dod.md` (copy the template) + `gate-findings.yaml`. No new ADR (works inside ADR-0115; if a decision here contradicts ADR-0115's *Deferred* framing, this spec supersedes those two deferrals by design — note it when updating ADR-0115's status line at close).
