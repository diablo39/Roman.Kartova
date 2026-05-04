using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kartova.SharedKernel.Pagination;

/// <summary>
/// Encodes and decodes opaque pagination cursors per ADR-0095.
/// Wire format is base64url-encoded JSON <c>{ s, i, d }</c>:
/// <list type="bullet">
/// <item><description><c>s</c> — sort value of the boundary row (string|number|ISO-8601 string)</description></item>
/// <item><description><c>i</c> — boundary row id (Guid, tiebreaker)</description></item>
/// <item><description><c>d</c> — direction the cursor was produced under ("asc"|"desc"). The handler verifies this matches the request's <c>sortOrder</c> to detect reused cursors across a sort flip.</description></item>
/// </list>
/// </summary>
public static class CursorCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
    };

    public sealed record DecodedCursor(object SortValue, Guid Id, SortOrder Direction);

    public static string Encode(object sortValue, Guid id, SortOrder direction)
    {
        var payload = new CursorPayload(sortValue, id, direction == SortOrder.Asc ? "asc" : "desc");
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
        return new DecodedCursor(sortValue, payload.I, direction);
    }

    private static object UnwrapJsonElement(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString()!,
        JsonValueKind.Number => el.TryGetInt64(out var i) ? i : el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw new InvalidCursorException($"Unsupported cursor sort-value kind: {el.ValueKind}"),
    };

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string s)
    {
        var b64 = s.Replace('-', '+').Replace('_', '/');
        switch (b64.Length % 4)
        {
            case 2: b64 += "=="; break;
            case 3: b64 += "="; break;
            case 1: throw new FormatException("Invalid base64url length.");
        }
        return Convert.FromBase64String(b64);
    }

    private sealed record CursorPayload(
        [property: JsonPropertyName("s")] object? S,
        [property: JsonPropertyName("i")] Guid I,
        [property: JsonPropertyName("d")] string? D);
}
