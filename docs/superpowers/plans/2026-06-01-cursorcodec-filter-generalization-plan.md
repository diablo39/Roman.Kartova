# CursorCodec Filter-State Generalization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove Catalog-specific filter knowledge (`ic`/`ownerUserId`) from the shared `CursorCodec` + `QueryablePagingExtensions`; the cursor carries an opaque caller-owned `string→string` filter map, and filter-state composition moves into `ListApplicationsHandler`.

**Architecture:** The cursor JSON becomes `{ s, i, d, f? }` where `f` is an opaque map the codec never interprets. A new pure `CursorFilterComparer.FindMismatch` (sorted-union, first-difference) replaces the two hand-written `ic`/`ou` mismatch branches in the extension. The Catalog handler builds the map (`includeDecommissioned` always; `ownerUserId` when applied). Clean break — no legacy `ic`/`ou` decoding (ADR-0095 declares cursors opaque + time-bound; pre-MVP, no persisted cursors). Plus a frontend `gcTime` 5→15 min bump and an ADR-0095 amendment.

**Tech Stack:** .NET 10, System.Text.Json, EF Core (sqlite test path + Npgsql), MSTest v4 + native asserts (ADR-0097), React + TanStack Query.

**Spec:** `docs/superpowers/specs/2026-06-01-cursorcodec-filter-generalization-design.md`

---

## File Structure

| File | Responsibility | Change |
|---|---|---|
| `src/Kartova.SharedKernel/Pagination/CursorFilterComparer.cs` | Pure filter-set diff (no EF, no codec) | **Create** |
| `tests/Kartova.SharedKernel.Tests/Pagination/CursorFilterComparerTests.cs` | Unit tests for the comparer | **Create** |
| `src/Kartova.SharedKernel/Pagination/CursorCodec.cs` | Opaque cursor encode/decode | Modify — `f` map; drop `ic`/`ou`/GUID-parse |
| `src/Kartova.SharedKernel/Pagination/CursorFilterMismatchException.cs` | 400 on filter change | Modify — XML-doc wording only (filter-agnostic) |
| `src/Kartova.SharedKernel.Postgres/Pagination/QueryablePagingExtensions.cs` | Keyset paging tail | Modify — `expectedFilters` param + comparer |
| `src/Modules/Catalog/Kartova.Catalog.Infrastructure/ListApplicationsHandler.cs` | Owns the filter map | Modify — build map, pass `expectedFilters` |
| `tests/Kartova.SharedKernel.Tests/Pagination/CursorCodecTests.cs` | Codec unit tests | Modify — replace `ic`/`ou` tests with `f` tests |
| `tests/Kartova.SharedKernel.Tests/Pagination/QueryablePagingExtensionsTests.cs` | Extension integration tests | Modify — `ic` tests → `expectedFilters` tests |
| `web/src/lib/list/useCursorList.ts` | Frontend cursor list driver | Modify — `gcTime` default 5→15 min |
| `docs/architecture/decisions/ADR-0095-cursor-pagination-contract.md` | Contract record | Modify — amendment (previewed first) |

**Unchanged but must be re-verified green:** `PagingExceptionHandler.cs` + `PagingExceptionHandlerTests.cs`, `CursorFilterMismatchExceptionTests.cs`, `ListApplicationsPaginationTests.cs` (Catalog integration — exercises cursor-filter-mismatch over HTTP; exception shape is preserved so behavior is identical).

---

## Task 1: CursorFilterComparer (pure, independent)

**Files:**
- Create: `src/Kartova.SharedKernel/Pagination/CursorFilterComparer.cs`
- Test: `tests/Kartova.SharedKernel.Tests/Pagination/CursorFilterComparerTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/Kartova.SharedKernel.Tests/Pagination/CursorFilterComparerTests.cs`:

