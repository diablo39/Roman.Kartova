using FluentAssertions;
using Kartova.SharedKernel.Pagination;
using Xunit;

namespace Kartova.SharedKernel.Tests.Pagination;

public sealed class CursorCodecTests
{
    private static readonly Guid AnyId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void Encode_then_Decode_roundtrips_string_sort_value()
    {
        var encoded = CursorCodec.Encode("alpha", AnyId, SortOrder.Asc);
        var decoded = CursorCodec.Decode(encoded);

        decoded.SortValue.Should().Be("alpha");
        decoded.Id.Should().Be(AnyId);
        decoded.Direction.Should().Be(SortOrder.Asc);
    }

    [Fact]
    public void Encode_then_Decode_roundtrips_iso8601_timestamp_string()
    {
        var encoded = CursorCodec.Encode("2026-05-04T12:34:56.789Z", AnyId, SortOrder.Desc);
        var decoded = CursorCodec.Decode(encoded);

        decoded.SortValue.Should().Be("2026-05-04T12:34:56.789Z");
        decoded.Direction.Should().Be(SortOrder.Desc);
    }

    [Fact]
    public void Encode_produces_url_safe_string()
    {
        var encoded = CursorCodec.Encode("value with spaces & symbols/+", AnyId, SortOrder.Asc);

        encoded.Should().NotContain("+");
        encoded.Should().NotContain("/");
        encoded.Should().NotContain("=");
    }

    [Fact]
    public void Decode_throws_InvalidCursorException_on_garbage_input()
    {
        var act = () => CursorCodec.Decode("not-a-valid-cursor!!!");

        act.Should().Throw<InvalidCursorException>();
    }

    [Fact]
    public void Decode_throws_InvalidCursorException_on_tampered_base64()
    {
        var encoded = CursorCodec.Encode("alpha", AnyId, SortOrder.Asc);
        var tampered = encoded[..(encoded.Length / 2)] + "X" + encoded[(encoded.Length / 2 + 1)..];

        var act = () => CursorCodec.Decode(tampered);

        act.Should().Throw<InvalidCursorException>();
    }

    [Fact]
    public void Decode_throws_InvalidCursorException_when_required_field_missing()
    {
        var malformed = Convert.ToBase64String("{\"s\":\"alpha\"}"u8.ToArray())
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var act = () => CursorCodec.Decode(malformed);

        act.Should().Throw<InvalidCursorException>();
    }

    [Fact]
    public void Encode_then_Decode_roundtrips_integer_sort_value()
    {
        var encoded = CursorCodec.Encode(42L, AnyId, SortOrder.Asc);
        var decoded = CursorCodec.Decode(encoded);

        decoded.SortValue.Should().Be(42L);
        decoded.Direction.Should().Be(SortOrder.Asc);
    }

    [Fact]
    public void Encode_then_Decode_roundtrips_double_sort_value()
    {
        var encoded = CursorCodec.Encode(3.14, AnyId, SortOrder.Desc);
        var decoded = CursorCodec.Decode(encoded);

        decoded.SortValue.Should().Be(3.14);
    }

    [Fact]
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

        var act = () => CursorCodec.Decode(encoded);

