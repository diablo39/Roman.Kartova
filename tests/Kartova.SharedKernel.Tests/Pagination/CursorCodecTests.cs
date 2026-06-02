using System.Text.RegularExpressions;
using Kartova.SharedKernel.Pagination;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kartova.SharedKernel.Tests.Pagination;

[TestClass]
public sealed class CursorCodecTests
{
    private static readonly Guid AnyId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [TestMethod]
    public void Encode_then_Decode_roundtrips_string_sort_value()
    {
        var encoded = CursorCodec.Encode("alpha", AnyId, SortOrder.Asc);
        var decoded = CursorCodec.Decode(encoded);

        Assert.AreEqual("alpha", decoded.SortValue);
        Assert.AreEqual(AnyId, decoded.Id);
        Assert.AreEqual(SortOrder.Asc, decoded.Direction);
    }

    [TestMethod]
    public void Encode_then_Decode_roundtrips_iso8601_timestamp_string()
    {
        var encoded = CursorCodec.Encode("2026-05-04T12:34:56.789Z", AnyId, SortOrder.Desc);
        var decoded = CursorCodec.Decode(encoded);

        Assert.AreEqual("2026-05-04T12:34:56.789Z", decoded.SortValue);
        Assert.AreEqual(SortOrder.Desc, decoded.Direction);
    }

    [TestMethod]
    public void Encode_produces_url_safe_string()
    {
        var encoded = CursorCodec.Encode("value with spaces & symbols/+", AnyId, SortOrder.Asc);

        StringAssert.DoesNotMatch(encoded, new Regex(@"\+"));
        StringAssert.DoesNotMatch(encoded, new Regex("/"));
        StringAssert.DoesNotMatch(encoded, new Regex("="));
    }

    [TestMethod]
    public void Decode_throws_InvalidCursorException_on_garbage_input()
    {
        Assert.ThrowsExactly<InvalidCursorException>(() => CursorCodec.Decode("not-a-valid-cursor!!!"));
    }

    [TestMethod]
    public void Decode_throws_InvalidCursorException_on_tampered_base64()
    {
        var encoded = CursorCodec.Encode("alpha", AnyId, SortOrder.Asc);
        var tampered = encoded[..(encoded.Length / 2)] + "X" + encoded[(encoded.Length / 2 + 1)..];

        Assert.ThrowsExactly<InvalidCursorException>(() => CursorCodec.Decode(tampered));
    }

    [TestMethod]
    public void Decode_throws_InvalidCursorException_when_required_field_missing()
    {
        var malformed = Convert.ToBase64String("{\"s\":\"alpha\"}"u8.ToArray())
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        Assert.ThrowsExactly<InvalidCursorException>(() => CursorCodec.Decode(malformed));
    }

    [TestMethod]
    public void Encode_then_Decode_roundtrips_integer_sort_value()
    {
        var encoded = CursorCodec.Encode(42L, AnyId, SortOrder.Asc);
        var decoded = CursorCodec.Decode(encoded);

        Assert.IsInstanceOfType<long>(decoded.SortValue);
        Assert.AreEqual(42L, decoded.SortValue);
        Assert.AreEqual(SortOrder.Asc, decoded.Direction);
    }

    [TestMethod]
    public void Encode_then_Decode_roundtrips_double_sort_value()
    {
        var encoded = CursorCodec.Encode(3.14, AnyId, SortOrder.Desc);
        var decoded = CursorCodec.Decode(encoded);

        Assert.IsInstanceOfType<double>(decoded.SortValue);
        Assert.AreEqual(3.14, decoded.SortValue);
    }

    [TestMethod]
    public void Decode_throws_when_s_field_is_explicit_json_null()
    {
        // Hand-craft `{"s":null,"i":"<guid>","d":"asc"}`.
        // System.Text.Json deserializes a JSON null token for an `object?` property as C# null,
        // so this is caught by the outer null guard rather than UnwrapJsonElement's Null arm.
        // The UnwrapJsonElement Null arm guards against future STJ behaviour changes.
        // Both paths are fail-closed: explicit JSON null in `s` must always be rejected.
        var json = $$"""{"s":null,"i":"{{AnyId}}","d":"asc"}""";
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        // explicit JSON null in `s` must be rejected
        Assert.ThrowsExactly<InvalidCursorException>(() => CursorCodec.Decode(encoded));
    }

    [TestMethod]
    public void Encode_produces_compact_output_without_whitespace()
    {
        // Kills mutant at line 20: WriteIndented=false mutated to true.
        // Indented JSON contains newlines and extra spaces; compact JSON does not.
        var encoded = CursorCodec.Encode("alpha", AnyId, SortOrder.Asc);

        // Decode the base64url to inspect the JSON payload directly.
        var bytes = System.Buffers.Text.Base64Url.DecodeFromChars(encoded.AsSpan());
        var json = System.Text.Encoding.UTF8.GetString(bytes);

        StringAssert.DoesNotMatch(json, new Regex("\n"));
        StringAssert.DoesNotMatch(json, new Regex("  ")); // two spaces — indented JSON indent
    }

