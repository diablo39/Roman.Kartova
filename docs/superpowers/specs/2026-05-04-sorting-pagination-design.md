# Sorting & Cursor Pagination — Design Spec

**Date:** 2026-05-04
**Author:** Roman Głogowski (AI-assisted)
**Status:** Draft → pending user review
**Scope:** Reusable API contract + frontend primitives for sortable, cursor-paginated list endpoints. Reference implementation on `GET /api/v1/catalog/applications`.
**Supersedes / refines:** ADR-0029 (REST as primary API style — "pagination via cursors") with a concrete contract.

---

## 1. Goal

Establish a single, reusable contract for sorting + pagination across every list endpoint and every list screen in Kartova, applied first to Applications. From this slice forward, **every new list endpoint and every new list screen MUST be designed and implemented with sorting + cursor pagination from the first cut** — no "ship flat list now, paginate later".

## 2. Background

| Surface | State at 2026-05-04 |
|---|---|
| API | `GET /api/v1/catalog/applications` returns `T[]` ordered `CreatedAt ASC, Id ASC`. No paging, no sort params. |
| UI | `ApplicationsTable` renders all rows. Columns: Name, Description. No sortable headers, no pager. |
| ADR-0029 | "Pagination via cursors" stated as policy; contract never instantiated. |
| Catalog scope | Applications is the first list endpoint. Components / Services / Libraries (E-02) follow. |

This slice freezes the contract before the second list endpoint forces ad-hoc choices.

## 3. Decisions

Brainstorming Q&A (2026-05-04) recorded the following:

| Q | Decision |
|---|---|
| Q1 — Scope | **B.** Reusable pattern now: API contract + frontend hook/component shell, applied to Applications as reference. |
| Q2 — Pagination style | **A.** Pure cursor, no `total` count. No `?include=total` opt-in in MVP. |
| Q3 — UI navigation | **A.** Prev / Next pager. No infinite scroll. No "page N of M". |
| Q4 — Sortable columns + default | **B.** Name + CreatedAt sortable, default `createdAt:desc`. Stable tiebreaker on `Id`. |
| Q5 — URL state | **C.** `sortBy` + `sortOrder` in URL query string; cursor is ephemeral (not in URL). |

Server-side factoring chosen: **C — `IQueryable<T>.ToCursorPagedAsync(...)` extension method** (not per-handler hand-roll, not a `KeysetPager` service).

Sort syntax chosen: **separate `sortBy` + `sortOrder` query params** (not compound `?sort=field:dir`).

## 4. API contract

### 4.1 Request

```
GET /api/v1/catalog/applications
    ?sortBy=createdAt           (optional; per-resource enum allowlist)
    &sortOrder=desc             (optional; enum: asc | desc)
    &cursor=<opaque>            (optional; absent = first page)
    &limit=50                   (optional; default 50, max 200)
```

- `sortBy` is a per-resource enum. Applications: `createdAt | name`. Generated as `SortByApplications` in OpenAPI.
- `sortOrder` is a shared enum: `asc | desc`. Default per-resource (Applications: `desc`).
- `cursor` is opaque base64url-encoded JSON `{ s: <sortValue>, i: <id>, d: "asc"|"desc" }`. The `s` field carries a JSON scalar (string, number, or ISO-8601 timestamp string) representing the last-row's sort key value. Format is internal; clients MUST treat the cursor as opaque.
- `limit` ∈ [1, 200]. Default 50.

### 4.2 Response envelope

Shared type `CursorPage<T>` lives in `Kartova.SharedKernel.Contracts`:

```jsonc
{
  "items": [ /* T[] */ ],
  "nextCursor": "eyJzIjoi...",   // null when last page
  "prevCursor": null              // always null in MVP — see §5.2
}
```

- `nextCursor !== null` ⟺ "more pages exist after this one".
- `prevCursor` is reserved on the wire for future server-emitted prev support; in MVP it is always `null`. Frontend manages prev navigation via a client-side cursor stack (see §6.1).
- No `total`, no `page`, no `hasMore` — derivable from `nextCursor`.

**Wire-shape break:** the response moves from `T[]` to `{ items: T[], nextCursor, prevCursor }`. Slice-4's frontend currently consumes `T[]`. The migration ships in the same commit as the API change. No external consumers exist (pre-MVP), so this is safe.