        act.Should().Throw<InvalidCursorException>("explicit JSON null in `s` must be rejected");
    }

    [Fact]
    public void Encode_produces_compact_output_without_whitespace()
    {
        // Kills mutant at line 20: WriteIndented=false mutated to true.
        // Indented JSON contains newlines and extra spaces; compact JSON does not.
        var encoded = CursorCodec.Encode("alpha", AnyId, SortOrder.Asc);

        // Decode the base64url to inspect the JSON payload directly.
        var bytes = System.Buffers.Text.Base64Url.DecodeFromChars(encoded.AsSpan());
        var json = System.Text.Encoding.UTF8.GetString(bytes);

        json.Should().NotContain("\n");
        json.Should().NotContain("  "); // two spaces — indented JSON indent
    }

    [Fact]
    public void Encode_then_Decode_roundtrips_true_bool_sort_value()
    {
        // Kills mutant at line 76: JsonValueKind.True => true mutated to => false.
        // With mutated code, decoded value would be false instead of true.
        var encoded = CursorCodec.Encode(true, AnyId, SortOrder.Asc);
        var decoded = CursorCodec.Decode(encoded);

        decoded.SortValue.Should().Be(true);
    }

    [Fact]
    public void Encode_then_Decode_roundtrips_false_bool_sort_value()
    {
        // Kills mutant at line 77: JsonValueKind.False => false mutated to => true.
        // With mutated code, decoded value would be true instead of false.
        var encoded = CursorCodec.Encode(false, AnyId, SortOrder.Asc);
        var decoded = CursorCodec.Decode(encoded);

        decoded.SortValue.Should().Be(false);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Decode_throws_when_cursor_is_empty_or_whitespace(string cursor)
    {
        var act = () => CursorCodec.Decode(cursor);

        act.Should().Throw<InvalidCursorException>().WithMessage("Cursor is empty.");
    }

    [Fact]
    public void Decode_throws_when_id_field_is_empty_guid()
    {
        var json = $$"""{"s":"alpha","i":"{{Guid.Empty}}","d":"asc"}""";
        var encoded = ToBase64Url(json);

        var act = () => CursorCodec.Decode(encoded);

        act.Should().Throw<InvalidCursorException>("Guid.Empty in `i` is treated as missing");
    }

    [Theory]
    [InlineData("up")]
    [InlineData("ASC")]
    [InlineData("")]
    [InlineData("ascending")]
    public void Decode_throws_when_direction_field_is_not_asc_or_desc(string direction)
    {
        var json = $$"""{"s":"alpha","i":"{{AnyId}}","d":"{{direction}}"}""";
        var encoded = ToBase64Url(json);

        var act = () => CursorCodec.Decode(encoded);

        act.Should().Throw<InvalidCursorException>();
    }

    [Fact]
    public void Decode_throws_when_sort_value_is_json_array()
    {
        // Kills mutant at line 79 default arm: array/object kinds must be rejected.
        // STJ deserializes object? as JsonElement when the underlying token is a structure;
        // UnwrapJsonElement's default arm is the only guard.
        var json = $$"""{"s":[1,2,3],"i":"{{AnyId}}","d":"asc"}""";
        var encoded = ToBase64Url(json);

        var act = () => CursorCodec.Decode(encoded);

        act.Should().Throw<InvalidCursorException>()
            .WithMessage("*Unsupported cursor sort-value kind*");
    }

    [Fact]
    public void Decode_throws_when_sort_value_is_json_object()
    {
        var json = $$"""{"s":{"nested":1},"i":"{{AnyId}}","d":"asc"}""";
        var encoded = ToBase64Url(json);

        var act = () => CursorCodec.Decode(encoded);

        act.Should().Throw<InvalidCursorException>()
            .WithMessage("*Unsupported cursor sort-value kind*");
    }

    [Fact]
    public void Encode_then_Decode_roundtrips_desc_direction()
    {
        // Exercises the `payload.D == "asc" ? Asc : Desc` ternary's else branch.
        var encoded = CursorCodec.Encode(42L, AnyId, SortOrder.Desc);
        var decoded = CursorCodec.Decode(encoded);

        decoded.Direction.Should().Be(SortOrder.Desc);
    }

    [Fact]
    public void Encode_then_Decode_preserves_includeDecommissioned_true()
    {
        var encoded = CursorCodec.Encode(
            sortValue: "2026-05-07T12:00:00.0000000Z",
            id: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            direction: SortOrder.Desc,
            includeDecommissioned: true);

        var decoded = CursorCodec.Decode(encoded);

        decoded.IncludeDecommissioned.Should().BeTrue();
    }

    [Fact]
    public void Decode_legacy_cursor_without_ic_field_returns_includeDecommissioned_false()
    {
        // Hand-crafted legacy cursor: { s, i, d } only, no `ic` — the shape pre-slice-6 emitted.
        var legacyJson = "{\"s\":\"2026-05-07T12:00:00.0000000Z\",\"i\":\"11111111-1111-1111-1111-111111111111\",\"d\":\"desc\"}";
        var legacyCursor = System.Buffers.Text.Base64Url.EncodeToString(System.Text.Encoding.UTF8.GetBytes(legacyJson));

        var decoded = CursorCodec.Decode(legacyCursor);

        decoded.IncludeDecommissioned.Should().BeFalse();
    }

    private static string ToBase64Url(string json) =>
        System.Buffers.Text.Base64Url.EncodeToString(System.Text.Encoding.UTF8.GetBytes(json));
}