```csharp
using Kartova.SharedKernel.Pagination;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kartova.SharedKernel.Tests.Pagination;

[TestClass]
public sealed class CursorFilterComparerTests
{
    private static IReadOnlyDictionary<string, string> Map(params (string K, string V)[] pairs)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (k, v) in pairs) d[k] = v;
        return d;
    }

    [TestMethod]
    public void Equal_maps_return_null()
    {
        var result = CursorFilterComparer.FindMismatch(
            Map(("includeDecommissioned", "false"), ("ownerUserId", "abc")),
            Map(("ownerUserId", "abc"), ("includeDecommissioned", "false"))); // order-independent

        Assert.IsNull(result);
    }

    [TestMethod]
    public void Empty_vs_empty_returns_null()
    {
        Assert.IsNull(CursorFilterComparer.FindMismatch(Map(), Map()));
    }

    [TestMethod]
    public void Key_only_in_cursor_reports_none_as_actual()
    {
        var result = CursorFilterComparer.FindMismatch(
            Map(("ownerUserId", "G1")), Map());

        Assert.IsNotNull(result);
        Assert.AreEqual("ownerUserId", result.Value.Name);
        Assert.AreEqual("G1", result.Value.Expected);
        Assert.AreEqual("(none)", result.Value.Actual);
    }

    [TestMethod]
    public void Key_only_in_request_reports_none_as_expected()
    {
        var result = CursorFilterComparer.FindMismatch(
            Map(), Map(("ownerUserId", "G2")));

        Assert.IsNotNull(result);
        Assert.AreEqual("ownerUserId", result.Value.Name);
        Assert.AreEqual("(none)", result.Value.Expected);
        Assert.AreEqual("G2", result.Value.Actual);
    }

    [TestMethod]
    public void Differing_value_reports_both_sides()
    {
        var result = CursorFilterComparer.FindMismatch(
            Map(("includeDecommissioned", "true")),
            Map(("includeDecommissioned", "false")));

        Assert.IsNotNull(result);
        Assert.AreEqual("includeDecommissioned", result.Value.Name);
        Assert.AreEqual("true", result.Value.Expected);
        Assert.AreEqual("false", result.Value.Actual);
    }

    [TestMethod]
    public void Multiple_differences_return_first_by_ordinal_key()
    {
        // Keys "a" and "z" both differ; ordinal-sorted first is "a".
        var result = CursorFilterComparer.FindMismatch(
            Map(("z", "1"), ("a", "1")),
            Map(("z", "2"), ("a", "2")));

        Assert.IsNotNull(result);
        Assert.AreEqual("a", result.Value.Name);
    }

    [TestMethod]
    public void Value_comparison_is_ordinal_case_sensitive()
    {
        var result = CursorFilterComparer.FindMismatch(
            Map(("k", "Value")), Map(("k", "value")));

        Assert.IsNotNull(result);
        Assert.AreEqual("k", result.Value.Name);
    }

    [TestMethod]
    public void Key_comparison_is_ordinal_case_sensitive()
    {
        // "K" (cursor) and "k" (request) are distinct keys → "K" is only-in-cursor.
        // Ordinal sort places uppercase 'K' (0x4B) before lowercase 'k' (0x6B),
        // so the first reported difference is "K".
        var result = CursorFilterComparer.FindMismatch(
            Map(("K", "v")), Map(("k", "v")));

        Assert.IsNotNull(result);
        Assert.AreEqual("K", result.Value.Name);
        Assert.AreEqual("v", result.Value.Expected);
        Assert.AreEqual("(none)", result.Value.Actual);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Kartova.SharedKernel.Tests/Kartova.SharedKernel.Tests.csproj --filter "FullyQualifiedName~CursorFilterComparerTests"`
Expected: FAIL — `CursorFilterComparer` does not exist (CS0103 / build error).

- [ ] **Step 3: Implement the comparer**

Create `src/Kartova.SharedKernel/Pagination/CursorFilterComparer.cs`:

```csharp
namespace Kartova.SharedKernel.Pagination;

/// <summary>
/// Compares the filter state a cursor was issued under against the filter state
/// of the current request (ADR-0095). Domain-agnostic: it never interprets
/// filter names or values — the owning module (e.g. Catalog's
/// <c>ListApplicationsHandler</c>) supplies the key/value pairs. A difference in
/// either direction (added, dropped, or changed) means paging would skip or
/// repeat rows, so the caller raises a 400.
/// </summary>
public static class CursorFilterComparer
{
    private const string Absent = "(none)";

    /// <summary>
    /// Returns the first filter-set difference as (Name, Expected, Actual), or
    /// <c>null</c> when the two sets are equal. <c>Expected</c> is the value the
    /// cursor was issued under; <c>Actual</c> is the current request value. Keys
    /// are walked in ordinal-sorted order so the reported difference is
    /// deterministic regardless of dictionary iteration order. A key present on
    /// only one side reports <c>"(none)"</c> for the missing side. Key and value
    /// comparison is ordinal.
    /// </summary>
    public static (string Name, string Expected, string Actual)? FindMismatch(
        IReadOnlyDictionary<string, string> cursorFilters,
        IReadOnlyDictionary<string, string> requestFilters)
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var k in cursorFilters.Keys) keys.Add(k);
        foreach (var k in requestFilters.Keys) keys.Add(k);

        foreach (var key in keys)
        {
            var inCursor = cursorFilters.TryGetValue(key, out var cursorValue);
            var inRequest = requestFilters.TryGetValue(key, out var requestValue);

            if (inCursor && inRequest)
            {
                if (!string.Equals(cursorValue, requestValue, StringComparison.Ordinal))
                {
                    return (key, cursorValue!, requestValue!);
                }
            }
            else if (inCursor)
            {
                return (key, cursorValue!, Absent);
            }
            else
            {
                return (key, Absent, requestValue!);
            }
        }

        return null;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Kartova.SharedKernel.Tests/Kartova.SharedKernel.Tests.csproj --filter "FullyQualifiedName~CursorFilterComparerTests"`
Expected: PASS — 8/8.

- [ ] **Step 5: Commit**

```bash
git add src/Kartova.SharedKernel/Pagination/CursorFilterComparer.cs tests/Kartova.SharedKernel.Tests/Pagination/CursorFilterComparerTests.cs
git commit -m "feat(cursor): pure CursorFilterComparer for filter-set diff (ADR-0095)"
```