### 4.3 Errors (RFC 7807, per ADR-0091)

| Condition | Status | `type` |
|---|---|---|
| `sortBy` outside per-resource allowlist | 400 | `https://kartova.dev/problems/invalid-sort-field` (response includes `allowedFields`) |
| `sortOrder` not in {asc, desc} | 400 | `https://kartova.dev/problems/invalid-sort-order` |
| `cursor` malformed / tampered / direction-mismatched against `sortOrder` | 400 | `https://kartova.dev/problems/invalid-cursor` |
| `limit` outside [1, 200] | 400 | `https://kartova.dev/problems/invalid-limit` |
| Unknown id, cross-tenant id (404 semantics) | n/a — list endpoint, RLS auto-filters |

## 5. Server-side architecture

### 5.1 New SharedKernel pieces

| File | Purpose |
|---|---|
| `src/SharedKernel/Kartova.SharedKernel.Contracts/Pagination/CursorPage.cs` | Record `CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor, string? PrevCursor)`. `[ExcludeFromCodeCoverage]`. |
| `src/SharedKernel/Kartova.SharedKernel.Contracts/Pagination/SortOrder.cs` | Enum `SortOrder { Asc, Desc }`. `[ExcludeFromCodeCoverage]`. |
| `src/SharedKernel/Kartova.SharedKernel/Pagination/CursorCodec.cs` | Static `Encode((object sortValue, Guid id, SortOrder dir))` / `Decode(string)`. Base64url + `System.Text.Json`. |
| `src/SharedKernel/Kartova.SharedKernel/Pagination/SortSpec.cs` | `SortSpec<TEntity>(string FieldName, Expression<Func<TEntity, object>> KeySelector)`. |
| `src/SharedKernel/Kartova.SharedKernel/Pagination/QueryablePagingExtensions.cs` | `ToCursorPagedAsync<T>(...)` extension method. |
| `src/SharedKernel/Kartova.SharedKernel/Pagination/InvalidSortFieldException.cs` | Carries `FieldName` + `AllowedFields`. |
| `src/SharedKernel/Kartova.SharedKernel/Pagination/InvalidCursorException.cs` | — |
| `src/SharedKernel/Kartova.SharedKernel/Pagination/BoundedListResultAttribute.cs` | Marker for handlers exempt from the pagination fitness rule (see §8). |
| `src/SharedKernel/Kartova.SharedKernel.AspNetCore/PagingExceptionHandler.cs` | Maps `InvalidSortFieldException` / `InvalidCursorException` to RFC 7807 400. Registered alongside the existing `DomainValidationExceptionHandler`. |

### 5.2 `ToCursorPagedAsync` semantics

```csharp
public static Task<CursorPage<T>> ToCursorPagedAsync<T>(
    this IQueryable<T> source,
    SortSpec<T> sort,
    SortOrder order,
    string? cursor,
    int limit,
    Expression<Func<T, Guid>> idSelector,
    CancellationToken ct);
```

Behavior:

1. If `cursor` present: decode → `{ sortValue, id, direction }`. If `direction != order`: throw `InvalidCursorException`. Apply keyset filter `WHERE (sortKey, id) > (cursorSortValue, cursorId)` for `asc`, reversed for `desc`. PostgreSQL row-constructor comparison is used (`(a, b) > (?, ?)`).
2. Apply `ORDER BY sortKey <order>, id <order>`.
3. Take `limit + 1` rows. If `limit + 1` returned → trim last, encode `nextCursor` from the last *kept* row. Otherwise `nextCursor = null`.
4. `prevCursor = null` always (frontend manages prev — §6.1).

The extension method is the **only** place that knows about cursor encoding, the `+1` trick, the tiebreaker, and the keyset filter shape.

### 5.3 Catalog wiring

