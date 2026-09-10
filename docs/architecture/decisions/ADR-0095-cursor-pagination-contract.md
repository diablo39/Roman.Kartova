# ADR-0095: Cursor Pagination Contract — Wire Shape, Sort Syntax, and First-Cut Mandate

**Status:** Accepted
**Date:** 2026-05-04
**Deciders:** Roman Głogowski (solo developer)
**Category:** API & Integration Architecture
**Related:** ADR-0029 (REST as primary API style — refines its "pagination via cursors" mention), ADR-0083 (test pyramid), ADR-0090 (tenant scope), ADR-0091 (RFC 7807 error responses), ADR-0092 (REST URL convention).

## Context

ADR-0029 stated "pagination via cursors" as policy without a concrete contract. The Catalog `Applications` list endpoint is the first list shipped; without freezing the wire shape now, every later list endpoint (Components, Services, Libraries in E-02; logs and audit trails in later phases) re-debates it.

We also observed that adding pagination retroactively is a wire-shape break + a UI rework — both expensive — so the policy here covers not just *how* lists paginate, but *that* every list endpoint and screen does so from the first cut.

## Decision

1. **Wire envelope.** Every list endpoint returns `CursorPage<T> { items: T[], nextCursor: string | null, prevCursor: string | null }`.
2. **Sort syntax.** Two query params: `?sortBy=<field>&sortOrder=<asc|desc>`. `sortBy` is a per-resource enum surfaced by OpenAPI; the server enforces an allowlist. Single-field sort only in MVP.
3. **Cursor format.** Opaque base64url-encoded JSON `{ s, i, d }` — sort value, id (tiebreaker), direction. Format is internal; clients MUST treat the cursor as opaque.
4. **Pagination style.** Pure cursor; no `total`, no `page`, no `hasMore` (derivable from `nextCursor`). No `?include=total` opt-in in MVP.
5. **`prevCursor`.** Reserved on the wire; always `null` in MVP. Frontend manages "Prev" via a client-side cursor stack.
6. **Limit.** `?limit=N`, default 50, max 200, range error → 400 RFC 7807.
7. **Error type prefixes.** Base URI defined in ADR-0091 (`https://kartova.io/problems/`). ADR-0095 only adds the slugs: `invalid-sort-field`, `invalid-sort-order`, `invalid-cursor`, `invalid-limit` — i.e. `https://kartova.io/problems/invalid-sort-field`, `.../invalid-sort-order`, `.../invalid-cursor`, `.../invalid-limit`.
8. **Standing convention.** Every new list endpoint and every new list screen MUST be designed and implemented with sorting + cursor pagination from the first cut. Bounded lists (≤ N rows by domain invariant) MAY return a flat array, but MUST be decorated with `[BoundedListResult]` and an inline justification comment citing the cap. Default is paginated; opt-out is explicit.
9. **Architecture fitness test** enforces clause 8 — `tests/Kartova.ArchitectureTests/PaginationConventionRules.cs`.

## Consequences

- One reusable extension method (`IQueryable<T>.ToCursorPagedAsync`) carries the keyset filter, the `+1` trick, and the cursor codec. Handlers compose filters and call the extension at the tail.
- OpenAPI generates per-resource sort-field enums (`SortByApplications`, `SortByComponents`, …); the frontend gets compile-time-safe sort values.
- "Jump to page N" is impossible by construction. For screens that genuinely need it (admin moderation, audit trails), `[BoundedListResult]` on a separate endpoint is the escape hatch.
- Cursors are time-bound; sharing cursors across a sort change or a long-lived bookmark is brittle. The `d` field in the cursor JSON guards against direction-mismatched reuse → 400.

## Implementation notes

- `s` (sort value) carries a JSON scalar (string, number, or ISO-8601 timestamp string).
- Stable tiebreaker is `id` (Guid). The keyset filter uses the disjunctive form `key > @p OR (key = @p AND id > @p)` for `asc`, reversed for `desc` (chosen for portability across PostgreSQL + sqlite test path; row-constructor was the original target but was dropped per spec §14 mitigation).
- Cursor decode mismatching `d` against the request's `sortOrder` throws `InvalidCursorException` → 400.
- `gcTime` on frontend `useCursorList` set to 15 min default to bound the per-cursor `useQuery` cache growth (one cached entry per visited cursor in the in-memory stack).

## Amendment (2026-06-01): generalized filter state in the cursor

Clause 3's wire shape is generalized from `{ s, i, d }` to `{ s, i, d, f? }`:

