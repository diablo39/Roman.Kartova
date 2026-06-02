# CursorCodec Filter-State Generalization — Design Spec

**Date:** 2026-06-01
**Author:** Roman Głogowski (AI-assisted)
**Status:** Draft → pending user review
**Scope:** Remove domain-specific filter knowledge from the shared cursor-pagination layer. `CursorCodec` and `QueryablePagingExtensions` become domain-agnostic; filter-state composition moves into the owning module (Catalog). Plus a minor cursor-validity bump.
**Refines:** ADR-0095 (cursor pagination contract) — amends the documented wire shape from `{ s, i, d }` to `{ s, i, d, f? }`.
**Related:** ADR-0073 (lifecycle states — source of the `includeDecommissioned` filter), ADR-0082 (modular monolith boundaries), ADR-0091 (RFC 7807 errors).

---

## 1. Problem

`CursorCodec` lives in `Kartova.SharedKernel.Pagination` — the most generic, widely-shared location in the modular monolith — and its name reads as a generic pagination primitive. The core machinery (base64url + JSON round-trip of `{ s, i, d }` = sort value / id / direction) genuinely *is* generic and is consumed by `QueryablePagingExtensions.ToCursorPagedAsync`, the keyset-pagination tail every module's list endpoint calls (ADR-0095).

But two **Catalog-domain filter fields** have leaked into this shared type:

- `ic` / `IncludeDecommissioned` — a Catalog lifecycle filter (ADR-0073, slice 6).
- `ou` / `OwnerUserId` — a Catalog applications filter (slice 9).

They are baked into four shared-layer locations:

| Location | Leak |
|---|---|
| `CursorCodec.DecodedCursor` | two Catalog-specific record properties |
| `CursorCodec.Encode` | two Catalog-specific optional parameters |
| `CursorCodec.CursorPayload` | JSON keys `ic`, `ou`; plus `Guid.TryParseExact` for `ou` |
| `QueryablePagingExtensions.ToCursorPagedAsync` | two `expected*` params + one hand-written mismatch branch per filter |

**Why now, not later.** The pattern has already repeated once: the slice-9 `ou` work describes itself verbatim as *"symmetric to includeDecommissioned"* / *"parallels the `ic` mismatch precedent"*. The recipe — add a named JSON field + a named `Encode` param + a `DecodedCursor` property + a bespoke mismatch branch — is an established clone that grows by one named field per future filtered list in any module. The second deliberate copy is the rule-of-three trigger.

This is **not** a naming/placement problem — the core codec belongs in SharedKernel and must stay there (moving it into Catalog would drag the shared keyset machinery and the `SharedKernel.Postgres` dependency into one bounded context). The fix is to remove the *domain knowledge*, not relocate the *code*.

## 2. Decisions

Brainstorming Q&A (2026-06-01):

| Q | Decision |
|---|---|
| Disposition in this PR | **Generalize now** (not defer, not leave-as-is). |
| Wire/back-compat | **Clean break.** Drop `ic`/`ou` decoding. Keep generic forward-compat (a cursor with no filter field = no constraint), but do not special-case the retired keys. Justified: ADR-0095 declares the cursor opaque + time-bound, and we are pre-MVP with no persisted cursors. The ADR only ever documented `{ s, i, d }` — `ic`/`ou` were never written down. |
| Filter-state representation | **B — generic string→string map (`f`).** The codec round-trips an opaque `IReadOnlyDictionary<string,string>` it never interprets; the owning module supplies the keys/values. Chosen over (A) a single opaque fingerprint string (loses per-filter diagnostics) and (C) a caller-supplied comparison strategy (over-engineered for two filters — YAGNI). |
| Comparer placement | A **separate pure type** `CursorFilterComparer` (not a static method on `CursorCodec`) so the "did the filter set change" logic is unit-testable without the codec. |
| Cursor "time validity" | Bump the frontend `useCursorList` `gcTime` default 5 min → 15 min. This is the only existing time bound on a cursor; there is **no** server-enforced cursor TTL and we are **not** adding one. |

