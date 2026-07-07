# Slice — Catalog: Async API (unified entity) + stored spec artifacts

**Date:** 2026-07-07
**Stories:** E-02.F-03.S-02 (Register async API + AsyncAPI spec)
**Phase:** 1 — Core Catalog & Notifications
**Branch (proposed):** `feat/catalog-api-spec-storage`
**Governing decisions:** [ADR-0111](../../architecture/decisions/ADR-0111-api-first-class-entity-provider-instance-fields.md) **(amended by this slice)** + **ADR-0112 (new — proposed here)**

---

## 1. Goal

Deliver async API support **and** the spec-document storage the API family needs, by making one insight structural: an **API is a single unified entity keyed by `Style`** (`Rest`/`Grpc`/`GraphQL`/`AsyncApi`), and the **format-specific detail lives inside a stored spec document**, not in DB columns.

Under this model, "async API" is **one new `Style` value** — the shipped `POST/GET/list /apis` already register and read it. The substantive work of the slice is therefore **spec-content storage**: a developer can attach an OpenAPI/AsyncAPI document (by file upload *or* copy-paste) to any API, read it back, and replace it. Async is simply the first consumer; sync APIs get OpenAPI storage for free.

This reverses ADR-0111's original framing that async "adds messaging protocol + channels" as structured columns (§7 ADR impact). Channels/protocol/operations are carried **by the AsyncAPI document**, so no async-specific columns are added.

Concretely, for the MyShop worked example, this slice lets `Orders-Events` (an `AsyncApi`-style API) hold its AsyncAPI YAML document, and lets `Orders-HTTP` hold its OpenAPI JSON — both as `text` in Postgres, RLS-scoped, transactional with their owning API.

---

## 2. Pre-requisites (already on master)