---

## Task 2: Generalize CursorCodec + extension + handler (coordinated)

> **Note:** This is a cross-cutting signature change. The `Encode`/`DecodedCursor` shape changes break `QueryablePagingExtensions`, `ListApplicationsHandler`, and the two existing test files simultaneously — there is no green intermediate. Apply all edits (Steps 1–5) before the single build/test/commit (Steps 6–8).

**Files:**
- Modify: `src/Kartova.SharedKernel/Pagination/CursorCodec.cs`
- Modify: `src/Kartova.SharedKernel/Pagination/CursorFilterMismatchException.cs` (doc only)
- Modify: `src/Kartova.SharedKernel.Postgres/Pagination/QueryablePagingExtensions.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/ListApplicationsHandler.cs`
- Modify: `tests/Kartova.SharedKernel.Tests/Pagination/CursorCodecTests.cs`
- Modify: `tests/Kartova.SharedKernel.Tests/Pagination/QueryablePagingExtensionsTests.cs`

- [ ] **Step 1: Rewrite `CursorCodec.cs`**

Replace the entire file with:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kartova.SharedKernel.Pagination;

/// <summary>
/// Encodes and decodes opaque pagination cursors per ADR-0095.
/// Wire format is base64url-encoded JSON <c>{ s, i, d, f? }</c>:
/// <list type="bullet">
/// <item><description><c>s</c> — sort value of the boundary row (string|number|ISO-8601 string)</description></item>
/// <item><description><c>i</c> — boundary row id (Guid, tiebreaker)</description></item>
/// <item><description><c>d</c> — direction the cursor was produced under ("asc"|"desc"). The handler verifies this matches the request's <c>sortOrder</c> to detect reused cursors across a sort flip.</description></item>
/// <item><description><c>f</c> — optional, opaque filter state the cursor was issued under: a string→string map the codec never interprets. The owning module (e.g. Catalog) supplies the keys/values; <see cref="CursorFilterComparer"/> detects a filter change mid-pagination, surfaced as <see cref="CursorFilterMismatchException"/>. Absent when no filters apply, decoding as an empty map.</description></item>
/// </list>
/// </summary>
public static class CursorCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private static readonly IReadOnlyDictionary<string, string> EmptyFilters =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public sealed record DecodedCursor(
        object SortValue,
        Guid Id,
        SortOrder Direction,
        IReadOnlyDictionary<string, string> Filters);

    public static string Encode(
        object sortValue,
        Guid id,
        SortOrder direction,
        IReadOnlyDictionary<string, string>? filters = null)
    {
        // `f` is omitted from the JSON when no filters apply (null/empty) to keep
        // cursors short and forward-compatible: a decoder that sees no `f` treats
        // it as "no filter state". The codec never interprets the keys/values —
        // they are opaque, owned by the calling module.
        var payload = new CursorPayload(
            sortValue,
            id,
            direction == SortOrder.Asc ? "asc" : "desc",
            filters is { Count: > 0 }
                ? new Dictionary<string, string>(filters, StringComparer.Ordinal)
                : null);
        var json = JsonSerializer.SerializeToUtf8Bytes(payload, Options);
        return ToBase64Url(json);
    }

    public static DecodedCursor Decode(string cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            throw new InvalidCursorException("Cursor is empty.");
        }

        byte[] bytes;
        try
        {
            bytes = FromBase64Url(cursor);
        }
        catch (FormatException ex)
        {
            throw new InvalidCursorException("Cursor is not valid base64url.", ex);
        }

        CursorPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<CursorPayload>(bytes, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidCursorException("Cursor JSON is malformed.", ex);
        }

        if (payload is null
            || payload.S is null
            || payload.I == Guid.Empty
            || payload.D is not "asc" and not "desc")
        {
            throw new InvalidCursorException("Cursor is missing required fields.");
        }

        var direction = payload.D == "asc" ? SortOrder.Asc : SortOrder.Desc;
        var sortValue = payload.S is JsonElement el ? UnwrapJsonElement(el) : payload.S;
        // Cursors with no filter state (or cursors issued before any filter
        // existed) omit `f`; decode as an empty map so consumers never null-check.
        IReadOnlyDictionary<string, string> filters = payload.F is { Count: > 0 }
            ? payload.F
            : EmptyFilters;
        return new DecodedCursor(sortValue, payload.I, direction, filters);
    }

    private static object UnwrapJsonElement(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString()!,
        JsonValueKind.Number =>
            // The (object) cast on the long arm prevents C# `?:` from finding the
            // common-type `double` between `long` and `double` and silently widening
            // `i` to `42.0`. Without it, every integer-cursor sort value decodes as
            // System.Double instead of Int64.
            // Downstream cursor comparisons (`ConvertCursorValue`, JSON re-serialization for
            // the next page) compare runtime types via Object.Equals — silently widening to
            // double flips paging behaviour, not just the boxed type label.
            el.TryGetInt64(out var i) ? (object)i : el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => throw new InvalidCursorException("Cursor sort value must not be null."),
        _ => throw new InvalidCursorException($"Unsupported cursor sort-value kind: {el.ValueKind}"),
    };

    private static string ToBase64Url(byte[] bytes) =>
        System.Buffers.Text.Base64Url.EncodeToString(bytes);

    private static byte[] FromBase64Url(string s) =>
        System.Buffers.Text.Base64Url.DecodeFromChars(s.AsSpan());

    private sealed record CursorPayload(
        [property: JsonPropertyName("s")] object? S,
        [property: JsonPropertyName("i")] Guid I,
        [property: JsonPropertyName("d")] string? D,
        [property: JsonPropertyName("f")] Dictionary<string, string>? F);
}
```

- [ ] **Step 2: Update `CursorFilterMismatchException.cs` XML doc (filter-agnostic wording)**

In `src/Kartova.SharedKernel/Pagination/CursorFilterMismatchException.cs`, replace the `<summary>` block:

```csharp
/// <summary>
/// Thrown when a paginated request's filter parameters do not match the filter
/// state encoded in the supplied cursor. This is a 400 Bad Request — the cursor
/// was issued under a different filter, so paging would silently skip rows or
/// repeat them. Mapped to RFC 7807 by <c>PagingExceptionHandler</c> with
/// problem-type slug <c>cursor-filter-mismatch</c>. The differing filter is
/// reported generically via <see cref="CursorFilterComparer"/>; the codec and
/// extension know no specific filter names. ADR-0095.
/// </summary>
```

(Leave the rest of the class — constructor, `MakeMessage`, properties — untouched.)

- [ ] **Step 3: Update `QueryablePagingExtensions.cs`**

3a. Add a shared empty map field. After the `DefaultLimit` const (line ~16), inside the class body:

```csharp
    private static readonly IReadOnlyDictionary<string, string> EmptyFilters =
        new Dictionary<string, string>(StringComparer.Ordinal);