| File | Change |
|---|---|
| `src/Modules/Catalog/Kartova.Catalog.Contracts/ApplicationSortField.cs` | New enum `{ CreatedAt, Name }`. |
| `src/Modules/Catalog/Kartova.Catalog.Application/ListApplicationsQuery.cs` | Modified: `(ApplicationSortField SortBy, SortOrder SortOrder, string? Cursor, int Limit)`. |
| `src/Modules/Catalog/Kartova.Catalog.Infrastructure/ApplicationSortSpecs.cs` | New static class with two `SortSpec<Application>` instances (`CreatedAt`, `Name`). Co-located with the handler that enforces the allowlist. |
| `src/Modules/Catalog/Kartova.Catalog.Infrastructure/ListApplicationsHandler.cs` | Modified: resolves `SortSpec` from `SortBy`, calls `ToCursorPagedAsync`, returns `CursorPage<ApplicationResponse>`. |
| `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs` | Modified: `ListApplicationsAsync` binds `[FromQuery] sortBy, sortOrder, cursor, limit`; defaults `sortBy=createdAt`, `sortOrder=desc`, `limit=50`; max `limit=200` enforced. |

## 6. Frontend architecture

### 6.1 New primitive at `web/src/lib/list/`

| File | Purpose |
|---|---|
| `web/src/lib/list/types.ts` | `SortDirection`, `CursorListResult<T>`. |
| `web/src/lib/list/useCursorList.ts` | Generic hook over TanStack `useInfiniteQuery`. Returns `{ items, isLoading, isFetching, isError, hasNext, hasPrev, goNext(), goPrev(), reset() }`. Maintains internal cursor stack for prev. `reset()` runs automatically when `queryKey` changes (sort change resets pagination). |
| `web/src/lib/list/useListUrlState.ts` | Wraps React Router `useSearchParams`. Parameters `{ defaultSortBy, defaultSortOrder, allowedSortFields }`. Returns `{ sortBy, sortOrder, setSort }`. Validates URL params against allowlist; falls back to defaults on invalid input (no error UI). Cursor is **not** in URL. |

### 6.2 New shell at `web/src/components/application/data-table/`

| Component | Purpose |
|---|---|
| `<DataTable>` | Composes Untitled UI `<Table>` with sort + pager affordances. Opt-in sugar — not mandatory. |
| `<SortableHead>` | Wraps `<Table.Head>`. Shows sort indicator chevron; calls `onSortChange` on click; sets `aria-sort`. Headers declared non-sortable render plain `<Table.Head>`. |
| `<TablePager>` | Prev/Next buttons under the table. Disabled when `hasPrev`/`hasNext` is false. Shows current-page row count only ("50 results"). No "page N of M". |
| `<TableSkeleton>` | Extracted from current inline skeleton loop. |

### 6.3 Catalog wiring

| File | Change |
|---|---|
| `web/src/features/catalog/api/applications.ts` | `applicationKeys.list({ sortBy, sortOrder })` parameterized. New `useApplicationsList(params)` hook replaces `useApplications`. Existing invalidation via `applicationKeys.list()` prefix continues to work. |
| `web/src/features/catalog/components/ApplicationsTable.tsx` | Receives `list` + sort props. Uses `<SortableHead>` for Name + Created columns; plain `<Table.Head>` for Description; renders `<TablePager>`. |
| `web/src/features/catalog/pages/CatalogListPage.tsx` | Wires `useListUrlState` + `useCursorList`; passes through to `ApplicationsTable`. |
| `web/src/features/catalog/api/openapi-types.ts` (generated) | Regenerated after API change. Picks up `SortByApplications` enum, `SortOrder` enum, `CursorPage<ApplicationResponse>` envelope. |

### 6.4 Query-key invariants