- **Sync `Api` slice shipped** (E-02.F-03.S-01, PR #55): `Api` aggregate (`ApiId`, `ApiStyle {Rest,Grpc,GraphQL}`, `Version`, optional `SpecUrl`, team-owned), `EfApiConfiguration`, `RegisterApiHandler`/`GetApiByIdHandler`/`ListApisHandler`, `POST/GET-by-id/list /api/v1/catalog/apis`, `catalog.apis.register` permission (5-sync), `api.registered` audit, real-seam tests.
- Catalog module conventions: `CatalogEndpointDelegates` (has `RegisterApiAsync`/`GetApiByIdAsync`/`ListApisAsync`), direct-dispatch handlers (ADR-0093), `EnlistInTenantScopeInterceptor`, `ITenantScope` Begin/Commit transport wiring (ADR-0090), `CursorPage<T>` (ADR-0095).
- `KartovaApiFixtureBase` (real Postgres Testcontainer + role/grants + real `JwtBearer`/`TestJwtSigner`).
- `IAuditWriter.AppendAsync` (in-transaction, fail-closed) + `CatalogAuditActions`/`CatalogAuditTargetTypes` (already has `Api = "Api"`).
- RBAC matrix + web permission snapshot guard; team-membership gate as used by `RegisterApi`.

---

## 3. Decisions

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | **Unified API entity** — async is `ApiStyle.AsyncApi`, a new enum value. No separate `AsyncApi` aggregate, no 4th `EntityKind`. | Backstage-shaped (`kind: API`, keyed by `type`). Keeps edges/graph/lookup/list uniform; the future unified view (S-03) is then trivial. |
| 2 | **Format-specific detail lives in the stored spec doc**, not columns. No `protocol`/`channels`/`operations` columns. | Storing the document makes structured columns redundant for registration/read; async channels are needed as *data* only for `publishes-to`/`subscribes-from` graph edges, which are deferred (FU-C, needs Broker/E-02.F-04). |
| 3 | **Spec stored as `text` in Postgres**, in a dedicated `api_spec` table — **not** MinIO. | Size profile 20–50 KB typical, ~1 MB tail: TOAST auto-compresses/out-of-lines >2 KB; inherits RLS; transactional with the API row (no orphan). MinIO earns its keep >~1–10 MB / binary. **ADR-0112.** |
| 4 | **Separate `api_spec` table**, one **current** spec per API (1:1), *not* a column on `apis`. | Keeps keyset list/pagination queries from dragging a 1 MB blob into `SELECT`. Spec loads only on the detail/render path. |
| 5 | **Dedicated sub-resource:** `PUT /apis/{id}/spec` (upsert, raw body) + `GET /apis/{id}/spec` (raw text). | Keeps the shipped register path untouched; separates blob from entity CRUD; natural path to versions (E-21) + rendering (Phase 3). Register payload stays lean. |
| 6 | **Raw request body** on PUT (not a JSON-wrapped string field). File upload and copy-paste both send bytes with a `Content-Type` → one endpoint serves both ingestion UX. | The confirmed file-upload + paste requirement is a UI concern; the backend contract is identical for both. |
| 7 | **`SpecUrl` kept** as optional external provenance ("where it came from"); stored spec = "the bytes we serve". No auto-fetch from `SpecUrl` this slice. | Distinct concepts; zero retrofit churn to the shipped `Api` aggregate. Auto-fetch at import = Phase 2 (E-03). |
| 8 | **Spec is optional** — registration never requires one (mirrors optional `SpecUrl`). | Import may fill it later; parity with how sync shipped. |
| 9 | **Write authz:** reuse `catalog.apis.register` + team-membership gate for `PUT .../spec`. Reads reuse `catalog.read`. | Spec-write = same authority as registering the API; avoids a new permission's 5-sync cost. No new permission added this slice. |
| 10 | **`media_type` allowlist:** `application/json`, `application/yaml`. XML/WSDL deferred with the WSDL style. | The two async/OpenAPI serializations. Unsupported type → `415`. |
| 11 | **Content validation:** required non-empty on PUT; hard cap **5 MiB**. | Bounds abuse well above the ~1 MB tail. Empty/oversized → `400` ProblemDetails. |
| 12 | New audit action **`api.spec.updated`**, target type `Api` (existing), appended in-transaction (fail-closed). | Same pattern as `api.registered`; PUT is a mutation on the API. |
| 13 | `ApiResponse` gains a **`hasSpec: bool`** flag, computed via `EXISTS`/left-join — **never** the blob itself in list/get. | Lets list/detail show "has spec" without transferring content. |
| 14 | **No spec parsing/validation** (is it valid AsyncAPI/OpenAPI?). Stored as opaque text. | Parsing is a rabbit hole; validity is an E-14 policy concern, not a registration invariant. |
| 15 | `ApiStyle.AsyncApi` added; the stale enum comment ("Async styles are a separate entity (E-02.F-03.S-02)") is **corrected** to reflect the unified model. | The comment predicted a separate entity; this slice decides otherwise. |
| 16 | **Backend-only slice.** Frontend spec upload/view UI = follow-up (mirrors sync API UI shipping as FU-9). | Keeps slice ~400 LOC and DoD on the real seam. UI wires file-picker + textarea → the same `PUT` endpoint. |

---

## 4. Architecture

### 4.1 Endpoint topology added by this slice

```
PUT /api/v1/catalog/apis/{id:guid}/spec   (tenant-scoped, NEW; catalog.apis.register + team membership; raw body + Content-Type)
GET /api/v1/catalog/apis/{id:guid}/spec   (tenant-scoped, NEW; catalog.read; returns raw text + stored Content-Type; 404 if absent)
```

`POST /apis`, `GET /apis/{id}`, `GET /apis` are **unchanged** — they already serve `style=AsyncApi`.

### 4.2 PUT-spec happy-path flow

```
Client → JWT auth → tenant-claims transform
  → TenantScopeBeginMiddleware (BEGIN TX, SET LOCAL app.current_tenant_id)
  → endpoint binding (PUT /apis/{id}/spec, raw body via PipeReader/Stream)
  → UpsertApiSpecDelegate
      ├ claim gate: catalog.apis.register                 → 403
      ├ validate Content-Type ∈ {application/json, application/yaml} → 415
      ├ read+validate body: non-empty, ≤ 5 MiB            → 400
      ├ load Api by id (RLS-scoped)                        → 404 if absent/other tenant
      ├ membership gate: OrgAdmin OR member of Api.TeamId  → 403
      └ UpsertApiSpecHandler.Handle(...)
          upsert api_spec row (tenant_id, api_id, content, media_type, created_by, created_at)
          SaveChangesAsync()
          audit.AppendAsync(api.spec.updated)  in-txn, fail-closed
      → 201 Created (+ Location) on first store · 204 No Content on replace
  → TenantScopeCommitEndpointFilter (COMMIT TX)
```

### 4.3 GET-spec flow

```
→ GetApiSpecHandler: SELECT content, media_type FROM api_spec WHERE api_id = @id  (RLS-scoped)
→ null → 404 (ProblemDetails)
→ Results.Text(content, mediaType)   (raw document, not JSON-wrapped)
```

### 4.4 Files created

| File | Purpose |
|------|---------|
| `Kartova.Catalog.Domain/ApiSpec.cs` | Entity (`ApiId ApiId`, `TenantId`, `string Content`, `string MediaType`, `Guid CreatedByUserId`, `DateTimeOffset CreatedAt`); `Create`/`Replace` factory with content/media-type validation. |
| `Kartova.Catalog.Domain/ApiMediaType.cs` | Allowlist constants + `IsAllowed(string)` (`application/json`, `application/yaml`). |
| `Kartova.Catalog.Application/UpsertApiSpecCommand.cs` | `record (Guid ApiId, string Content, string MediaType)`. |
| `Kartova.Catalog.Application/GetApiSpecQuery.cs` | `record (Guid ApiId)`; handler returns a small `(string Content, string MediaType)?` — no Contracts DTO (GET emits raw text). |
| `Kartova.Catalog.Infrastructure/EfApiSpecConfiguration.cs` | `api_spec` table: PK, `api_id` FK→`apis` (cascade delete), `content text`, `media_type`, unique `(api_id)`, RLS. |
| `Kartova.Catalog.Infrastructure/UpsertApiSpecHandler.cs` | Direct-dispatch handler (load Api for membership, upsert spec, audit). |
| `Kartova.Catalog.Infrastructure/GetApiSpecHandler.cs` | Read content + media_type by api_id. |
| `Kartova.Catalog.Infrastructure/Migrations/<ts>_AddApiSpec.cs` | Table + RLS (ENABLE + FORCE + `tenant_isolation` policy) + FK + unique index. |
| `Kartova.Catalog.Tests/ApiSpecTests.cs` | Domain unit tests (validation). |
| `Kartova.Catalog.IntegrationTests/ApiSpecTests.cs` | PUT/GET real-seam tests (happy + negatives + RLS). |

### 4.5 Files modified

| File | Change |
|------|--------|
| `Kartova.Catalog.Domain/ApiStyle.cs` | Add `AsyncApi`; correct the "separate entity" comment. |
| `Kartova.Catalog.Domain/Api.cs` | (No structural change — `Enum.IsDefined` already accepts the new value.) Update class-doc note re: async unified. |
| `Kartova.Catalog.Infrastructure/CatalogDbContext.cs` | `DbSet<ApiSpec>` + apply `EfApiSpecConfiguration`. |
| `Kartova.Catalog.Infrastructure/CatalogModule.cs` | Register `UpsertApiSpecHandler`, `GetApiSpecHandler` (`AddScoped`). |
| `Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs` | `UpsertApiSpecAsync`, `GetApiSpecAsync` + route mapping (raw-body binding). |
| `Kartova.Catalog.Application/CatalogAuditActions.cs` | `ApiSpecUpdated = "api.spec.updated"`. |
| `Kartova.Catalog.Application/ApiResponseExtensions.cs` + `Contracts/ApiResponse.cs` | Add `HasSpec` (computed in Get/List handlers via `EXISTS`). |
| `Kartova.Catalog.Infrastructure/GetApiByIdHandler.cs` / `ListApisHandler.cs` | Populate `HasSpec`. |
| `Kartova.Catalog.IntegrationTests/CatalogPermissionMatrixTests.cs` | +2 rows (PUT→`catalog.apis.register`, GET→`catalog.read`). |

> **No new `KartovaPermission`** → the 5-sync frontend touchpoints are **not** triggered (reusing `catalog.apis.register`).

### 4.6 Data model — `api_spec`

```
api_spec
  id            uuid  PK
  tenant_id     uuid  NOT NULL        -- RLS discriminator
  api_id        uuid  NOT NULL  FK → apis(id) ON DELETE CASCADE
  content       text  NOT NULL        -- opaque spec document (JSON/YAML)
  media_type    text  NOT NULL        -- application/json | application/yaml
  created_by    uuid  NOT NULL
  created_at    timestamptz NOT NULL
  xmin          (system) → uint concurrency token
  UNIQUE (api_id)                      -- one current spec per API (versions = E-21)
  RLS: ENABLE + FORCE + tenant_isolation USING (tenant_id = current_setting('app.current_tenant_id')::uuid)
```

Cascade delete: removing an API removes its spec (no orphan). `api_id` unique enforces 1:1 (upsert semantics).

---

## 5. ADR impact (preview — save on approval)

### 5.1 ADR-0112 (new) — API spec artifacts stored as `text` in Postgres
- **Context:** API family needs to store OpenAPI/AsyncAPI (later WSDL) documents; sizes 20–50 KB typical, ~1 MB tail.
- **Decision:** Store spec documents as `text` in a dedicated RLS-scoped `api_spec` table, transactional with the owning API. Not MinIO/S3.
- **Consequences:** Free tenant isolation + transactional integrity + uniform ops. TOAST handles the 1 MB tail. **Revisit threshold:** migrate cold spec versions to MinIO (ADR-0004) if E-21 version-history makes this many-versions × ~1 MB × many-APIs and table bloat bites.
- **Relation:** narrows ADR-0004 (MinIO default) for this data class; distinct from ADR-0034 (OpenAPI *auto-render*).

### 5.2 ADR-0111 amendment — unified API entity; async is a `Style` value
- Amends ADR-0111 §1 / §Consequences ("async adds messaging protocol + channels").
- **New wording:** API is one unified entity keyed by `Style ∈ {Rest, Grpc, GraphQL, AsyncApi}`. Async's protocol/channels/operations are carried **by the stored AsyncAPI document**, not structured columns. Structured `publishes-to`/`subscribes-from` edges + Broker linkage remain deferred (FU-C, needs E-02.F-04) and, when built, will parse channels from the stored doc or a dedicated edge-authoring path.

---

## 6. Testing strategy (per [docs/TESTING-STRATEGY.md](../../TESTING-STRATEGY.md))

This slice wires **HTTP + auth + DB + middleware** → the **real seam is mandatory** (gate 3/5). Named gate-5 artifacts (each becomes a plan task):

**Unit (`Kartova.Catalog.Tests`):**
- `Api.Create` accepts `ApiStyle.AsyncApi`.
- `ApiSpec` validation: empty content → reject; content > 5 MiB → reject; disallowed media_type → reject; valid → constructs.

**Integration — real Postgres/RLS + real JWT (`KartovaApiFixtureBase`):**
- PUT spec **happy**: first create → 201/204 + row persisted; **replace** existing → content updated, single row (upsert).
- PUT **negatives**: non-member → 403; missing/other-tenant API → 404; oversized body → 400; empty body → 400; unsupported `Content-Type` → 415.
- GET spec **happy** (returns raw text + stored media_type) + **404 when absent**.
- **RLS cross-tenant:** tenant B cannot GET *or* PUT tenant A's API spec (404 via RLS).
- **Permission matrix:** the 2 new endpoints (PUT→`catalog.apis.register`, GET→`catalog.read`).
- `HasSpec` reflected in `GET /apis/{id}` and `GET /apis` after a spec is stored.
- Audit: `api.spec.updated` row written in the same transaction.

**Container (gate 4):** the `AddApiSpec` migration must apply in the `Kartova.Migrator` image — CI `images` job (`docker compose build`) is the artifact. Any failure becomes a regression test.

**Mutation (gate 6 — blocking):** diff touches Domain (`ApiSpec`/`ApiMediaType` validation) + Application logic → run `/misc:mutation-sentinel` → `/misc:test-generator`, target ≥80%, document survivors.

---

## 7. Scope boundary

**In:** `ApiStyle.AsyncApi`; `api_spec` table + domain type; `PUT`/`GET /apis/{id}/spec`; `HasSpec` flag; media-type allowlist + size cap; `api.spec.updated` audit; ADR-0112 + ADR-0111 amendment; real-seam + unit + mutation tests.

**Out (deferred, named):**

| Deferred | Owner |
|----------|-------|
| Spec parsing/validity checks (valid AsyncAPI/OpenAPI?) | E-14 policy / later |
| Spec rendering (Swagger UI / AsyncAPI React) | Phase 3 docs |
| Spec **version history** (many versions per API) | E-21 |
| `publishes-to` / `subscribes-from` edges + Broker linkage | FU-C / E-02.F-04 |
| Auto-fetch spec from `SpecUrl` at import | Phase 2 / E-03 |
| WSDL/XML style + media type | later |
| **Frontend** spec upload (file + paste) / view UI | follow-up (mirrors sync API FU-9) |
| API metadata **edit** (PUT `/apis/{id}`) | still deferred as in S-01 |
| S-03 unified sync/async view per service | E-02.F-03.S-03 (FU-E) |

---

## 8. Definition of Done

Gates per [CLAUDE.md](../../../CLAUDE.md) §Definition of Done (eight always-blocking + gate-6 conditional). Gate-6 **applies** (Domain/Application logic changed). DoD ledger at `docs/superpowers/verification/2026-07-07-catalog-async-api-spec-storage/dod.md` (copy the template at slice start); `gate-findings.yaml` sibling. Completion claims cite the ledger path.

**Estimated production LOC:** ~300–400 (spec table + 2 endpoints + 2 handlers + domain type + enum). One slice, under the 400 target.
