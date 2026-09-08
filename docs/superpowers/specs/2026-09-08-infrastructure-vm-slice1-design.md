# Design — Infrastructure Entity + Virtual Machine (Slice 1: read + create)

**Story:** E-02.F-04.S-01 — "As a DevOps engineer, I want to register infrastructure components (databases, caches, cloud resources) so that dependencies on them are tracked." Acceptance (`docs/product/phases/phase-1-core-catalog.md:47`): *Infrastructure entity created with provider, type, region; linkable to services.*
**Scope note:** this slice delivers the **Infrastructure entity foundation with Virtual Machine as the first `InfrastructureType`**, read + create. It delivers `type` (discriminator) + `region`; **`provider` and service-linkage (`DeployedOn`) are deferred** (§11) — so slice 1 **partially** satisfies S-01. Full S-01 closure spans slices 1–2 (+ a `provider` field decision).
**Date:** 2026-09-08 · **Author:** Roman Głogowski (AI-assisted)
**ADRs touched:** **one new ADR, amending ADR-0111** — Infrastructure as a discriminated `EntityKind` (VM first), flat indexed-JSONB variant storage, generic + type-specific endpoint split. Works inside ADR-0095 (cursor/sort/filter), ADR-0107 (filter surface), ADR-0109 (camelCase wire), ADR-0094 (Untitled UI / react-aria), ADR-0090 (`ITenantScope` + RLS), ADR-0085 (migrations via `Kartova.Migrator`), ADR-0082 (module boundaries), ADR-0084 (browser verification), ADR-0097 (five-tier testing).

---

## 1. Goal

A dedicated **Infrastructure** catalog entity, discriminated by `InfrastructureType` (VM first), positioned under the Infrastructure nav group. Two read surfaces — a type-specific **Virtual Machines** list with VM columns, and an **All Objects** list showing all infrastructure with shared columns — plus VM detail and VM create. Two endpoint tiers: a **generic** resource-level endpoint (kind-agnostic) and a **specific** VM endpoint (typed, rehydrates variant attributes from JSONB). No relationships in this slice.

## 2. Starting state (verified 2026-09-08 via code-map agent, not assumed)

| Fact | Location |
|---|---|
| `EntityKind` = flat closed enum `{Application, Service, Api, System}`; consumed by `EntityRef.Kind` + `RelationshipTypeRules` | `Kartova.Catalog.Domain/EntityKind.cs:3` |
| One aggregate + one table + one permission family + own list/get handlers **per kind** — no shared base entity | Application/Service/Api/CatalogSystem, parallel structure |
| Only discriminated model in repo = `Api` keyed by `ApiStyle` (`smallint`, `HasConversion<short>`, append-only), variant payload in 1:1 side table | `Api.cs:12,20`, `ApiStyle.cs:6`, `EfApiConfiguration.cs:36-40`, `ApiSpec.cs:10` |
| Edges are kind-agnostic: `EntityRef{ EntityKind Kind; Guid Id }` points at any kind; new kind joins edges by touching only `EntityKind` + `RelationshipTypeRules` | `EntityRef.cs:3`, `Relationship.cs:5` |
| `RelationshipType.DeployedOn` **already defined**, not yet creatable — reserved for hosting edges | `RelationshipType.cs:9`, `RelationshipTypeRules.cs:5` |
| Per-entity permission families (`catalog.<plural>.register`); `catalog.read` is the single shared read gate; new perm = const + `All` FrozenSet | `KartovaPermissions.cs:7-16,33-42` |
| Nav is hand-authored; Infrastructure `NavCollapsibleGroup` exists with **disabled** `Components` / `Brokers` stubs (no routes/entities behind them) | `web/src/components/layout/Sidebar.tsx:165-172` |
| **No** existing infrastructure/VM/host/resource domain type in C#; frontend has only the disabled nav stubs | (grep: no matches) |
| **No Elasticsearch in the solution** — catalog lists are per-kind EF handlers over Postgres with per-kind sort specs + cursor paging | `ListApplicationsHandler`, `ApplicationSortSpecs` (Catalog.Infrastructure) |
| Product taxonomy names Infrastructure + Broker as intended kinds; feature = E-02.F-04 | `ADR-0111:11`, `EPICS-AND-STORIES.md:207,340` |