- `applicationKeys.list({ sortBy, sortOrder })` — cursor is **not** in the key (each cursor is a `pageParam` inside `useInfiniteQuery`'s internal `pages` array).
- `useRegisterApplication` invalidates `applicationKeys.list()` (the prefix). After invalidation, `useInfiniteQuery` refetches from `pages[0]` (cursor = `undefined`, the first page). With default sort `createdAt:desc`, a freshly-registered app appears at the top.

## 7. Standards (apply to every future list)

This slice ratifies the following as a standing convention. Captured in:

1. **New ADR-00XX "Cursor pagination contract"** — the new ADR, supersedes the brief mention in ADR-0029. Authored as part of this slice.
2. **`CLAUDE.md` → "Working agreements"** — new bullet:
   > **List endpoints & list screens:** every new list endpoint exposes `sortBy` / `sortOrder` / `cursor` / `limit` and returns `CursorPage<T>` (ADR-00XX). Every new list screen wires `useCursorList` + `useListUrlState` + `<DataTable>`. Treat this as part of "first cut" — not a follow-up phase.
3. **Architecture fitness test** — see §8.

**Bounded-list opt-out:** if a list is bounded by domain invariant (e.g., enum, fixed cap), the handler MAY return a flat `IReadOnlyList<T>` *and* MUST be decorated with `[BoundedListResult]` and an inline justification comment citing the cap. Default is paginated; opt-out is explicit and reviewed.

## 8. Architecture fitness rule

New test class `tests/Kartova.ArchitectureTests/PaginationConventionRules.cs`:

> Any concrete handler whose name matches `List*Handler` in any module's `Infrastructure` assembly MUST have a `Handle` method whose return type is `Task<CursorPage<...>>` — **unless** the handler class is decorated with `[BoundedListResult]`.

Implementation: NetArchTest reflection over the slnx assemblies + `MethodInfo.ReturnType` inspection on `Handle`.

## 9. Testing

Five-tier pyramid (per ADR-0083):

### 9.1 Architecture (NetArchTest, mandatory CI gate)

`PaginationConventionRules` (§8). Asserts the standard.

### 9.2 Unit (xUnit)

| Subject | Cases |
|---|---|
| `CursorCodec` | Round-trip; base64url corruption → throws; JSON tampering → throws; sort-direction mismatch detection. |
| `ToCursorPagedAsync` (in-memory `IQueryable`) | Empty result; single-page result; exact-`limit` boundary (no next emitted); `limit + 1` boundary (next emitted); asc + desc; stable tiebreaker on duplicate sort values; `cursor.d ≠ order` → throws; unknown sort field → throws. |
| `ListApplicationsHandler` | Dispatch-to-extension wiring; `ApplicationSortField` → `SortSpec` resolution; default-params path. Uses sqlite or in-memory `CatalogDbContext`. |

### 9.3 Integration (Testcontainers, real PostgreSQL)

`tests/Kartova.Catalog.IntegrationTests/`:

- **Pagination correctness:** seed 150 applications across 2 tenants. Page through tenant-A's 75 in batches of 50. Assert no duplicates, no skips, exact ordering, RLS hides tenant-B.
- **Validation 400s:** `?sortBy=invalid` → 400 RFC 7807 `invalid-sort-field` with `allowedFields` payload. Tampered cursor → 400 `invalid-cursor`. `limit=0` and `limit=201` → 400 `invalid-limit`.
- **Defaults:** request with no query params returns same result as `?sortBy=createdAt&sortOrder=desc&limit=50`.

### 9.4 Frontend unit (Vitest + React Testing Library)

| Subject | Cases |
|---|---|
| `useListUrlState` | Defaults applied when URL empty; URL takes precedence; invalid `sortBy` falls back to default; round-trip via `setSort`. |
| `useCursorList` | Prev/Next stack invariants (mock `useInfiniteQuery`); `reset()` triggered on `queryKey` change. |
| `<TablePager>` | Disabled states for `hasPrev` / `hasNext`. |
| `<SortableHead>` | Click toggles asc → desc → asc; `aria-sort` attribute correct. |

### 9.5 E2E smoke (Playwright MCP, dev-server cold start)

One golden-path scenario:

> Cold-start dev server → navigate to `/catalog` with seeded ~120 apps → assert default order (createdAt desc) → click "Name" header → URL becomes `?sortBy=name&sortOrder=asc` → assert ordering changed → click "Next" → assert new rows + Prev enabled → click "Prev" → original rows back → console clean.

### 9.6 Coverage targets

- `CursorCodec`, `QueryablePagingExtensions`, `ApplicationSortSpecs`, `ListApplicationsHandler`: 85% branch coverage; Stryker mutation ≥80%.
- `CursorPage`, `SortOrder`, `SortSpec`, `BoundedListResultAttribute`, exception types: `[ExcludeFromCodeCoverage]` (per repo Contracts/DTO/marker rule).
- Frontend hooks: standard Vitest coverage; `<DataTable>` shell components covered by hook + smoke tests (no separate snapshot tests).

## 10. Dev seed

Bump dev seed (Org A) from ~3 applications to **~120 applications** so Prev/Next is exercisable manually in `docker compose up`. Otherwise pagination is invisible at MVP scale and the smoke E2E lacks signal.

Seed values: deterministic, varied `name` and `displayName` so sorting by `name` produces a visibly different order from `createdAt`.

## 11. ADR work

**New ADR-00XX "Cursor pagination contract"** — written as part of this slice. The concrete number is assigned at implementation time to avoid collision with parallel ADR work (note: ADR-0092 currently exists twice in the repo — `ADR-0092-rest-api-url-convention.md` and `ADR-0092-untitled-ui-component-library.md`; this slice does not fix that collision but flags it). Captures:

- Wire shape (`?sortBy`, `?sortOrder`, `?cursor`, `?limit`).
- Opaque cursor format (`{ s, i, d }` base64url JSON, internal; clients treat as opaque).
- Per-resource sort allowlist enforcement; RFC 7807 error types.
- `prevCursor` always-null-in-MVP convention; client-side cursor stack.
- Standing convention (§7) — every new list paginates by default, `[BoundedListResult]` for opt-out.

ADR-0029 stays accepted; the new ADR refines its "pagination via cursors" mention with the concrete contract.

## 12. Definition of Done

Per CLAUDE.md DoD:

1. Solution build with `TreatWarningsAsErrors=true` — 0 warnings, 0 errors.
2. Per-task subagent reviews (spec-compliance + code-quality) on each plan task.
3. Slice-level `superpowers:requesting-code-review` against the full branch diff.
4. All tier 1–4 tests green (architecture + unit + integration + frontend unit).
5. **`docker compose up` smoke evidence required** — this slice changes wire shape and adds query-string binding. Capture: `curl /api/v1/catalog/applications?sortBy=name&limit=10` returning `{ items, nextCursor, prevCursor }` envelope, and one negative path (`?sortBy=garbage` → 400 RFC 7807). Plus one Playwright session (§9.5).

## 13. Out of scope

- Multi-field sort (`?sortBy=createdAt,name`) — deferred. Single-field is sufficient for MVP.
- Server-emitted `prevCursor` — reserved on the wire, but not implemented. Frontend manages prev via cursor stack.
- `?include=total` opt-in for total count — deferred. Re-evaluate when a screen genuinely needs it.
- Filtering (`?filter=...`, full-text search) — separate concern, separate slice.
- Cursor TTL / signed cursors — cursors are time-bound by domain semantics (a row at cursor X today may have moved), but no explicit expiry / signature is added.
- Infinite scroll — explicitly rejected for tabular data (Q3 = A).
- Page-number jumps ("go to page 47") — incompatible with pure cursor; explicitly out.

## 14. Risks

| Risk | Mitigation |
|---|---|
| EF Core's PostgreSQL provider may not translate row-constructor comparison `(a, b) > (?, ?)` for all sort-key types (e.g., nullable timestamps). | Validate during Task 1 (extension method spike) with sqlite + Testcontainers. Fall back to disjunctive form `(a > ? OR (a = ? AND b > ?))` if needed — same correctness, slightly slower planner. |
| Cursor stability under inserts is correct, but under **deletes** of the cursor row, `(sortKey, id) > (deletedSortValue, deletedId)` still works (the deleted row is just absent from the result). Validate. | Integration test: delete a row mid-pagination, assert next page is correct. |
| `useInfiniteQuery` cache may grow large if the user pages deeply then sorts; React Query's gcTime applies but worth checking. | Set explicit `gcTime` on `useCursorList` (default 5 min). Document. |
| OpenAPI codegen may not handle the per-resource enum `SortByApplications` cleanly across multiple modules later. | Verify by adding a placeholder second list endpoint (e.g., `SortByComponents`) in unit tests, even if Components endpoint is post-slice. |

## 15. Effort estimate

Order-of-magnitude:

- Backend SharedKernel pieces + extension method + tests: ~1 day.
- Catalog wiring + integration tests: ~0.5 day.
- Frontend `useCursorList` + `useListUrlState` + `<DataTable>` shell + tests: ~1 day.
- Catalog page wiring + Playwright smoke + dev-seed bump: ~0.5 day.
- Architecture fitness test + ADR + CLAUDE.md update: ~0.5 day.

Total: ~3.5 days of focused work.
