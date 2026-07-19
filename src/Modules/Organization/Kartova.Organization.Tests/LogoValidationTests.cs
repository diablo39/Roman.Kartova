using System.Text;
using Kartova.Organization.Application;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kartova.Organization.Tests;

/// <summary>
/// Tests for <see cref="LogoValidation"/> magic-byte detection and SVG sanitization.
/// Magic-byte tests include exact-boundary length cases so mutation operators that
/// flip <c>&gt;=</c> to <c>&gt;</c> (or vice-versa) on the length guards have a
/// failing observer. The unsupported-mime-type test kills the <c>_ =&gt; false</c>
/// arm-removal mutator on the switch expression.
/// </summary>
[TestClass]
public sealed class LogoValidationTests
{
    [TestMethod]
    public void MagicBytesMatch_png_with_correct_header_returns_true()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 };
        Assert.IsTrue(LogoValidation.MagicBytesMatch(bytes, "image/png"));
    }

    [TestMethod]
    public void MagicBytesMatch_png_with_wrong_header_returns_false()
    {
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0x00 };
        Assert.IsFalse(LogoValidation.MagicBytesMatch(bytes, "image/png"));
    }

    [TestMethod]
    public void MagicBytesMatch_png_with_exactly_8_bytes_returns_true()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        Assert.IsTrue(LogoValidation.MagicBytesMatch(bytes, "image/png"));
    }

    [TestMethod]
    public void MagicBytesMatch_png_with_7_bytes_returns_false()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A };
        Assert.IsFalse(LogoValidation.MagicBytesMatch(bytes, "image/png"));
    }

    [TestMethod]
    public void MagicBytesMatch_jpeg_with_correct_header_returns_true()
    {
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        Assert.IsTrue(LogoValidation.MagicBytesMatch(bytes, "image/jpeg"));
    }

    [TestMethod]
    public void MagicBytesMatch_svg_with_xml_prelude_returns_true()
    {
        var bytes = Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><svg></svg>");
        Assert.IsTrue(LogoValidation.MagicBytesMatch(bytes, "image/svg+xml"));
    }

    [TestMethod]
    public void MagicBytesMatch_svg_with_root_element_returns_true()
    {
        var bytes = Encoding.UTF8.GetBytes("  <svg xmlns=\"...\"/>");
        Assert.IsTrue(LogoValidation.MagicBytesMatch(bytes, "image/svg+xml"));
    }

    [TestMethod]
    public void MagicBytesMatch_returns_false_for_unsupported_mime_type()
    {
        // Valid PNG header bytes, but the mime type is unsupported — the
        // switch expression's `_ => false` arm must return false.
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        Assert.IsFalse(LogoValidation.MagicBytesMatch(bytes, "image/gif"));
    }

    [TestMethod]
    public void SanitizeSvg_strips_script_element()
    {
        var input = "<svg><script>alert(1)</script><circle r=\"5\"/></svg>";
        var (sanitized, materiallyChanged) = LogoValidation.SanitizeSvg(input);
        Assert.IsFalse(sanitized.Contains("<script", StringComparison.OrdinalIgnoreCase),
            "Sanitized output should not contain a <script tag opener.");
        Assert.IsTrue(materiallyChanged);
    }

    [TestMethod]
    public void SanitizeSvg_passes_clean_svg_unchanged()
    {
        var input = "<svg xmlns=\"http://www.w3.org/2000/svg\"><circle r=\"5\" fill=\"red\"/></svg>";
        var (sanitized, materiallyChanged) = LogoValidation.SanitizeSvg(input);
        Assert.IsFalse(materiallyChanged);
        Assert.IsTrue(sanitized.Contains("circle"));
    }

    [TestMethod]
    public void SanitizeSvg_strips_mathml_annotation_xml_integration_point()
    {
        // CVE-2026-54570 mutation-XSS vector: a MathML <annotation-xml encoding="text/html">
        // integration point is what lets a payload survive HTML parsing. The allow-list has no
        // such element, so it — and any script it smuggles — must be gone, and the upload rejected.
        var input = "<svg><annotation-xml encoding=\"text/html\"><img src=x onerror=alert(1)></annotation-xml><circle r=\"5\"/></svg>";
        var (sanitized, materiallyChanged) = LogoValidation.SanitizeSvg(input);
        Assert.IsFalse(sanitized.Contains("annotation-xml", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(sanitized.Contains("onerror", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(sanitized.Contains("<img", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(materiallyChanged);
    }

    [TestMethod]
    public void SanitizeSvg_strips_foreign_object_html_integration_point()
    {
        // <foreignObject> is the SVG→HTML integration point; it is deliberately not allow-listed.
        var input = "<svg><foreignObject><body><script>alert(1)</script></body></foreignObject><rect/></svg>";
        var (sanitized, materiallyChanged) = LogoValidation.SanitizeSvg(input);
        Assert.IsFalse(sanitized.Contains("foreignObject", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(sanitized.Contains("<script", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(materiallyChanged);
    }

    [TestMethod]
    public void SanitizeSvg_output_is_a_fixpoint_reparsing_it_is_stable()
    {
        // Defence-in-depth (the mutation-XSS class): whatever SanitizeSvg returns must itself be
        // inert — re-sanitizing the output yields the same string, with no residual markup that
        // only materializes on a second parse. This is the property the fixpoint loop guarantees.
        var input = "<svg><annotation-xml encoding=\"text/html\"><style><img src=x onerror=alert(1)></style></annotation-xml></svg>";
        var (sanitized, _) = LogoValidation.SanitizeSvg(input);
        var (twice, materiallyChanged) = LogoValidation.SanitizeSvg(sanitized);
        Assert.AreEqual(sanitized, twice, "sanitizing an already-sanitized SVG must be idempotent (fixpoint).");
        Assert.IsFalse(materiallyChanged, "a sanitized SVG must not be re-flagged as hostile on a second pass.");
    }
}
