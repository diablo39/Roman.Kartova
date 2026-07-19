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
    /// Threshold above which a sanitized SVG is considered "materially changed"
    /// — i.e. the caller should reject the upload because more than 20% of the
    /// original byte length was stripped. Tuned conservatively: a clean SVG
    /// rarely loses &gt;20% to whitespace normalization, while an SVG carrying
    /// <c>&lt;script&gt;</c> or external <c>href</c>s typically loses far more.
    /// </summary>
    private const double MaterialChangeThreshold = 0.20;

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

    private static ReadOnlySpan<byte> XmlPreludeUtf8 => "<?xml"u8;
    private static ReadOnlySpan<byte> SvgOpenUtf8 => "<svg"u8;

    /// <summary>
    /// Allocation-free SVG-prelude detector. Looks for either the XML declaration
    /// (<c>&lt;?xml</c>) or the SVG root tag (<c>&lt;svg</c>) after stripping any
    /// leading UTF-8 BOM and ASCII whitespace. The match is case-insensitive
    /// because XML names like "&lt;SVG" or "&lt;?XML" are syntactically valid.
    /// </summary>
    private static bool IsSvgText(ReadOnlySpan<byte> bytes)
    {
        // Skip leading ASCII whitespace + optional UTF-8 BOM. SVG payloads in the wild often
        // include a BOM before "<?xml ..." (RFC 7303 allows it, browsers serve it).
        var span = bytes;

        // Strip a UTF-8 BOM (0xEF 0xBB 0xBF) if present.
        if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF)
            span = span[3..];

        // Trim leading ASCII whitespace bytes (space, tab, LF, CR).
        while (span.Length > 0 && IsAsciiWhitespace(span[0])) span = span[1..];

        return StartsWithCaseInsensitiveAscii(span, XmlPreludeUtf8)
            || StartsWithCaseInsensitiveAscii(span, SvgOpenUtf8);
    }

    private static bool IsAsciiWhitespace(byte b) => b == 0x20 || b == 0x09 || b == 0x0A || b == 0x0D;

    // ASCII case-insensitive byte-span StartsWith. Sufficient because both XML
    // prelude and SVG tag names are pure ASCII letters; mixed-case ("<SVG", "<?XML")
    // must compare equal to lower-case.
    private static bool StartsWithCaseInsensitiveAscii(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        if (haystack.Length < needle.Length) return false;
        for (int i = 0; i < needle.Length; i++)
        {
            var h = haystack[i];
            var n = needle[i];
            // Fold ASCII A-Z to a-z by setting bit 0x20 if the byte is an uppercase letter.
            if (h is >= (byte)'A' and <= (byte)'Z') h |= 0x20;
            if (n is >= (byte)'A' and <= (byte)'Z') n |= 0x20;
            if (h != n) return false;
        }
        return true;
    }

    /// <summary>
    /// Maximum sanitization passes before a payload that will not stabilize is
    /// declared hostile. See <see cref="SanitizeSvg"/> for the fixpoint rationale;
    /// a clean SVG stabilizes on the second pass (the sanitizer is idempotent on
    /// already-clean markup), so the bound is only ever hit by mutating input.
    /// </summary>
    private const int MaxSanitizePasses = 4;

    /// <summary>
    /// Runs the SVG through the allow-list sanitizer <em>to a fixpoint</em> and
    /// reports whether the result is "materially" different from the input. The
    /// heuristic flags a material change when more than 20% of the input length
    /// was removed — a soft signal that hostile content was stripped without
    /// rejecting cosmetically-normalized clean SVGs.
    /// <para>
    /// Defence-in-depth against mutation-XSS (the CVE-2026-54570 class): the
    /// underlying HTML parser can serialize a DOM that a browser re-parses into
    /// <em>different</em> markup, letting a payload survive a single pass. Re-running
    /// the sanitizer until its output stops changing collapses that gap — any markup
    /// that only materializes on re-parse is stripped by the next pass. Input that
    /// will not stabilize within <see cref="MaxSanitizePasses"/> is treated as
    /// hostile (<c>MateriallyChanged = true</c>), so the caller rejects it. This
    /// guards the whole mutation class independently of the parser fix, on top of
    /// the tight element/attribute allow-list — which already removes the concrete
    /// CVE vectors (MathML <c>&lt;annotation-xml encoding=…&gt;</c>, SVG
    /// <c>&lt;foreignObject&gt;</c>).
    /// </para>
    /// </summary>
    public static (string Sanitized, bool MateriallyChanged) SanitizeSvg(string input)
    {
        var current = _svgSanitizer.Sanitize(input);
        var stabilized = false;
        for (var pass = 1; pass < MaxSanitizePasses; pass++)
        {
            var next = _svgSanitizer.Sanitize(current);
            if (string.Equals(next, current, StringComparison.Ordinal))
            {
                stabilized = true;
                break;
            }
            current = next;
        }

        var changeRatio = input.Length == 0 ? 0.0 : 1.0 - (double)current.Length / input.Length;
        return (current, !stabilized || changeRatio > MaterialChangeThreshold);
    }

    private static HtmlSanitizer BuildSanitizer()
    {
        var s = new HtmlSanitizer();
        s.AllowedTags.Clear();
        // Pure SVG shape/gradient primitives only. The HTML/MathML integration-point elements
        // that enable mutation-XSS parser confusion — SVG <foreignObject>, MathML <math> and
        // <annotation-xml> (with encoding="text/html"|"application/xhtml+xml") — are deliberately
        // absent, so they are stripped. This is the primary defence for the CVE-2026-54570 class;
        // SanitizeSvg's fixpoint loop backs it up parser-independently.
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
        // "style" is intentionally NOT on the allow-list: Ganss.Xss filters
        // style="..." values via its (broad, default) AllowedCssProperties list,
        // which would inherit a default CSS surface area we don't audit. SVG
        // styling is fully expressible through explicit attributes (fill, stroke,
        // stroke-width, opacity, transform), so dropping "style" closes that
        // allow-list inheritance vector without losing functionality.
        foreach (var a in new[]
        {
            "id", "class", "viewBox", "d", "fill", "stroke", "stroke-width",
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