**Industry precedent (research 2026-09-08):** Backstage models all infra as one `Resource` kind discriminated by a free-form `spec.type` (`virtual-machine`, `database`, `kubernetes-cluster`); Atlassian Compass uses one `Component` with a `type` enum (VM → `CLOUD_RESOURCE`). Neither ships a bespoke per-device entity type. This slice follows the same discriminated pattern, with a bounded enum (Compass/`ApiStyle`-style) chosen over free strings for RBAC + typed Postgres filters.

## 3. Locked decisions

| # | Decision | Why |
|---|---|---|
| 1 | New `EntityKind.Infrastructure` (single kind), keyed by `InfrastructureType` discriminator (`VirtualMachine` first) | Matches product taxonomy + Backstage `Resource`/Compass `CLOUD_RESOURCE` + in-repo `Api`/`ApiStyle`. Avoids one bespoke `EntityKind` per device type. |
| 2 | `InfrastructureType` enum, `smallint` `HasConversion<short>`, **append-only** | Exact `ApiStyle` treatment; persisted discriminator stays stable. |
| 3 | Aggregate holds variant attributes as **opaque JSONB**, kind-agnostic; VM typing lives at the **application layer** (`VmAttributes` ↔ JSONB) | Mirrors `Api` holding opaque spec. Adding Broker later = new app-layer attrs, zero domain/table change. |
| 4 | **Flat** JSONB shape `{ powerState, os, … }` — NOT type-namespaced `{ vm: {…} }` | One `Type` per row (scalar discriminator) already namespaces; envelope would always be a single-key echo and deepens every index/sort/filter expression. No multi-facet resources (confirmed). |
| 5 | Filterable/sortable VM fields stay **queryable** (typed-column-equivalent via indexed JSONB expressions), not buried in opaque JSON | ADR-0095 cursor keyset needs stable, indexed sort keys. `Api` stores spec as un-queried `text`; VM differs — its list filters/sorts. |
| 6 | **Two endpoint tiers:** generic `/catalog/infrastructure` (resource-level, shared columns, never deserializes JSONB) + specific `/catalog/infrastructure/vms` (typed, rehydrates JSONB) | Generic stays kind-agnostic forever; specific owns VM shape + typed write validation. |
| 7 | Shared typed columns: `id, type, display_name, description, team_id, system_id (nullable), created_by_user_id, created_at, xmin` | Anchor generic endpoint + RLS + cursor keyset. `system_id` nullable/unwritten now (forward-compat, dodges a later migration on a populated table). |
| 8 | **No lifecycle** — operational `powerState` is a plain VM attribute, no state machine, no lifecycle perms | VMs are observed/imported infra, not governance-tracked like Application. Consistent with Service/Api/System (register perm only). |
| 9 | **No relationships in slice 1** — standalone VM entity | Keeps slice 1 under the ~800-line ceiling. Membership (`PartOf`) + `DeployedOn` + hierarchy = slice 2. |
| 10 | Auth: reuse `catalog.read`; new write perm `catalog.infrastructure.register` | Read over data the user can already see; write mirrors `catalog.services.register`. |
| 11 | Nav: Infrastructure group → **Virtual Machines** (live) + **All Objects** (live, last, after divider) = all infrastructure; keep **Brokers** disabled stub | User-requested layout; Brokers stub = cheap roadmap signal. |

### Rejected alternatives
- **Dedicated `EntityKind.VirtualMachine`** (per-device top-level kind): rejected — contradicts product taxonomy (ADR-0111 says *Infrastructure*), sets a precedent where Broker/Cluster/Container each need a full parallel stack (enum+aggregate+perm+handlers+nav), diverges from both incumbents.
- **Type-namespaced JSONB** `{ vm:{…}, loadbalancer:{…} }`: rejected — solves cross-type key collision that can't occur (one scalar `Type` per row; specific endpoints are type-scoped via partial index), deepens every expression for no benefit. Would only pay off under a multi-facet model (a row = several types at once), which breaks the scalar discriminator and is out of scope.
- **Free-string `spec.type`** (Backstage-style): rejected — bounded enum serves RBAC + typed Postgres filters; drift-hardening matches `ApiStyle`.
- **Single overloaded endpoint** carrying a nullable VM block for both views: rejected — generic view would carry per-type knowledge it must never gain; two tiers keep generic kind-agnostic.
- **All variant attrs in opaque JSON with list rehydration for sort/filter**: rejected — JSONB sort key without an expression index breaks cursor keyset (seq-scan, non-deterministic order).