```

3b. Replace the method signature's two trailing filter params. Change:

```csharp
        Func<T, Guid> idExtractor,
        CancellationToken ct,
        bool? expectedIncludeDecommissioned = null,
        Guid? expectedOwnerUserId = null)
        where T : class
```

to:

```csharp
        Func<T, Guid> idExtractor,
        CancellationToken ct,
        IReadOnlyDictionary<string, string>? expectedFilters = null)
        where T : class
```

3c. Replace the XML `<para>` on the overload (the block describing `expectedOwnerUserId`) with:

```csharp
    /// <para>
    /// <paramref name="expectedFilters"/> — ADR-0095 filter-state replay. An
    /// opaque, caller-owned string→string map of the filters this request
    /// applies. The codec encodes it into the next cursor; on decode,
    /// <see cref="CursorFilterComparer"/> requires the request's map to equal the
    /// cursor's map and fails closed via <see cref="CursorFilterMismatchException"/>
    /// on any difference (added, dropped, or changed). Null/empty when the caller
    /// applies no filters, in which case `f` is omitted from the cursor and an
    /// incoming cursor that carries filter state is itself a mismatch.
    /// </para>
```

3d. Replace the cursor-decode filter branches. Change this block:

```csharp
            var decoded = CursorCodec.Decode(cursor);
            if (decoded.Direction != order)
            {
                throw new InvalidCursorException(
                    $"Cursor was issued for direction '{decoded.Direction}' but request uses '{order}'.");
            }
            if (expectedIncludeDecommissioned is bool requestFilter
                && decoded.IncludeDecommissioned != requestFilter)
            {
                throw new CursorFilterMismatchException(
                    filterName: "includeDecommissioned",
                    expectedValue: decoded.IncludeDecommissioned ? "true" : "false",
                    actualValue: requestFilter ? "true" : "false");
            }
            // ownerUserId mismatch — symmetric to includeDecommissioned. The decoded
            // value may be null (cursor issued without an owner filter); a null/value
            // delta is a real mismatch and must trip the same exception. Using
            // Nullable<Guid>.Equals keeps the comparison value-typed (no boxing) and
            // gives the same null-vs-value semantics across the four (null,null) /
            // (null,value) / (value,null) / (value,value) cases.
            if (!Nullable.Equals(decoded.OwnerUserId, expectedOwnerUserId))
            {
                throw new CursorFilterMismatchException(
                    filterName: "ownerUserId",
                    expectedValue: decoded.OwnerUserId?.ToString("D") ?? "(none)",
                    actualValue: expectedOwnerUserId?.ToString("D") ?? "(none)");
            }
            q = ApplyKeysetFilter(q, sort.KeySelector, idSelector, decoded.SortValue, decoded.Id, order);
```

to:

```csharp
            var decoded = CursorCodec.Decode(cursor);
            if (decoded.Direction != order)
            {
                throw new InvalidCursorException(
                    $"Cursor was issued for direction '{decoded.Direction}' but request uses '{order}'.");
            }
            // Filter-state replay (ADR-0095): the request's filter set must equal
            // the set the cursor was issued under, or paging would skip/repeat
            // rows. Domain-agnostic — CursorFilterComparer never interprets the
            // keys; the owning handler supplies them. A difference in either
            // direction is a 400.
            var mismatch = CursorFilterComparer.FindMismatch(
                decoded.Filters, expectedFilters ?? EmptyFilters);
            if (mismatch is { } m)
            {
                throw new CursorFilterMismatchException(m.Name, m.Expected, m.Actual);
            }
            q = ApplyKeysetFilter(q, sort.KeySelector, idSelector, decoded.SortValue, decoded.Id, order);