- `f` is an **opaque, caller-owned** `string→string` filter map. `CursorCodec`
  never interprets it — the owning module (e.g. Catalog's `ListApplicationsHandler`)
  supplies the keys/values. Absent when no filters apply; decodes as an empty map.
- Filter-mismatch detection is a **generic** map comparison (`CursorFilterComparer`,
  sorted-union first-difference). `CursorFilterMismatchException` reports the first
  differing key; the shared codec/extension know no specific filter names.
- The request's filter map must equal the cursor's; a difference in either
  direction (added/dropped/changed) is a 400 `cursor-filter-mismatch`.

This closes a documentation gap: the slice-6 `ic` (includeDecommissioned) and
slice-9 `ou` (ownerUserId) cursor fields were code-only and never recorded here.
They are replaced by the generic `f` map (clean break — no legacy `ic`/`ou`
decoding; cursors are opaque + time-bound per this ADR).

**Consequences update:** the frontend per-cursor cache `gcTime` default is raised
from 5 min to 15 min (`web/src/lib/list/useCursorList.ts`).

## Cross-reference (2026-06-21)

This ADR owns only the **`f`-map wire format** (clause 3 / the 2026-06-01
amendment) — i.e. how filter state is encoded in and validated against the
opaque cursor. The **filter-consideration mandate** (every list must decide
whether it needs filters) and the **standard filter UI** (`<FilterBar>` /
`useListFilters`) are owned by **ADR-0107**. Look there for "how do we do list
filtering"; this ADR is purely the cursor transport.

## Amendment (2026-09-10): NULL boundary key (`n` flag) + null-safe keyset (TD-001)

Clause 3's wire shape is extended from `{ s, i, d, f? }` to `{ s?, i, d, f?, n? }` to
support pagination over a **nullable** sort key:

- `n` is an optional boolean. `n: true` means the boundary row's sort key was `NULL`;
  `s` is then omitted. Absent/false means `s` carries the boundary value exactly as
  before. **Backward compatible:** a non-nullable sort never emits `n`, so existing
  cursors are unchanged and decode identically; the decoder treats a missing `n` as
  "boundary key is present in `s`".
- A `NULL` boundary decodes to the `CursorNullSortValue` sentinel (not `null`), keeping
  `DecodedCursor.SortValue` non-null and distinguishing a genuine `NULL` key from a real
  empty string / zero value.

Keyset mechanism (`QueryablePagingExtensions`) gains a **null-safe path**, opt-in per
sort via `SortSpec<T>.IsNullable`:

- **Ordering:** `NULL`s sort **LAST for `asc`, FIRST for `desc`** — encoded explicitly
  via a portable null-flag key (`sortKey IS NULL`) prepended to the `ORDER BY`, because
  PostgreSQL (default NULLS LAST asc) and the sqlite test path (default NULLS FIRST) do
  not otherwise agree.
- **Predicate:** the disjunctive keyset filter is made null-aware so paging never
  truncates at a `NULL` boundary (the scalar predicate evaluates to SQL `UNKNOWN` → the
  entire NULLS block is silently dropped). It handles a `NULL` boundary key explicitly.
- **Non-nullable sorts are untouched** (default `IsNullable = false`): the original
  two-key `ORDER BY` and scalar predicate are byte-for-byte unchanged, so expression
  selectors matched to a partial index (the VM JSONB sorts) keep their index match.

This replaces the earlier per-column `?? ""` (COALESCE-to-empty-string) workaround on
`InfrastructureSortSpecs.Provider`, which conflated an empty-string provider with an
absent one and was not reusable for non-string nullable keys.

**Deferred:** the VM JSONB sort selectors remain `IsNullable = false`, relying on the
`VmAttributes.Validate` write-path invariant (every attribute key present + non-null);
whether they adopt the generic guard or slice-4 enforces attribute presence on ingest is
decided in slice-4 (see `docs/engineering/tech-debt.md` TD-001).

## Amendment (2026-09-10): sort-field discriminator (`sf`) — bind `sortBy` into the cursor (TD-004)

Clause 3's wire shape is extended from `{ s?, i, d, f?, n? }` to `{ s?, i, d, f?, n?, sf? }` to
bind the sort **field** the cursor was issued under — closing the same skip/repeat gap the
2026-06-01 `f`-map amendment closed for filters, but for the sort key.

**Problem.** The cursor bound the sort **order** (`d`) and the filter state (`f`) but not the
sort **field** (`sortBy`). A client that changed `sortBy` mid-pagination while keeping
`sortOrder` and reusing the cursor passed both guards; the new field's keyset predicate was
then applied against the **previous** field's boundary value. When the two fields share a
comparable CLR type (two strings, two timestamps) this does not error — it silently skips or
repeats rows.

- `sf` is an optional string: the `SortSpec<T>.FieldName` the cursor was issued under. Encoded
  on every new cursor; omitted (and normalized from blank) when a caller records no field.
- On decode the handler requires the request's `sortBy` (the resolved `SortSpec.FieldName`) to
  equal the cursor's `sf`; any difference is a 400 `cursor-sort-field-mismatch`
  (`CursorSortFieldMismatchException`), mirroring `cursor-filter-mismatch`.
- **Backward compatible:** an absent `sf` (a cursor issued before this field existed) decodes as
  "no field recorded" → no check, per the codec's forward-compat convention. Cursors are opaque
  and time-bound, so no legacy-`sf` decoding is needed.
- **No per-handler change:** the check lives entirely in `QueryablePagingExtensions.ToCursorPagedAsync`,
  which already receives the request's `SortSpec<T>` — the field name is read from `sort.FieldName`
  on both encode and decode. Clause 7's slug list gains `cursor-sort-field-mismatch`.