## 4. Slice scope & decomposition

Full feature exceeds the ~800-line slice ceiling → decomposed. **This spec = slice 1 only.** Later slices get their own spec→plan cycle.

**Slice 1 (this spec):** `EntityKind.Infrastructure` + `InfrastructureType` enum · `catalog_infrastructure` table (typed columns + flat JSONB `attributes`) + GIN + partial btree-expr indexes + migration · generic list endpoint · specific VM list/detail/create endpoints · `catalog.infrastructure.register` permission (5-sync) · nav (Virtual Machines + All Objects) · VM list + All-Infrastructure list + VM detail (read) + VM create form · DevSeed VMs · CI/helm/compliance touchpoints. **No relationships.**

**Deferred (explicit — §11).**

## 5. Domain model & persistence (Catalog module)

### 5.1 Aggregate `InfrastructureResource` (`Kartova.Catalog.Domain`)
One class (`Api`/`CatalogSystem` pattern), `: ITenantOwned, ITeamScopedResource`:

```
InfrastructureResource
  InfrastructureId Id            // strongly-typed (like ApiId)
  InfrastructureType Type        // discriminator; VirtualMachine first
  string DisplayName
  string? Description
  Guid TeamId
  Guid? SystemId                 // nullable, unwritten in slice 1
  Guid CreatedByUserId
  DateTimeOffset CreatedAt
  string Attributes              // opaque JSONB payload (jsonb column), kind-agnostic
  uint  xmin                     // concurrency token
```
Domain validates only shared invariants (DisplayName non-empty, `Enum.IsDefined(type)`). Variant validation lives in the VM application layer.

### 5.2 `InfrastructureType` enum
`{ VirtualMachine = 0 }` — append-only, mapped `smallint` via `HasConversion<short>()` (exact `ApiStyle` treatment).

### 5.3 Table `catalog_infrastructure` (EF config + migration via `Kartova.Migrator`)
- Typed columns per 5.1; `attributes jsonb`.
- RLS tenant policy + team-scoped, registered via `AddModuleDbContext` (ADR-0090) — never raw `AddDbContext`.
- **Indexes:**
  - GIN `jsonb_path_ops` on `attributes` (containment filters).
  - Partial btree-expression, `WHERE type = 0` (VM-scoped): `(attributes->>'powerState')`, `(attributes->>'os')`, `(attributes->>'hostname')`, `(attributes->>'region')`, `((attributes->>'vcpu')::int)`, `((attributes->>'memoryGb')::int)`.
- **Cursor keyset (ADR-0095):** default sort `display_name asc` (typed column) + `id` tiebreaker. Secondary JSONB sorts append `id`; the EF sort-spec SQL expression MUST be byte-identical to the partial-index expression (cast-aligned) or it seq-scans — verified by `EXPLAIN` at gate 9.

## 6. Application layer & API

### 6.1 Endpoints (slice 1)
| Method | Route | Result | Perm |
|--------|-------|--------|------|
| GET | `/catalog/infrastructure` | `CursorPage<InfrastructureListItem>` (shared columns) | `catalog.read` |
| GET | `/catalog/infrastructure/vms` | `CursorPage<VmListItem>` (rehydrated) | `catalog.read` |
| GET | `/catalog/infrastructure/vms/{id}` | `VmDetail` | `catalog.read` |
| POST | `/catalog/infrastructure/vms` | 201 + id (typed validation → serialize) | `catalog.infrastructure.register` |

### 6.2 CQRS handlers (Wolverine)
`ListInfrastructureHandler` · `ListVmsHandler` · `GetVmByIdHandler` · `CreateVmHandler`. Per-kind read handlers (matches `ListApplicationsHandler`; no shared index).