```

3e. Replace the next-cursor encode. Change:

```csharp
            nextCursor = CursorCodec.Encode(
                    NormalizeForCursor(sortValue),
                    id,
                    order,
                    expectedIncludeDecommissioned ?? false,
                    expectedOwnerUserId);
```

to:

```csharp
            nextCursor = CursorCodec.Encode(
                    NormalizeForCursor(sortValue),
                    id,
                    order,
                    expectedFilters);
```

- [ ] **Step 4: Update `ListApplicationsHandler.cs`**

4a. Replace the comment on line ~47 (`The cursor JSON (CursorCodec.ic) is mismatch-checked…`) and the slice-9 `ou` comment block (lines ~61–65) wording is fine to keep, but update the `.ToCursorPagedAsync` call. Replace:

```csharp
        var page = await source
            .ToCursorPagedAsync(
                spec, q.SortOrder, q.Cursor, q.Limit,
                ApplicationSortSpecs.IdSelector, IdExtractor, ct,
                expectedIncludeDecommissioned: q.IncludeDecommissioned,
                expectedOwnerUserId: q.OwnerUserId);
```

with:

```csharp
        // Filter state the cursor is issued under (ADR-0095). The owning module
        // owns the keys/values; the shared codec treats them as opaque. Always-
        // applied dimensions (includeDecommissioned) are always present; optional
        // filters (ownerUserId) only when applied. A change mid-pagination trips
        // CursorFilterMismatchException inside ToCursorPagedAsync.
        var filters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["includeDecommissioned"] = q.IncludeDecommissioned ? "true" : "false",
        };
        if (q.OwnerUserId is { } owner)
        {
            filters["ownerUserId"] = owner.ToString("D");
        }

        var page = await source
            .ToCursorPagedAsync(
                spec, q.SortOrder, q.Cursor, q.Limit,
                ApplicationSortSpecs.IdSelector, IdExtractor, ct,
                expectedFilters: filters);
```

4b. (Optional, cosmetic) On line ~47, change `The cursor JSON (CursorCodec.ic) is mismatch-checked inside` to `The filter state (CursorCodec `f`) is mismatch-checked inside`.

- [ ] **Step 5: Update the two existing test files**

5a. In `tests/Kartova.SharedKernel.Tests/Pagination/CursorCodecTests.cs`, **delete** these seven test methods (they reference the removed `ic`/`ou` API):
- `Encode_then_Decode_preserves_includeDecommissioned_true`
- `Decode_legacy_cursor_without_ic_field_returns_includeDecommissioned_false`
- `Encode_then_Decode_preserves_ownerUserId_when_present`
- `Encode_with_null_ownerUserId_omits_ou_field_and_decodes_null`
- `Decode_legacy_cursor_without_ou_field_returns_null_ownerUserId`
- `Decode_throws_when_ownerUserId_is_not_canonical_guid`
- `Encode_with_includeDecommissioned_false_omits_ic_field_and_round_trips`

Then **add** these generic `f` tests (before the closing `ToBase64Url` helper):

```csharp
    [TestMethod]
    public void Encode_then_Decode_roundtrips_filter_map()
    {
        var filters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["includeDecommissioned"] = "true",
            ["ownerUserId"] = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
        };

        var encoded = CursorCodec.Encode("alpha", AnyId, SortOrder.Asc, filters);
        var decoded = CursorCodec.Decode(encoded);

        Assert.AreEqual(2, decoded.Filters.Count);
        Assert.AreEqual("true", decoded.Filters["includeDecommissioned"]);
        Assert.AreEqual("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", decoded.Filters["ownerUserId"]);
    }

    [TestMethod]
    public void Encode_with_null_filters_omits_f_field_and_decodes_empty()
    {
        var encoded = CursorCodec.Encode("alpha", AnyId, SortOrder.Asc, filters: null);

        var bytes = System.Buffers.Text.Base64Url.DecodeFromChars(encoded.AsSpan());
        var json = System.Text.Encoding.UTF8.GetString(bytes);
        StringAssert.DoesNotMatch(json, new Regex("\"f\""));

        var decoded = CursorCodec.Decode(encoded);
        Assert.AreEqual(0, decoded.Filters.Count);
    }

    [TestMethod]
    public void Encode_with_empty_filters_omits_f_field_and_decodes_empty()
    {
        var empty = new Dictionary<string, string>();

        var encoded = CursorCodec.Encode("alpha", AnyId, SortOrder.Asc, empty);

        var bytes = System.Buffers.Text.Base64Url.DecodeFromChars(encoded.AsSpan());
        var json = System.Text.Encoding.UTF8.GetString(bytes);
        StringAssert.DoesNotMatch(json, new Regex("\"f\""));

        var decoded = CursorCodec.Decode(encoded);
        Assert.AreEqual(0, decoded.Filters.Count);
    }

    [TestMethod]
    public void Decode_cursor_without_f_field_returns_empty_filters()
    {
        // { s, i, d } only — no `f`.
        var json = $$"""{"s":"alpha","i":"{{AnyId}}","d":"asc"}""";
        var encoded = ToBase64Url(json);

        var decoded = CursorCodec.Decode(encoded);

        Assert.AreEqual(0, decoded.Filters.Count);
    }

    [TestMethod]
    public void Decode_throws_when_f_field_has_non_string_value()
    {
        // `f` must be a string→string map. A non-string value is a tampering
        // signal — fail closed (JsonException → InvalidCursorException), matching
        // the rest of the codec's posture.
        var json = $$"""{"s":"alpha","i":"{{AnyId}}","d":"asc","f":{"includeDecommissioned":123}}""";
        var encoded = ToBase64Url(json);

        Assert.ThrowsExactly<InvalidCursorException>(() => CursorCodec.Decode(encoded));
    }