## 3. Target design

### 3.1 `CursorCodec` (SharedKernel.Pagination) — domain-agnostic

```csharp
public static string Encode(
    object sortValue,
    Guid id,
    SortOrder direction,
    IReadOnlyDictionary<string, string>? filters = null);

public sealed record DecodedCursor(
    object SortValue,
    Guid Id,
    SortOrder Direction,
    IReadOnlyDictionary<string, string> Filters);   // never null; empty when `f` absent

public static DecodedCursor Decode(string cursor);
```

- Wire JSON: `{ s, i, d, f? }`. `CursorPayload` gains `[JsonPropertyName("f")] Dictionary<string,string>? F`; loses `ic`, `ou`.
- `Encode`: when `filters` is null **or empty**, pass `null` for `f` (omitted via `WhenWritingNull`). Otherwise serialize the map.
- `Decode`: `f` absent → `Filters` = empty read-only dictionary (so consumers never null-check).
- **Removed:** `Guid.TryParseExact` for `ou`. The codec no longer parses owner GUIDs — it round-trips opaque strings. A malformed `f` (non-object JSON, or non-string values) still fails closed: `JsonException` → `InvalidCursorException`.
- Unchanged: empty/whitespace, bad base64url, malformed JSON, missing `s`/`i`/`d`, bad `d` → `InvalidCursorException`. The `JsonValueKind` unwrap of `s` (with the `long`-vs-`double` widening guard) is untouched.

### 3.2 `CursorFilterComparer` (SharedKernel.Pagination) — NEW, pure

```csharp
public static class CursorFilterComparer
{
    /// Returns the first filter-set difference as (Name, Expected, Actual),
    /// or null when the two filter sets are equal.
    /// Expected = value the cursor was issued under; Actual = current request value.
    public static (string Name, string Expected, string Actual)? FindMismatch(
        IReadOnlyDictionary<string, string> cursorFilters,
        IReadOnlyDictionary<string, string> requestFilters);
}
```

- Walks the **sorted (ordinal) union** of keys; returns the **first** difference so the reported key is deterministic regardless of dictionary iteration order:
  - key in cursor only → `(key, cursorValue, "(none)")`
  - key in request only → `(key, "(none)", requestValue)`
  - both present, values differ (ordinal) → `(key, cursorValue, requestValue)`
  - all keys present with equal values → `null`
- `"(none)"` is the existing sentinel: non-empty, so it satisfies `CursorFilterMismatchException`'s non-empty guards.
- Key and value comparison is `StringComparer.Ordinal`.

### 3.3 `QueryablePagingExtensions` (SharedKernel.Postgres) — domain-agnostic

- Parameter swap: `bool? expectedIncludeDecommissioned = null, Guid? expectedOwnerUserId = null` → single `IReadOnlyDictionary<string,string>? expectedFilters = null`.
- The two hand-written mismatch branches collapse to:

```csharp
var mismatch = CursorFilterComparer.FindMismatch(
    decoded.Filters,
    expectedFilters ?? EmptyFilters);          // EmptyFilters = shared empty read-only dict
if (mismatch is { } m)
{
    throw new CursorFilterMismatchException(m.Name, m.Expected, m.Actual);
}
```

- The direction check (`decoded.Direction != order` → `InvalidCursorException`) is unchanged and runs first.
- Next-page cursor encoded with `expectedFilters` (omitted when null/empty): `CursorCodec.Encode(NormalizeForCursor(sortValue), id, order, expectedFilters)`.

### 3.4 `ListApplicationsHandler` (Catalog.Infrastructure) — owns the domain knowledge

```csharp
var filters = new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["includeDecommissioned"] = q.IncludeDecommissioned ? "true" : "false",
};
if (q.OwnerUserId is { } owner)
{
    filters["ownerUserId"] = owner.ToString("D");
}
// ...
.ToCursorPagedAsync(spec, q.SortOrder, q.Cursor, q.Limit,
    ApplicationSortSpecs.IdSelector, IdExtractor, ct,
    expectedFilters: filters);
```