### 6.3 `VmAttributes` record (app layer ↔ JSONB)
`PowerState (enum: running|stopped|suspended) · Os (string) · Vcpu (int) · MemoryGb (int) · Hostname (string) · IpAddresses (string[]) · Region (string)`.
Serialized **camelCase** (ADR-0109) via a single shared `JsonSerializerOptions` — JSONB keys MUST equal the index/sort expression literals (`powerState`, `vcpu`, …). Create serializes; list/get rehydrate.

### 6.4 Create validation (`CreateVmHandler` / request)
`powerState` ∈ enum · `vcpu` > 0 · `memoryGb` > 0 · `hostname` non-empty · each `ipAddresses` entry `IPAddress.TryParse` · `teamId` authorized. Reject → 400 ProblemDetails.

### 6.5 Sort allowlists (ADR-0095)
- Generic: `displayName` (default asc), `type`, `createdAt`.
- VM: `displayName` (default asc), `powerState`, `os`, `vcpu`, `memoryGb`, `hostname`, `region`, `createdAt`. `VmSortSpecs` maps JSONB keys → SQL expression identical to the partial index (cast-aligned).

### 6.6 Filters (ADR-0107 `f` map) — GIN containment (`jsonb_path_ops`)
| Filter | View | SQL |
|--------|------|-----|
| type | generic | `type = @t` (column) |
| powerState | VM | `attributes @> '{"powerState":…}'` |
| os | VM | `attributes @> '{"os":…}'` |
| region | VM | `attributes @> '{"region":…}'` |
| hostname | VM | `attributes @> '{"hostname":…}'` (exact; prefix/contains search deferred) |
| ipAddresses | VM | `attributes @> '{"ipAddresses":[…]}'` (contains) |

Mirrored into `docs/design/list-filter-registry.md` — one row per list (generic + VM). VM list has no own name-bearing default beyond `displayName` (present) → default `displayName asc`.

### 6.7 VM list surface (ADR-0107 confirmed)
| Field | Column | Sort | Filter |
|-------|--------|------|--------|
| displayName | ✅ `isRowHeader` | ✅ default asc | text search |
| powerState | ✅ badge | ✅ | ✅ select |
| os | ✅ | ✅ | ✅ select/text |
| vcpu | ✅ | ✅ | defer |
| memoryGb | ✅ | ✅ | defer |
| hostname | ✅ | ✅ | ✅ exact |
| ipAddresses (string[]) | ✅ first + "+N" | ❌ | ✅ contains |
| region | ✅ | ✅ | ✅ select |
| team | ✅ | ❌ | defer |

**All Objects (generic):** displayName (`isRowHeader`) · type badge · team · system · createdAt; type filter select. Shared columns only — mixed types, no per-type columns.

All DTOs `[ExcludeFromCodeCoverage]` (Contracts coverage rule).

## 7. Frontend & nav (Untitled UI / react-aria, ADR-0094)

- **Nav (`Sidebar.tsx`):** Infrastructure group → `Virtual Machines` (`/catalog/infrastructure/vms`, live) · divider · `All Objects` (`/catalog/infrastructure`, live, last). Replace disabled `Components` stub; keep `Brokers` disabled.
- **Routes:** `/catalog/infrastructure` · `/catalog/infrastructure/vms` · `/catalog/infrastructure/vms/new` · `/catalog/infrastructure/vms/:id`.
- **Screens:**
  - `VmListPage` — `useCursorList` + `useListUrlState` + `<DataTable>` + `<FilterBar>`/`useListFilters`. Columns/sort/filters per §6.7. "Create VM" → `/new`.
  - `AllInfrastructureListPage` — generic endpoint, shared columns + Type badge + type filter.
  - `VmDetailPage` — full field render incl. all `ipAddresses`.
  - `VmCreatePage` — typed form; `ipAddresses` multi-entry; POST.
- **Gotchas:** `<Table>` needs exactly one `isRowHeader` col (displayName) or overlays blank-page (CLAUDE.md). Add Infrastructure/VM Type-badge variant (amber, per Stitch mockups).
- **Generated client:** rebuild API image → regenerate web client + `openapi-snapshot.json` to expose new endpoints (param-order churn is cosmetic).

