# Slice — Catalog: API spec UI (attach/view) + configurable size cap

**Date:** 2026-07-07
**Stories:** E-02.F-03.S-02 — frontend follow-up (spec upload/view UI) + configurable spec size cap
**Phase:** 1 — Core Catalog & Notifications
**Branch (proposed):** `feat/catalog-api-spec-ui`
**Governing decisions:** [ADR-0112](../../architecture/decisions/ADR-0112-api-spec-artifacts-stored-in-postgres.md) (spec stored as text; **amended here** — 5 MiB becomes the default of a configurable cap), [ADR-0111](../../architecture/decisions/ADR-0111-api-first-class-entity-provider-instance-fields.md) (unified API entity), [ADR-0094](../../architecture/decisions/ADR-0094-untitled-ui-component-library.md) (Untitled UI), [ADR-0084](../../architecture/decisions/ADR-0084-playwright-mcp-for-frontend-development.md) (browser verify), [ADR-0107](../../architecture/decisions/ADR-0107-list-filtering-consideration-and-filterbar-ui.md) (list surface).
**Predecessor spec:** [2026-07-07-catalog-async-api-spec-storage-design.md](2026-07-07-catalog-async-api-spec-storage-design.md) (backend, shipped).

---

## 1. Goal

Give the API spec-storage capability shipped in S-02 a **user surface**, and make the size cap **operator-configurable**.

S-02 shipped `PUT`/`GET /apis/{id}/spec` + `HasSpec` with **no frontend** — a developer can register an async (or any-style) API but cannot attach or read its spec through the UI. This slice closes that loop:

1. **Attach / replace** a spec document (file-picker **or** paste) from the API detail page.
2. **View** the stored spec (raw, read-only) with the media-type badge + copy.
3. **See "has spec"** at a glance in the Apis list (new indicator column).
4. **Configurable cap** — the 5 MiB limit moves from a hardcoded domain `const` to `appsettings` (`Catalog:ApiSpec:MaxContentBytes`), single source of truth, validated into a safe band.

The affordance appears on **every** API style (Rest/gRPC/GraphQL/AsyncApi) — the backend already accepts a spec for any style, and hiding it for sync styles would bury a live capability (REST→OpenAPI, Async→AsyncAPI, GraphQL→SDL/introspection JSON).

Nothing about spec **rendering**, **validation**, or **version history** is in scope (see §7).

---

## 2. Pre-requisites (already on master)

- **Backend spec storage (S-02):** `ApiSpec` domain type, `api_spec` RLS table, `PUT /apis/{id}/spec` (raw body, `application/json`|`application/yaml`, `catalog.apis.register` + team membership, 201/204), `GET /apis/{id}/spec` (raw text, `catalog.read`, 404 if absent), `ApiResponse.HasSpec` on get/list. Enforcement in `CatalogEndpointDelegates.UpsertApiSpecAsync` (415 → declared-length pre-check → auth → `ReadCappedAsync` streaming cap → handler).
- **Frontend API surface (FU-9):** `ApiDetailPage`, `ApisTable`, `ApisListPage`, `RegisterApiDialog`, `apis.ts` data layer (`useApi`, `useApisList`, `useRegisterApi`), `API_STYLE_LABEL`.
- **Raw-upload precedent:** `organization.ts` logo upload uses raw `fetch(${API_BASE_URL}/...)` + explicit `Content-Type` + Bearer, bypassing `apiClient` because openapi-fetch hard-codes `application/json` and JSON-parses responses. Same shape as the spec endpoints.
- **Perm plumbing:** `usePermissions` + `CatalogApisRegister` const (`web/src/shared/auth/permissions.ts:11`).
- **ProblemDetails→form/toast:** `applyProblemDetailsToForm`, `throwWithStatus`.

---