    [TestMethod]
    public void Encode_then_Decode_roundtrips_true_bool_sort_value()
    {
        // Kills mutant: `JsonValueKind.True => true` arm mutated to `=> false`.
        // With mutated code, decoded value would be false instead of true.
        var encoded = CursorCodec.Encode(true, AnyId, SortOrder.Asc);
        var decoded = CursorCodec.Decode(encoded);

        Assert.AreEqual(true, decoded.SortValue);
    }

    [TestMethod]
    public void Encode_then_Decode_roundtrips_false_bool_sort_value()
    {
        // Kills mutant: `JsonValueKind.False => false` arm mutated to `=> true`.
        // With mutated code, decoded value would be true instead of false.
        var encoded = CursorCodec.Encode(false, AnyId, SortOrder.Asc);
        var decoded = CursorCodec.Decode(encoded);

        Assert.AreEqual(false, decoded.SortValue);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("\t")]
    public void Decode_throws_when_cursor_is_empty_or_whitespace(string cursor)
    {
        var ex = Assert.ThrowsExactly<InvalidCursorException>(() => CursorCodec.Decode(cursor));
        Assert.AreEqual("Cursor is empty.", ex.Message);
    }

    [TestMethod]
    public void Decode_throws_when_id_field_is_empty_guid()
    {
        var json = $$"""{"s":"alpha","i":"{{Guid.Empty}}","d":"asc"}""";
        var encoded = ToBase64Url(json);

        // Guid.Empty in `i` is treated as missing
        Assert.ThrowsExactly<InvalidCursorException>(() => CursorCodec.Decode(encoded));
    }

    [TestMethod]
    [DataRow("up")]
    [DataRow("ASC")]
    [DataRow("")]
    [DataRow("ascending")]
    public void Decode_throws_when_direction_field_is_not_asc_or_desc(string direction)
    {
        var json = $$"""{"s":"alpha","i":"{{AnyId}}","d":"{{direction}}"}""";
        var encoded = ToBase64Url(json);

        Assert.ThrowsExactly<InvalidCursorException>(() => CursorCodec.Decode(encoded));
    }

    [TestMethod]
    public void Decode_throws_when_sort_value_is_json_array()
    {
        // Kills mutant: default arm in `UnwrapJsonElement` switch — array/object kinds must be rejected.
        // STJ deserializes object? as JsonElement when the underlying token is a structure;
        // UnwrapJsonElement's default arm is the only guard.
        var json = $$"""{"s":[1,2,3],"i":"{{AnyId}}","d":"asc"}""";
        var encoded = ToBase64Url(json);

        var ex = Assert.ThrowsExactly<InvalidCursorException>(() => CursorCodec.Decode(encoded));
        StringAssert.Matches(ex.Message, new Regex("Unsupported cursor sort-value kind"));
    }

    [TestMethod]
    public void Decode_throws_when_sort_value_is_json_object()
    {
        var json = $$"""{"s":{"nested":1},"i":"{{AnyId}}","d":"asc"}""";
        var encoded = ToBase64Url(json);

        var ex = Assert.ThrowsExactly<InvalidCursorException>(() => CursorCodec.Decode(encoded));
        StringAssert.Matches(ex.Message, new Regex("Unsupported cursor sort-value kind"));
    }

    [TestMethod]
    public void Encode_then_Decode_roundtrips_desc_direction()
    {
        // Exercises the `payload.D == "asc" ? Asc : Desc` ternary's else branch.
        var encoded = CursorCodec.Encode(42L, AnyId, SortOrder.Desc);
        var decoded = CursorCodec.Decode(encoded);

        Assert.AreEqual(SortOrder.Desc, decoded.Direction);
    }

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
        var json = $$"""{"s":"alpha","i":"{{AnyId}}","d":"asc"}""";
        var encoded = ToBase64Url(json);

        var decoded = CursorCodec.Decode(encoded);

        Assert.AreEqual(0, decoded.Filters.Count);
    }

    [TestMethod]
    public void Decode_throws_when_f_field_has_non_string_value()
    {
        // `f` must be a string→string map. A non-string value is a tampering
        // signal — fail closed (JsonException → InvalidCursorException).
        var json = $"{{\"s\":\"alpha\",\"i\":\"{AnyId}\",\"d\":\"asc\",\"f\":{{\"includeDecommissioned\":123}}}}";
        var encoded = ToBase64Url(json);

        Assert.ThrowsExactly<InvalidCursorException>(() => CursorCodec.Decode(encoded));
    }

    private static string ToBase64Url(string json) =>
        System.Buffers.Text.Base64Url.EncodeToString(System.Text.Encoding.UTF8.GetBytes(json));
}