## 8. Permissions — 5-sync (`catalog.infrastructure.register`)
1. `KartovaPermissions.cs` — const + add to `All` FrozenSet.
2. `KartovaRolePermissions.cs` — map to roles (mirror `catalog.services.register`).
3. `web/src/shared/auth/permissions.snapshot.json`.
4. `web/src/shared/auth/permissions.ts` — TS const.
5. `web/src/shared/auth/__tests__/usePermissions.test.tsx` — OrgAdmin full-set mock.

`catalog.read` reused (no new read perm). Grep blast radius — const references under-report.

## 9. Testing strategy (ADR-0097 five-tier · docs/TESTING-STRATEGY.md real-seam)

Named gate-3/gate-4 artifacts (writing-plans emits one task each):
- **Architecture (NetArchTest):** module-boundary + Contracts `[ExcludeFromCodeCoverage]` rules (auto-cover new DTOs).
- **Unit:** `VmAttributes` serialize↔rehydrate (camelCase keys match index literals) · create validation (enum, vcpu/memoryGb>0, `IPAddress.TryParse` per NIC, hostname) · sort/filter spec SQL-expression mapping.
- **Integration — real seam (mandatory):** `KartovaApiFixtureBase`, real Postgres/RLS + real `JwtBearer`. ≥1 happy + ≥1 negative per endpoint: POST 201 / 400 / 403; generic list returns VM row (shared cols) + RLS tenant isolation; VM list rehydrates attributes + sort by `powerState` + filter `powerState` + `ipAddresses` contains (GIN); cursor keyset stability (default + JSONB secondary sort).
- **Container build (gate 4):** `docker compose build` — migration container carries the new migration.
- **E2E:** net-new surface; no existing `e2e/` spec traverses infra → E2E-impact trigger **N/A**. Optional VM smoke spec → nightly, non-blocking.
- **Gate 9:** live POST+GET on real stack (auth+DB), capture req/resp + `EXPLAIN` confirming JSONB sort hits the partial expression index; Playwright screenshots of both lists + detail + create.

## 10. Impact Analysis (LSP)
Deferred to the implementation plan (`writing-plans` emits the mandatory `## Impact Analysis (LSP)` section). Existing-symbol change to scope there: **`EntityKind` enum gains a member** (`EntityKind.cs:3`) — its consumers (`EntityRef`, `RelationshipTypeRules`, EF `EntityRef` complex-property mapping storing kind as string, any `switch`/badge map over kinds) must be enumerated via `LSP findReferences`/`incomingCalls` on the enum and each caller confirmed handled (exhaustive switches, string round-trip for the new kind). `RelationshipType.DeployedOn` is untouched this slice. New code (aggregate, handlers, endpoints, VmAttributes) = new-symbol, no blast radius.

## 11. Out of scope / deferred (explicit, not silent)
- **Slice 1b** (if write pushes slice 1 over ~800 lines): VM update/delete + edit form.
- **Slice 2:** relationships — `PartOf` System membership (`SystemId` write via `PUT .../system`) + `DeployedOn` (App|Service)→Infrastructure + hierarchy/system-view integration. Closes the S-01 "linkable to services" acceptance.
- **`provider` field** (S-01 acceptance) + deferred VM fields: `diskGb`, `hypervisor`, `hostname` already in; `provider`, `tags[]` → later slice; expanded filters (hostname prefix/contains, vcpu/memory range).
- **Slice 4:** auto-import (vSphere / cloud asset inventory) → E-02.F-04 import path.
- **Broker** entity (E-02.F-04.S-02): separate slice, reuses this Infrastructure aggregate with `InfrastructureType.Broker`.

## 12. Definition of Done
The ten always-blocking gates in `CLAUDE.md` (build · per-task reviews · full suite incl. real-seam integration · container build · /simplify · requesting-code-review · review-pr · deep-review · visual/API verification · CI green), tracked in the slice DoD ledger at `docs/superpowers/verification/2026-09-08-infrastructure-vm-slice1/dod.md` (copy the template) + `gate-findings.yaml`. New ADR previewed to the human before saving.