```

5b. In `tests/Kartova.SharedKernel.Tests/Pagination/QueryablePagingExtensionsTests.cs`, **replace** the three `ic` tests at the bottom of the file with the following. The first is a rename+rewrite to `expectedFilters`; the second reflects the corrected strict semantics (cursor-with-filter vs no-filter request is now a mismatch); the third asserts the next cursor omits filter state. Then two new tests cover the `ownerUserId` key and the round-trip.

Delete:
- `ToCursorPagedAsync_throws_CursorFilterMismatchException_when_cursor_ic_does_not_match_expected`
- `ToCursorPagedAsync_with_null_expectedIncludeDecommissioned_skips_filter_check_even_when_cursor_has_ic_true`
- `ToCursorPagedAsync_with_null_expectedIncludeDecommissioned_encodes_next_cursor_with_ic_false`

Add:

```csharp
    private static readonly string OriginIso =
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).UtcDateTime
            .ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    private static readonly Guid Row1Id = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static IReadOnlyDictionary<string, string> Filters(params (string K, string V)[] pairs)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (k, v) in pairs) d[k] = v;
        return d;
    }

    [TestMethod]
    public async Task FilterMismatch_on_changed_value_reports_filter_name_expected_and_actual()
    {
        await SeedAsync(3);
        var cursor = CursorCodec.Encode(
            OriginIso, Row1Id, SortOrder.Asc,
            Filters(("includeDecommissioned", "true")));

        var ex = await Assert.ThrowsExactlyAsync<CursorFilterMismatchException>(async () =>
            await _db.Rows.ToCursorPagedAsync(
                ByCreatedAt, SortOrder.Asc, cursor, limit: 10, x => x.Id,
                x => x.Id, CancellationToken.None,
                expectedFilters: Filters(("includeDecommissioned", "false"))));

        Assert.AreEqual("includeDecommissioned", ex.FilterName);
        Assert.AreEqual("true", ex.ExpectedValue);
        Assert.AreEqual("false", ex.ActualValue);
    }

    [TestMethod]
    public async Task FilterMismatch_reports_ownerUserId_key_when_owner_changes()
    {
        await SeedAsync(3);
        var cursor = CursorCodec.Encode(
            OriginIso, Row1Id, SortOrder.Asc,
            Filters(("includeDecommissioned", "false"), ("ownerUserId", "aaaaaaaa-0000-0000-0000-000000000001")));

        var ex = await Assert.ThrowsExactlyAsync<CursorFilterMismatchException>(async () =>
            await _db.Rows.ToCursorPagedAsync(
                ByCreatedAt, SortOrder.Asc, cursor, limit: 10, x => x.Id,
                x => x.Id, CancellationToken.None,
                expectedFilters: Filters(("includeDecommissioned", "false"), ("ownerUserId", "aaaaaaaa-0000-0000-0000-000000000002"))));

        Assert.AreEqual("ownerUserId", ex.FilterName);
    }

    [TestMethod]
    public async Task Cursor_carrying_filters_replayed_against_no_filter_request_throws_mismatch()
    {
        // Corrected strict semantics (replaces the old `ic` opt-out skip): a
        // cursor issued with filter state, replayed against a request that
        // applies no filters, is a real mismatch (the row set changed).
        await SeedAsync(5);
        var cursor = CursorCodec.Encode(
            OriginIso, Row1Id, SortOrder.Asc,
            Filters(("includeDecommissioned", "true")));

        var ex = await Assert.ThrowsExactlyAsync<CursorFilterMismatchException>(async () =>
            await _db.Rows.ToCursorPagedAsync(
                ByCreatedAt, SortOrder.Asc, cursor, limit: 10, x => x.Id,
                x => x.Id, CancellationToken.None,
                expectedFilters: null));

        Assert.AreEqual("includeDecommissioned", ex.FilterName);
        Assert.AreEqual("true", ex.ExpectedValue);
        Assert.AreEqual("(none)", ex.ActualValue);
    }

    [TestMethod]
    public async Task MatchingFilters_do_not_throw_and_return_rows()
    {
        await SeedAsync(5);
        var cursor = CursorCodec.Encode(
            OriginIso, Row1Id, SortOrder.Asc,
            Filters(("includeDecommissioned", "false")));

        var page = await _db.Rows.ToCursorPagedAsync(
            ByCreatedAt, SortOrder.Asc, cursor, limit: 10, x => x.Id,
            x => x.Id, CancellationToken.None,
            expectedFilters: Filters(("includeDecommissioned", "false")));

        Assert.IsTrue(page.Items.Any());
    }

    [TestMethod]
    public async Task NextCursor_round_trips_the_expectedFilters()
    {
        await SeedAsync(6);
        var expected = Filters(
            ("includeDecommissioned", "true"),
            ("ownerUserId", "aaaaaaaa-0000-0000-0000-000000000009"));

        var page = await _db.Rows.ToCursorPagedAsync(
            ByCreatedAt, SortOrder.Asc, cursor: null, limit: 5, x => x.Id,
            x => x.Id, CancellationToken.None,
            expectedFilters: expected);

        Assert.IsNotNull(page.NextCursor);
        var decoded = CursorCodec.Decode(page.NextCursor!);
        Assert.AreEqual("true", decoded.Filters["includeDecommissioned"]);
        Assert.AreEqual("aaaaaaaa-0000-0000-0000-000000000009", decoded.Filters["ownerUserId"]);
    }

    [TestMethod]
    public async Task No_expectedFilters_encodes_next_cursor_without_filter_state()
    {
        await SeedAsync(6);

        var page = await _db.Rows.ToCursorPagedAsync(
            ByCreatedAt, SortOrder.Asc, cursor: null, limit: 5, x => x.Id, CancellationToken.None);

        Assert.IsNotNull(page.NextCursor);
        var decoded = CursorCodec.Decode(page.NextCursor!);
        Assert.AreEqual(0, decoded.Filters.Count);
    }