Rule: **always-applied** dimensions (`includeDecommissioned` — always one boolean or the other) are always present in the map; **optional** filters (`ownerUserId`) are included only when applied.

## 4. Data flow & the correctness fix

A cursor is issued carrying the filter map it was produced under. On the next request, the request's map must **equal** the cursor's map; a difference in *either direction* — an added filter, a dropped filter, or a changed value — is a mismatch → 400 `cursor-filter-mismatch`. This is the single correct invariant (changing the row set mid-pagination makes the encoded boundary meaningless).

This **fixes a latent asymmetry** in the current code: the `ic` check skips when the caller opts out (`expectedIncludeDecommissioned is bool …`), but the `ou` check always fires (`Nullable.Equals(…)`). Under the map rule both are governed by one consistent comparison.

Teams/Invitations list handlers are unaffected: they pass no filters → issue cursors with no `f` → empty-vs-empty matches, no mismatch.

## 5. Error handling

| Case | Outcome (all unchanged in shape) |
|---|---|
| Empty / bad base64url / malformed JSON / missing `s`/`i`/`d` / bad `d` | `InvalidCursorException` → 400 `invalid-cursor` |
| Direction ≠ request `sortOrder` | `InvalidCursorException` → 400 |
| Filter set changed (added/dropped/value) | `CursorFilterMismatchException(filterName, expected, actual)` → 400 `cursor-filter-mismatch` |
| Tampered/garbage `ownerUserId` string in `f` | Won't match the request's canonical GUID string → `CursorFilterMismatchException` (fail-closed; no parse) |

`CursorFilterMismatchException` is unchanged (its `filterName`/`expectedValue`/`actualValue` shape already fits). Its XML-doc example references `includeDecommissioned`; update the summary wording to be filter-agnostic (nit, optional).

## 6. Scope add-on: cursor validity 5 → 15 min

- `web/src/lib/list/useCursorList.ts:27` — default `gcTime` `5 * 60 * 1000` → `15 * 60 * 1000`; update the doc comment on line 8 (`Default 5 min` → `Default 15 min`).
- No server-side change (no cursor TTL exists or is being added).

## 7. ADR-0095 amendment

Add a dated **Amendment (2026-06-01)** to `ADR-0095-cursor-pagination-contract.md`:

- Document the generalized wire shape `{ s, i, d, f? }` where `f` is an **opaque, caller-owned** filter map; the codec is domain-agnostic and never interprets `f`.
- Filter-mismatch detection is a **generic map comparison** (`CursorFilterComparer`); `CursorFilterMismatchException` reports the first differing key.
- Note this also closes the doc gap that `ic`/`ou` (slices 6/9) were code-only and never recorded in the ADR.
- Update the consequences note: frontend per-cursor cache `gcTime` 5 min → 15 min.

ADR text will be **previewed for user approval before saving** (project rule: preview ADR decisions first).

## 8. Testing

| Suite | Changes |
|---|---|
| `CursorCodecTests` (unit) | Replace `ic`/`ou` tests with generic `f`: populated map round-trips; null/empty filters → `f` omitted → decodes to empty dict; absent `f` → empty dict; malformed `f` (non-object / non-string values) → `InvalidCursorException`. Remove GUID-parse tests. Keep `s`-unwrap (long-vs-double) tests. |
| `CursorFilterComparerTests` (unit, NEW) | match → null; key-only-in-cursor; key-only-in-request; differing value; multiple diffs → first by sorted key; empty/empty → null; ordinal case-sensitivity of keys and values. |
| `QueryablePagingExtensionsTests` (integration) | Mismatch tests pass `expectedFilters`; assert the reported key per scenario; next cursor carries the filters; cursor-with-`f` replayed against a no-filter request → mismatch; no-filter caller (Teams/Invitations shape) → no mismatch. |
| Frontend | `useCursorList` test (if asserting `gcTime`) updated for 15 min. |

