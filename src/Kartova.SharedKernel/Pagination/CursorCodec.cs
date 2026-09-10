using System.Collections.Frozen;
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

    public sealed record DecodedCursor(
        object SortValue,
        Guid Id,
        SortOrder Direction,
        IReadOnlyDictionary<string, string> Filters);

    public static string Encode(
        object? sortValue,
        Guid id,
        SortOrder direction,
        IReadOnlyDictionary<string, string>? filters = null)
    {
        // `f` is omitted from the JSON when no filters apply (null/empty) to keep
        // cursors short and forward-compatible: a decoder that sees no `f` treats
        // it as "no filter state". The codec never interprets the keys/values —
        // they are opaque, owned by the calling module. The map is serialized
        // synchronously below and never retained, so it is passed through without
        // a defensive copy.
        //
        // A NULL boundary sort key (only possible for an IsNullable sort — TD-001) is
        // encoded as `n:true` with `s` omitted, so paging can resume from inside a NULLS
        // FIRST/LAST block instead of the codec rejecting the null. Backward compatible:
        // non-nullable sorts never pass null here, so `n` is absent from their cursors and
        // an old cursor (no `n`) decodes exactly as before.
        var isNull = sortValue is null or CursorNullSortValue;
        var payload = new CursorPayload(
            isNull ? null : sortValue,
            id,
            direction == SortOrder.Asc ? "asc" : "desc",
            filters is { Count: > 0 } ? filters : null,
            isNull ? true : null);
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

        // A null-key cursor (n:true — TD-001) legitimately omits `s`; every other cursor
        // still requires it. Guard `s` only when the boundary key is not the null sentinel.
        var isNullKey = payload?.N == true;
        if (payload is null
            || (!isNullKey && payload.S is null)
            || payload.I == Guid.Empty
            || payload.D is not "asc" and not "desc")
        {
            throw new InvalidCursorException("Cursor is missing required fields.");
        }

        var direction = payload.D == "asc" ? SortOrder.Asc : SortOrder.Desc;
        object sortValue = isNullKey
            ? CursorNullSortValue.Instance
            : payload.S is JsonElement el ? UnwrapJsonElement(el) : payload.S!;
        // Cursors with no filter state (or cursors issued before any filter
        // existed) omit `f`; decode as an empty map so consumers never null-check.
        // Materialize a present map into a FrozenDictionary so DecodedCursor.Filters
        // is a truly immutable view — the STJ-deserialized dictionary is otherwise
        // castable back to IDictionary and mutable (defensive-immutability convention,
        // cf. OrgLogo.Bytes on this branch).
        IReadOnlyDictionary<string, string> filters = payload.F is { Count: > 0 } f
            ? f.ToFrozenDictionary(StringComparer.Ordinal)
            : FrozenDictionary<string, string>.Empty;
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
        [property: JsonPropertyName("f")] IReadOnlyDictionary<string, string>? F,
        [property: JsonPropertyName("n")] bool? N = null);
}

/// <summary>
/// Sentinel returned by <see cref="CursorCodec.Decode"/> as
/// <see cref="CursorCodec.DecodedCursor.SortValue"/> when the boundary row's sort key was
/// <c>NULL</c> (an <see cref="Pagination.SortSpec{TEntity}.IsNullable"/> sort whose page boundary
/// fell inside the NULLS block — TD-001). Keeps <c>SortValue</c> non-null while unambiguously
/// signalling "the boundary key is null" to the keyset predicate builder, distinct from any real
/// value (e.g. an empty string).
/// </summary>
public sealed class CursorNullSortValue
{
    public static readonly CursorNullSortValue Instance = new();
    private CursorNullSortValue() { }
}
