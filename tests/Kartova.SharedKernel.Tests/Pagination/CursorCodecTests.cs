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
}