`CursorFilterComparer` is production logic (not a DTO/Contract) → **no** `[ExcludeFromCodeCoverage]`; it must be covered. `DecodedCursor` remains a nested data carrier (no attribute today; unchanged).

## 9. Definition of Done

Full project DoD (CLAUDE.md, nine gates): 0-warning build (`TreatWarningsAsErrors`), per-task spec+quality subagent reviews, branch-diff code review, full unit+architecture+integration (Testcontainers) suite, `/simplify`, mutation loop ≥80% on changed files, `/pr-review-toolkit:review-pr`, `/deep-review`. Because this touches the DB/pipeline keyset path, a `docker compose up` happy-path (paginate applications across a page boundary) **and** a negative path (filter-mismatch 400) must be captured.

## 10. Files touched

| File | Change |
|---|---|
| `src/Kartova.SharedKernel/Pagination/CursorCodec.cs` | generalize: `f` map, drop `ic`/`ou`/GUID-parse |
| `src/Kartova.SharedKernel/Pagination/CursorFilterComparer.cs` | **new** pure comparer |
| `src/Kartova.SharedKernel.Postgres/Pagination/QueryablePagingExtensions.cs` | param swap + comparer call |
| `src/Modules/Catalog/Kartova.Catalog.Infrastructure/ListApplicationsHandler.cs` | build filter map |
| `web/src/lib/list/useCursorList.ts` | `gcTime` 5 → 15 min + comment |
| `docs/architecture/decisions/ADR-0095-cursor-pagination-contract.md` | amendment (previewed first) |
| `tests/Kartova.SharedKernel.Tests/Pagination/CursorCodecTests.cs` | generic `f` tests |
| `tests/Kartova.SharedKernel.Tests/Pagination/CursorFilterComparerTests.cs` | **new** |
| `tests/Kartova.SharedKernel.Tests/Pagination/QueryablePagingExtensionsTests.cs` | `expectedFilters` tests |

## 11. Out of scope

- Server-enforced cursor expiry / TTL (explicitly rejected).
- Hashing/obfuscating filter values in the cursor (diagnostics > compactness; cursor already declared opaque).
- Any change to Teams/Invitations handlers (they pass no filters).
- Relocating or renaming `CursorCodec` (the core is correctly placed in SharedKernel).

## 12. Follow-ups (tracked)

**FU-1 — `ConvertCursorValue` returns 500 instead of 400 on a tampered sort-value *type*.**
Discovered during this slice's `/pr-review` (silent-failure lens, 2026-06-01). **Pre-existing — not introduced by this refactor.**

- **Where:** `src/Kartova.SharedKernel.Postgres/Pagination/QueryablePagingExtensions.cs` — `ConvertCursorValue` (the `DateTimeOffset.Parse` / `DateTime.Parse` / `Guid.Parse` / `Convert.ChangeType` block).
- **Symptom:** a tampered/hand-crafted cursor whose `s` (sort value) is the wrong type for the query's sort key (e.g. a non-date string for a `createdAt` query) makes `Parse`/`ChangeType` throw raw `FormatException` / `InvalidCastException` / `OverflowException`. These are **not** handled by `PagingExceptionHandler` (which maps only `InvalidCursorException` / `CursorFilterMismatchException` / `Invalid{Sort,Limit}*`), so the request returns **500** instead of the intended **400 `invalid-cursor`** — and a stack trace may leak if the global handler isn't tight.
- **Fix:** wrap the `ConvertCursorValue` body in `try { … } catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException) { throw new InvalidCursorException($"Cursor sort value '{value}' is not compatible with expected type {targetType.Name}.", ex); }`.
- **Test:** add a `ListApplicationsPaginationTests` case (or a `QueryablePagingExtensionsTests` case) that replays a cursor with a deliberately wrong-typed `s` and asserts **400 `invalid-cursor`** (not 500).
- **Why deferred here:** out of scope for the filter-state generalization (the diff never touched sort-value conversion); deserves its own small PR + dedicated tamper test rather than scope-creeping this one.