## 3. Design decisions

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | **Frontend + backend slice.** Folds the configurable-cap backend change into the UI slice. | User-requested single source of truth; keeping cap hardcoded while adding a client mirror would create drift. |
| 2 | **Affordance on all API styles.** | Backend accepts a spec for any style; unified-entity model (ADR-0111). |
| 3 | **View inline, mutate via dialog.** Detail page shows raw spec in a read-only `<pre>`; attach/replace is a modal (`AttachApiSpecDialog`) mirroring `RegisterApiDialog`. | Matches the codebase convention (mutations → dialogs, reads → inline). |
| 4 | **File-picker AND paste**, one raw-`fetch` PUT. Media type inferred (file ext `.json`/`.yaml`/`.yml`, else content sniff `{`→json / else yaml), user-overridable via a JSON/YAML toggle. | Backend contract is identical for both ingestion paths (raw bytes + `Content-Type`); one endpoint serves both. |
| 5 | **Raw `fetch`, not `apiClient`,** for GET+PUT spec. | openapi-fetch JSON-parses responses (chokes on YAML) and hard-codes `application/json` request bodies. Mirror `organization.ts`. |
| 6 | **GET 404 = "no spec yet"**, an empty state — not an error. `useApiSpec` returns `null`, `enabled: hasSpec`. | 404 is the documented absent-spec signal; avoids a spurious error UI + a wasted fetch when the detail already says none. |
| 7 | **No client-side size mirror.** Client checks only non-empty; the cap is enforced by the backend, whose 400 message names the configured number. | The drift trap: a hardcoded client value silently diverges from a configurable server cap. Server is authoritative; the value is **not** exposed to the SPA. |
| 8 | **No client-side membership gate.** Show the attach/replace button to holders of `catalog.apis.register`; let the backend 403 surface inline. | The SPA does not know team membership; duplicating it client-side would be wrong or stale. Permission is the honest client-side gate. |
| 9 | **Keep Spec URL and stored spec as two distinct things** on the detail page. | `specUrl` = external provenance ("where it came from"); stored spec = "the bytes we serve" (S-02 §3.7). Different concepts, different UI rows. |
| 10 | **Apis list gets a `Spec` indicator column** (badge/check when `hasSpec`, muted "—" otherwise). **Not** sortable, **no** filter this slice. | ADR-0107 field-addition trigger for a new user-facing field: column = **yes**, sort = **no** (presence isn't a meaningful sort key), filter = **deferred**. Recorded in `list-filter-registry.md`. |
| 11 | **Cap → `IOptions<CatalogSpecOptions>`** bound from `Catalog:ApiSpec`, default `5 * 1024 * 1024`. Enforcement (declared-length, `ReadCappedAsync`, 400 message) reads the configured value at the endpoint. | Single source of truth in config; endpoint is the layer that streams bytes and already owns the cap. Default preserves ADR-0112 behavior. |
| 12 | **Domain `ApiSpec` drops the byte-cap check + `MaxContentBytes` const;** keeps non-empty + media-type invariants. | Size is a transport/operational bound enforced at the streaming seam — not a domain invariant. A domain `const` duplicating config is the drift being removed. |
| 13 | **`IValidateOptions` band: 1 KiB … 50 MiB.** Absurd values fail fast at startup. | `ReadCappedAsync` buffers up to the cap; an operator typo (10 GB) is an OOM vector. Bounds "configurable" without becoming a foot-gun. |
| 14 | **ADR-0112 amended** (not new ADR): "hard cap 5 MiB" → "default 5 MiB of a configurable, validated cap." | Decision class unchanged (store as text); only the cap's provenance changes. |

---

## 4. Architecture

### 4.1 Backend — configurable cap

**Files created**

| File | Purpose |
|------|---------|
| `Kartova.Catalog.Infrastructure/CatalogSpecOptions.cs` | `sealed class CatalogSpecOptions { public int MaxContentBytes { get; set; } = 5 * 1024 * 1024; }` + section name const `"Catalog:ApiSpec"`. `[ExcludeFromCodeCoverage]` (options POCO). |
| `Kartova.Catalog.Infrastructure/CatalogSpecOptionsValidator.cs` | `IValidateOptions<CatalogSpecOptions>` — fail unless `1024 <= MaxContentBytes <= 50 * 1024 * 1024`. |

**Files modified**

| File | Change |
|------|--------|
| `Kartova.Catalog.Domain/ApiSpec.cs` | Remove `const MaxContentBytes` + the byte-count check in `Validate`. `Validate` keeps non-empty + `ApiMediaType.IsAllowed`. Update the class-doc size note. |
| `Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs` | `UpsertApiSpecAsync` gains `IOptions<CatalogSpecOptions> specOptions` param; use `specOptions.Value.MaxContentBytes` at the declared-length pre-check (`:672`), `ReadCappedAsync` (`:679`), and `SpecTooLarge(int limit)` message (`:930`). `SpecTooLarge` takes the limit as a param. |
| `Kartova.Catalog.Infrastructure/CatalogModule.cs` | `services.AddOptions<CatalogSpecOptions>().Bind(config.GetSection("Catalog:ApiSpec")).ValidateOnStart();` + register the validator. |
| `src/Kartova.Api/appsettings.json` | Add `"Catalog": { "ApiSpec": { "MaxContentBytes": 5242880 } }` with a comment noting the 1 KiB…50 MiB band. |
| `src/Kartova.SharedKernel.AspNetCore/ProblemTypes.cs:85` | Update the `SpecTooLarge` comment (no longer references `ApiSpec.MaxContentBytes`). |
| `Kartova.Catalog.Tests/ApiSpecTests.cs` | Domain tests referencing `ApiSpec.MaxContentBytes` (lines ~31/40/41/82/84): drop the byte-cap assertions from the domain layer (the cap no longer lives there); keep non-empty + media-type + UTF-8 byte-count-independent cases. |
| `Kartova.Catalog.IntegrationTests/ApiSpecTests.cs:245` | Rework the oversized case to not reference the removed const; add the configured-boundary case (§6). |

> Backend still adds **no new `KartovaPermission`** (reuses `catalog.apis.register`) → 5-sync **not** triggered.

### 4.2 Backend — endpoint enforcement flow (unchanged shape, configured limit)

```
PUT /apis/{id}/spec
  415 if Content-Type ∉ {application/json, application/yaml}
  400 if declared Content-Length > cfg.MaxContentBytes            ← configured
  load Api (RLS) → 404; membership gate → 403
  ReadCappedAsync(body, cfg.MaxContentBytes) → null ⇒ 400          ← configured
  handler.Handle → 201 (first) | 204 (replace); audit api.spec.updated
```

### 4.3 Frontend — data layer (`web/src/features/catalog/api/apis.ts`)

| Symbol | Shape |
|--------|-------|
| `apiKeys.spec(id)` | new query key |
| `useApiSpec(id, hasSpec)` | GET via raw `fetch(${API_BASE_URL}/api/v1/catalog/apis/${id}/spec)` + Bearer; `enabled: hasSpec`; 200 → `{ content, mediaType }` (mediaType from response `Content-Type`); 404 → `null`; other → throw. |
| `useUpsertApiSpec(id)` | raw `fetch` PUT; body = raw string; `Content-Type` = chosen media type; Bearer. Success → `qc.invalidateQueries({ queryKey: apiKeys.detail(id) })` + `apiKeys.all` (refresh `hasSpec` + list column) + `apiKeys.spec(id)`. Errors → ProblemDetails throw. |

Token read via `react-oidc-context` `useAuth()` (as `organization.ts`).

### 4.4 Frontend — components

| File | Purpose |
|------|---------|
| `components/AttachApiSpecDialog.tsx` (new) | Modal mirroring `RegisterApiDialog` shell. `<input type="file" accept=".json,.yaml,.yml">` + paste `<TextArea>` + JSON/YAML segmented toggle (inferred default, overridable). Client guard: non-empty only. Submit → `useUpsertApiSpec`. 415/400/403 ProblemDetails → inline message + toast; success toast + close. Title "Attach spec"/"Replace spec" by `hasSpec`. |
| `components/ApiSpecSection.tsx` (new) | Rendered on `ApiDetailPage`. `hasSpec` → media-type `Badge` + "Replace" button + read-only `<pre className="max-h-[480px] overflow-auto">{content}</pre>` + copy-to-clipboard. Else → empty state + "Attach spec" button. Button visible only with `CatalogApisRegister` perm. Opens `AttachApiSpecDialog`. |

**Files modified**

| File | Change |
|------|--------|
| `pages/ApiDetailPage.tsx` | Keep the Spec URL field. Insert `<ApiSpecSection api={api} />` between the metadata grid and `RelationshipsSection`. |
| `components/ApisTable.tsx` | New `Table.Head id="hasSpec"` (plain, not `SortableHead`) "Spec"; cell = green-check `Badge` when `api.hasSpec` else muted "—". Loading skeleton `cells` 6 → 7. |
| `docs/design/list-filter-registry.md` | Apis row: record hasSpec — column ✓, sort ✗, filter deferred. |

### 4.5 Frontend types

`ApiResponse` already carries `hasSpec` in the generated client (snapshot line ~3107). No codegen change needed unless the snapshot is stale — regenerate if `hasSpec` is absent from `@/generated/openapi` (per the codegen-churn note).

---

## 5. ADR impact (preview — save on approval)

**ADR-0112 amendment (edit existing ADR):**
- **Decision line:** "stored as `text` … hard cap 5 MiB" → "… with a **configurable** cap `Catalog:ApiSpec:MaxContentBytes` (**default 5 MiB**, validated to 1 KiB…50 MiB)."
- **Consequences:** add — cap now operator-tunable per deployment; enforced at the streaming endpoint (not a domain invariant); validation band prevents an unbounded-buffer OOM vector.
- No change to the store-as-text vs MinIO decision, the revisit threshold, or the ADR-0004/ADR-0034 relations.

No new ADR. No change to ADR-0111.

---

## 6. Testing Strategy (per [TESTING-STRATEGY.md](../../TESTING-STRATEGY.md))

This slice wires an HTTP config seam (backend) + UI (frontend). Gate-5 artifacts, named as deliverables:

**Backend — real-seam integration (`Kartova.Catalog.IntegrationTests/ApiSpecTests.cs`, `KartovaApiFixtureBase`, real Postgres/RLS + real JWT):**
- **Configured-boundary (proves configurability):** `WebApplicationFactory` overrides `Catalog:ApiSpec:MaxContentBytes` to a small value (e.g. 64); a `application/json` body of 65 bytes → **400**, `detail` contains the configured number. A 64-byte body → 201.
- Default oversized case reworked to not reference the removed const.
- Existing happy (201 first / 204 replace) + 415 + 403 + 404 + RLS cases retained.

**Backend — options validation (`Kartova.Catalog.Tests` unit):**
- `CatalogSpecOptionsValidator`: rejects 0, `1023`, `50*1024*1024 + 1`; accepts `1024`, `5 MiB`, `50 MiB`.

**Backend — domain unit (`Kartova.Catalog.Tests/ApiSpecTests.cs`):**
- `Validate` still rejects empty/whitespace + disallowed media type; **no** byte-cap assertion at the domain layer (moved to endpoint/integration).

**Backend — mutation (gate 6, BLOCKING — diff touches Domain/Application logic):**
- `/misc:mutation-sentinel` → `/misc:test-generator` on `ApiSpec.cs`, `CatalogEndpointDelegates.UpsertApiSpecAsync`, `CatalogSpecOptionsValidator`. Target ≥80%; document survivors.

**Frontend — Vitest + RTL:**
- `AttachApiSpecDialog.test.tsx`: file path + paste path; media-type inference (`.yaml`→yaml, `.json`→json, sniff) + manual override; empty rejection (no PUT fired); 415/403/400 → inline + toast; success → toast + close + invalidation.
- `ApiSpecSection.test.tsx`: has-spec (renders `<pre>`, Replace, copy, media badge) vs no-spec (empty state + Attach); button hidden without perm.
- `ApisTable.test.tsx` (extend): Spec column renders check vs "—" by `hasSpec`; skeleton cell count.
- `apis.test.tsx` (extend): raw-fetch GET/PUT compose correct URL + `Authorization` + `Content-Type` + raw body; 404 → `null`.

**Browser verify (gate, ADR-0084):** cold-start dev server → login `admin@orga` → register an API (DevSeed has none) → detail page → attach a `.yaml` then replace with `.json` → copy → back to list, confirm Spec column check → snapshot + console clean. **`react-aria <Table>` rowheader** unchanged (existing `displayName isRowHeader`), but re-verify no blank-page on dialog open.

**Container build (gate 4):** no Dockerfile/`COPY` change → runs as CI regression only, no new coverage.

---

## 7. Out of scope (deferred, named)

| Deferred | Owner |
|----------|-------|
| Spec **rendering** (Swagger UI / AsyncAPI React) | Phase 3 docs (E-11.F-02/F-03) |
| Spec **validation** (valid OpenAPI/AsyncAPI?) | E-14 policy |
| Spec **version history** (many per API) | E-21 |
| **Delete-spec** endpoint / UI (spec dies with API via cascade) | later / not needed |
| Spec **filter** on Apis list (has-spec / no-spec) | follow-up (ADR-0107 deferral recorded) |
| **Expose the cap value to the SPA** (client pre-check matching server) | later — only if the server-only 400 UX proves insufficient |
| Auto-fetch spec from `specUrl` at import | Phase 2 (E-03) |
| WSDL/XML style + media type | later |

---

## 8. Impact Analysis (codelens/LSP)

Changes an existing symbol (`ApiSpec.MaxContentBytes` const removal + `ApiSpec.Validate` behavior change). Per CLAUDE.md carve-out, **`const` blast radius is grepped, not codelens'd** (codelens under-reports inlined `const` refs).

**`ApiSpec.MaxContentBytes` (grep, verified):** 3 production refs + tests —
- `ApiSpec.cs:10` (decl), `:57`/`:58` (Validate) — **removed** this slice.
- `CatalogEndpointDelegates.cs:672`, `:679`, `:930` — **rewired** to the configured value (tasks cover each).
- `ProblemTypes.cs:85` — comment only, updated.
- Tests: `Kartova.Catalog.Tests/ApiSpecTests.cs` (5 refs), `IntegrationTests/ApiSpecTests.cs:245` — **updated** (tasks cover each).

**`ApiSpec.Validate` (private static):** only callers are `ApiSpec.Create` and `ApiSpec.Replace` (same file) — behavior narrows (drops size check); no external caller. Confirmed by reading `ApiSpec.cs` (private method, single file).

No interface/base-type change → no `find_implementations`/`get_type_hierarchy` needed. Frontend is new code (TS, no codelens) + additive edits to `ApisTable`/`ApiDetailPage`.

---

## 9. Definition of Done

Eight always-blocking gates + gate 6 (**blocking** — diff touches Domain/Application). Ledger at `docs/superpowers/verification/2026-07-07-catalog-api-spec-ui/dod.md` (+ `gate-findings.yaml`). Pre-push `scripts/ci-local.sh`. Gates as enumerated in CLAUDE.md — not restated.

**Size estimate:** ~450–520 LOC production (2 new FE components + FE data-layer + 2 new BE files + rewired endpoint + table/detail edits) — over the ~400 target, under the 800 ceiling; justified by the folded configurable-cap change (single concern: "make the shipped spec capability usable + tunable").
