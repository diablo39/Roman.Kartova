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
7. **Error type prefixes** (per ADR-0091): `https://kartova.dev/problems/invalid-sort-field`, `.../invalid-sort-order`, `.../invalid-cursor`, `.../invalid-limit`.
8. **Standing convention.** Every new list endpoint and every new list screen MUST be designed and implemented with sorting + cursor pagination from the first cut. Bounded lists (≤ N rows by domain invariant) MAY return a flat array, but MUST be decorated with `[BoundedListResult]` and an inline justification comment citing the cap. Default is paginated; opt-out is explicit.
9. **Architecture fitness test** enforces clause 8 — `tests/Kartova.ArchitectureTests/PaginationConventionRules.cs`.

## Consequences

- One reusable extension method (`IQueryable<T>.ToCursorPagedAsync`) carries the keyset filter, the `+1` trick, and the cursor codec. Handlers compose filters and call the extension at the tail.
- OpenAPI generates per-resource sort-field enums (`SortByApplications`, `SortByComponents`, …); the frontend gets compile-time-safe sort values.
- "Jump to page N" is impossible by construction. For screens that genuinely need it (admin moderation, audit trails), `[BoundedListResult]` on a separate endpoint is the escape hatch.
- Cursors are time-bound; sharing cursors across a sort change or a long-lived bookmark is brittle. The `d` field in the cursor JSON guards against direction-mismatched reuse → 400.

## Implementation notes

- `s` (sort value) carries a JSON scalar (string, number, or ISO-8601 timestamp string).
- Stable tiebreaker is `id` (Guid). The keyset filter uses PostgreSQL row-constructor comparison: `(sortKey, id) > (?, ?)` for `asc`, reversed for `desc`. EF Core's PostgreSQL provider translates this directly.
- Cursor decode mismatching `d` against the request's `sortOrder` throws `InvalidCursorException` → 400.
- `gcTime` on frontend `useCursorList` set to 5 min default to bound `useInfiniteQuery` cache growth.
