using System.Text;
using Ganss.Xss;

namespace Kartova.Organization.Application;

/// <summary>
/// Logo upload validation helpers: file-format detection via magic bytes (so a
/// browser-supplied <c>Content-Type</c> can't be trusted to declare the real
/// content) and SVG sanitization (so an attacker can't smuggle JavaScript inside
/// an SVG that ends up rendered inline). PNG/JPEG are detected by their
/// well-known signature bytes; SVG is identified by an XML/SVG textual prelude
/// since SVG has no binary magic bytes. The SVG sanitizer is configured with an
/// allow-list of safe SVG elements/attributes and only the <c>data:</c> URL
/// scheme — <c>http(s):</c> external refs are stripped along with any
/// <c>&lt;script&gt;</c>, event handlers, or unknown elements.
/// </summary>
public static class LogoValidation
{
    private static readonly byte[] PngMagic = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly HtmlSanitizer _svgSanitizer = BuildSanitizer();

    /// <summary>
    /// Returns <c>true</c> when the first bytes of <paramref name="bytes"/> match
    /// the canonical magic-byte signature of the declared <paramref name="mimeType"/>.
    /// For <c>image/svg+xml</c> (which has no binary magic) the payload is
    /// inspected for an <c>&lt;?xml</c> or <c>&lt;svg</c> prelude after
    /// whitespace trimming. Unsupported mime types always return <c>false</c>.
    /// </summary>
    public static bool MagicBytesMatch(ReadOnlySpan<byte> bytes, string mimeType) => mimeType switch
    {
        "image/png" => bytes.Length >= 8 && bytes[..8].SequenceEqual(PngMagic),
        "image/jpeg" => bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
        "image/svg+xml" => IsSvgText(bytes),
        _ => false,
    };

    private static bool IsSvgText(ReadOnlySpan<byte> bytes)
    {
        var s = Encoding.UTF8.GetString(bytes);
        var trimmed = s.AsSpan().TrimStart();
        return trimmed.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("<svg", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Runs the SVG through the allow-list sanitizer and reports whether the
    /// sanitized output is "materially" different from the input. The current
    /// heuristic flags a material change when more than 20% of the input length
    /// was removed — useful as a soft signal that hostile content was stripped
    /// without rejecting cosmetically-normalized clean SVGs.
    /// </summary>
    public static (string Sanitized, bool MateriallyChanged) SanitizeSvg(string input)
    {
        var output = _svgSanitizer.Sanitize(input);
        var changeRatio = input.Length == 0 ? 0.0 : 1.0 - (double)output.Length / input.Length;
        return (output, changeRatio > 0.20);
    }

    private static HtmlSanitizer BuildSanitizer()
    {
        var s = new HtmlSanitizer();
        s.AllowedTags.Clear();
        foreach (var t in new[]
        {
            "svg", "g", "path", "rect", "circle", "ellipse", "polygon", "polyline", "line",
            "text", "defs", "use", "linearGradient", "radialGradient", "stop", "clipPath",
            "mask", "pattern",
        })
        {
            s.AllowedTags.Add(t);
        }

        s.AllowedAttributes.Clear();
        foreach (var a in new[]
        {
            "id", "class", "style", "viewBox", "d", "fill", "stroke", "stroke-width",
            "x", "y", "cx", "cy", "r", "rx", "ry", "points", "transform", "opacity",
            "width", "height", "xmlns",
        })
        {
            s.AllowedAttributes.Add(a);
        }

        s.AllowedSchemes.Clear();
        s.AllowedSchemes.Add("data");
        return s;
    }
}