```

- [ ] **Step 6: Build the solution (0 warnings gate)**

Run: `dotnet build Kartova.slnx -c Debug --nologo`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 7: Run the affected unit suites**

Run: `dotnet test tests/Kartova.SharedKernel.Tests/Kartova.SharedKernel.Tests.csproj`
Expected: PASS (CursorCodecTests, CursorFilterComparerTests, QueryablePagingExtensionsTests, CursorFilterMismatchExceptionTests all green).

Run: `dotnet test tests/Kartova.SharedKernel.AspNetCore.Tests/Kartova.SharedKernel.AspNetCore.Tests.csproj --filter "FullyQualifiedName~PagingExceptionHandlerTests"`
Expected: PASS (exception shape unchanged → mapping still green).

- [ ] **Step 8: Commit**

```bash
git add src/Kartova.SharedKernel/Pagination/CursorCodec.cs \
        src/Kartova.SharedKernel/Pagination/CursorFilterMismatchException.cs \
        src/Kartova.SharedKernel.Postgres/Pagination/QueryablePagingExtensions.cs \
        src/Modules/Catalog/Kartova.Catalog.Infrastructure/ListApplicationsHandler.cs \
        tests/Kartova.SharedKernel.Tests/Pagination/CursorCodecTests.cs \
        tests/Kartova.SharedKernel.Tests/Pagination/QueryablePagingExtensionsTests.cs
git commit -m "refactor(cursor): opaque filter map in CursorCodec; drop ic/ou domain coupling (ADR-0095)"
```

---

## Task 3: Frontend gcTime 5 → 15 min

**Files:**
- Modify: `web/src/lib/list/useCursorList.ts:8,27`

- [ ] **Step 1: Change the default and the doc comment**

In `web/src/lib/list/useCursorList.ts`, line 8 change:

```ts
  /** Garbage-collection time for cached pages (ms). Default 5 min. */
```
to:
```ts
  /** Garbage-collection time for cached pages (ms). Default 15 min. */
```

Line 27 change:
```ts
  const { queryKey, fetchPage, gcTime = 5 * 60 * 1000 } = options;
```
to:
```ts
  const { queryKey, fetchPage, gcTime = 15 * 60 * 1000 } = options;
