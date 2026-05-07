using FluentAssertions;
using Kartova.SharedKernel.AspNetCore;
using Xunit;

namespace Kartova.SharedKernel.AspNetCore.Tests;

public class VersionEncodingTests
{
    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(42u)]
    [InlineData(uint.MaxValue)]
    public void Encode_then_TryDecode_roundtrips_to_exact_value(uint original)
    {
        // Arrange
        var encoded = VersionEncoding.Encode(original);

        // Act
        var ok = VersionEncoding.TryDecode(encoded, out var decoded);

        // Assert
        ok.Should().BeTrue();
        decoded.Should().Be(original);
    }

    [Fact]
    public void Encode_zero_pins_known_base64_form()
    {
        // Arrange & Act
        var encoded = VersionEncoding.Encode(0u);

        // Assert: 4 zero bytes -> "AAAAAA==" (base64 of 0x00 0x00 0x00 0x00)
        encoded.Should().Be("AAAAAA==");
    }

    [Fact]
    public void Encode_uint_max_value_pins_known_base64_form()
    {
        // Arrange & Act
        var encoded = VersionEncoding.Encode(uint.MaxValue);

        // Assert: 4 bytes of 0xFF -> "/////w=="
        encoded.Should().Be("/////w==");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void TryDecode_returns_false_and_zeroes_version_for_null_empty_or_whitespace(string? raw)
    {
        // Arrange & Act
        var ok = VersionEncoding.TryDecode(raw!, out var version);

        // Assert
        ok.Should().BeFalse();
        version.Should().Be(0u);
    }

    [Fact]
    public void TryDecode_returns_false_when_base64_is_valid_but_length_is_not_four_bytes()
    {
        // Arrange: "AAAA" is valid base64 that decodes to 3 zero bytes (written = 3, not 4).
        // This kills the `||` -> `&&` mutant on line 23: with `&&`, TryFromBase64String returns
        // true so `!Convert.TryFromBase64String(...)` is false; with `&&`, the short-circuit
        // skips the length check and falls through, returning a bogus true. The real `||`
        // catches the wrong-length branch and returns false.
        const string raw = "AAAA";

        // Act
        var ok = VersionEncoding.TryDecode(raw, out var version);

        // Assert
        ok.Should().BeFalse();
        version.Should().Be(0u);
    }

    [Fact]
    public void TryDecode_returns_false_when_input_is_not_valid_base64()
    {
        // Arrange
        const string raw = "!!!!";

        // Act
        var ok = VersionEncoding.TryDecode(raw, out var version);

        // Assert
        ok.Should().BeFalse();
        version.Should().Be(0u);
    }
}
