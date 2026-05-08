using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kartova.SharedKernel.Pagination;

/// <summary>
/// Encodes and decodes opaque pagination cursors per ADR-0095.
/// Wire format is base64url-encoded JSON <c>{ s, i, d, ic? }</c>:
/// <list type="bullet">
/// <item><description><c>s</c> — sort value of the boundary row (string|number|ISO-8601 string)</description></item>
/// <item><description><c>i</c> — boundary row id (Guid, tiebreaker)</description></item>
/// <item><description><c>d</c> — direction the cursor was produced under ("asc"|"desc"). The handler verifies this matches the request's <c>sortOrder</c> to detect reused cursors across a sort flip.</description></item>
/// <item><description><c>ic</c> — optional include-decommissioned filter state at issue time. When absent (legacy cursors from before slice 6), decodes as <c>false</c>. Mismatched against the request's <c>includeDecommissioned</c> via <see cref="CursorFilterMismatchException"/> (ADR-0073 default-view rule, slice 6).</description></item>
/// </list>
/// </summary>
public static class CursorCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public sealed record DecodedCursor(object SortValue, Guid Id, SortOrder Direction, bool IncludeDecommissioned);

    public static string Encode(object sortValue, Guid id, SortOrder direction, bool includeDecommissioned = false)
    {
        // ic is intentionally omitted from the JSON when false to keep cursors short
        // and to remain forward-compatible with future filter dimensions: legacy
        // decoders that don't know the field treat it as default-false.
        var payload = new CursorPayload(
            sortValue,
            id,
            direction == SortOrder.Asc ? "asc" : "desc",
            includeDecommissioned ? true : (bool?)null);
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
        // Legacy cursors from pre-slice-6 omit `ic`; default to false so existing
        // in-flight clients keep paging without breaking on the contract change.
        var includeDecommissioned = payload.Ic ?? false;
        return new DecodedCursor(sortValue, payload.I, direction, includeDecommissioned);
    }

    private static object UnwrapJsonElement(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString()!,
        JsonValueKind.Number =>
            // The (object) cast on the long arm prevents C# `?:` from finding the
            // common-type `double` between `long` and `double` and silently widening
            // `i` to `42.0`. Without it, every integer-cursor sort value decodes as
            // System.Double instead of Int64.
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
        [property: JsonPropertyName("ic")] bool? Ic);
}