```

- [ ] **Step 2: Run the frontend list tests**

Run: `cd web && npm test -- src/lib/list`
Expected: PASS (no test asserts the literal default; existing behavior tests stay green).

- [ ] **Step 3: Commit**

```bash
git add web/src/lib/list/useCursorList.ts
git commit -m "chore(web): bump cursor list gcTime default 5->15 min (ADR-0095)"
```

---

## Task 4: ADR-0095 amendment (preview → approve → save)

**Files:**
- Modify: `docs/architecture/decisions/ADR-0095-cursor-pagination-contract.md`

- [ ] **Step 1: Draft the amendment and PREVIEW it to the user (do not save yet)**

Per the project rule "preview ADR decisions before saving," present this text in chat and wait for approval:

```markdown
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
```

- [ ] **Step 2: On approval, append the amendment to the ADR**

Append the approved text to the end of `docs/architecture/decisions/ADR-0095-cursor-pagination-contract.md`, and update the consequences line 39 (`gcTime ... 5 min default`) to `15 min`.

- [ ] **Step 3: Commit**

```bash
git add docs/architecture/decisions/ADR-0095-cursor-pagination-contract.md
git commit -m "docs(adr-0095): amend with generic cursor filter map + gcTime 15 min"
```

---

## Task 5: Definition-of-Done verification (slice boundary)

This slice wires DB/keyset/pipeline behavior, so the full DoD applies. Run each gate; cite command + output. Do not claim "complete" until all are green or explicitly deferred with reason.

- [ ] **Step 1: Full solution build, 0 warnings**

Run: `dotnet build Kartova.slnx -c Debug --nologo`
Expected: `0 Warning(s) 0 Error(s)`.

- [ ] **Step 2: Full unit + architecture suites**

Run: `dotnet test tests/Kartova.SharedKernel.Tests/Kartova.SharedKernel.Tests.csproj`
Run: `dotnet test tests/Kartova.SharedKernel.AspNetCore.Tests/Kartova.SharedKernel.AspNetCore.Tests.csproj`
Run: `dotnet test tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj`
Expected: PASS. (Architecture suite confirms no new SharedKernel→module dependency and `[BoundedListResult]`/contracts rules still hold.)

- [ ] **Step 3: Catalog pagination integration tests (Testcontainers — exercises cursor-filter-mismatch over HTTP)**

Run: `dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj --filter "FullyQualifiedName~ListApplicationsPaginationTests"`
Expected: PASS — includeDecommissioned/ownerUserId cursor-mismatch scenarios still return 400 `cursor-filter-mismatch` with the right filter name (behavior preserved).

- [ ] **Step 4: Frontend tests**

Run: `cd web && npm test -- src/lib/list src/features/catalog`
Expected: PASS.

- [ ] **Step 5: docker compose happy + negative path (HTTP/DB/pipeline slice — required)**

Bring the stack up, exercise the real applications list:
- **Happy:** page through `GET /api/v1/catalog/applications?limit=2` across a page boundary using the returned `nextCursor`; confirm 200 + no dup/skip.
- **Negative:** take a `nextCursor` issued with `includeDecommissioned=false`, replay it with `includeDecommissioned=true`; confirm `400` + `type=.../cursor-filter-mismatch` + `filterName=includeDecommissioned`.
Capture both responses in the PR notes.

- [ ] **Step 6: /simplify on the branch diff**

Run the `/simplify` skill against the branch diff. Address should-fix reuse/quality/efficiency items or skip with a reason.

- [ ] **Step 7: Mutation feedback loop on changed files (≥80%)**

Run `/misc:mutation-sentinel` scoped to `CursorCodec.cs`, `CursorFilterComparer.cs`, `QueryablePagingExtensions.cs`, `ListApplicationsHandler.cs`; then `/misc:test-generator` to kill survivors until ≥80% (per `stryker-config.json`). Document score + any accepted survivors.

- [ ] **Step 8: PR review + deep review**

Run `/pr-review-toolkit:review-pr`, then `/deep-review` against the branch diff with this plan + spec + ADR-0095 as context. Address Blocking/Should-fix; triage nits.

- [ ] **Step 9: Update CHECKLIST**

If this work maps to a tracked story, tick it in `docs/product/CHECKLIST.md`.

---

## Self-Review

**Spec coverage:**
- §3.1 CursorCodec `f` map, drop ic/ou/GUID-parse → Task 2 Step 1. ✓
- §3.2 CursorFilterComparer pure type → Task 1. ✓
- §3.3 QueryablePagingExtensions `expectedFilters` + comparer → Task 2 Step 3. ✓
- §3.4 ListApplicationsHandler builds map → Task 2 Step 4. ✓
- §4 strict either-direction mismatch + Teams/Invitations unaffected → Task 2 Step 5b (`Cursor_carrying_filters_replayed…`, `MatchingFilters…`) + verified in Task 5 Step 3. ✓
- §5 error handling (malformed `f` fail-closed) → Task 2 Step 5a (`Decode_throws_when_f_field_has_non_string_value`). ✓
- §6 gcTime 5→15 → Task 3. ✓
- §7 ADR amendment → Task 4. ✓
- §8 testing matrix → Tasks 1, 2 Step 5, 5. ✓
- §9 DoD → Task 5. ✓

**Placeholder scan:** none — every code step has complete code; every command has expected output. ✓

**Type consistency:** `Encode(object, Guid, SortOrder, IReadOnlyDictionary<string,string>?)`, `DecodedCursor.Filters : IReadOnlyDictionary<string,string>`, `CursorFilterComparer.FindMismatch(IReadOnlyDictionary<string,string>, IReadOnlyDictionary<string,string>) → (string Name, string Expected, string Actual)?`, `ToCursorPagedAsync(..., IReadOnlyDictionary<string,string>? expectedFilters = null)` — consistent across Tasks 1–2 and both test files. The `QueryablePagingExtensionsTests` use the dual-expression overload (`idSelector`, `idExtractor`) so `expectedFilters` is reachable. ✓
