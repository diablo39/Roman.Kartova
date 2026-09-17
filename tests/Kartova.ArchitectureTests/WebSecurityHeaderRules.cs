using System.IO;

namespace Kartova.ArchitectureTests;

/// <summary>
/// E-01.F-04.S-06b (ADR-0116 interim hardening): the web container's nginx template must ship a
/// Content-Security-Policy that shrinks the XSS surface behind the SPA's token-bearing session.
/// Landed as <c>Content-Security-Policy-Report-Only</c> first (non-breaking — the browser reports
/// violations without blocking) so the enforce-flip can follow a real-browser gate-9 pass. The
/// XSS-critical guarantee is a strict <c>script-src 'self'</c> with no <c>unsafe-inline</c>/<c>unsafe-eval</c>.
/// This drift sentinel fails loudly if the header disappears or <c>script-src</c> is weakened.
/// </summary>
[TestClass]
public sealed class WebSecurityHeaderRules
{
    private static readonly string TemplatePath =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "web", "default.conf.template");

    private static string ReadTemplate()
    {
        Assert.IsTrue(File.Exists(TemplatePath), $"web nginx template not found at {TemplatePath}");
        return File.ReadAllText(TemplatePath);
    }

    [TestMethod]
    public void WebNginxTemplate_ShipsContentSecurityPolicy()
    {
        var t = ReadTemplate();

        StringAssert.Contains(
            t, "Content-Security-Policy-Report-Only",
            "web container must send a CSP (Report-Only first — E-01.F-04.S-06b / ADR-0116).");

        // Dynamic API + KeyCloak origins are injected at container start via envsubst.
        StringAssert.Contains(
            t, "${CSP_EXTRA_ORIGINS}",
            "connect-src/frame-src/form-action must inject the per-env API + KeyCloak origins via CSP_EXTRA_ORIGINS.");

        // img-src must allow blob: — LogoUploader previews a staged upload from a URL.createObjectURL
        // (blob:) source; 'self' data: does not cover blob:, so omitting it blocks the logo preview
        // at enforce-flip. (S-06b review finding.)
        StringAssert.Contains(
            t, "img-src 'self' data: blob:",
            "img-src must allow blob: for the LogoUploader object-URL preview.");
    }

    [TestMethod]
    public void WebCsp_ScriptSrc_IsStrictSelf_NoUnsafe()
    {
        var t = ReadTemplate();

        StringAssert.Contains(
            t, "script-src 'self';",
            "script-src must be strict 'self' — the XSS-critical directive behind ADR-0116's accepted token-in-sessionStorage risk.");

        Assert.IsFalse(
            t.Contains("'unsafe-eval'", StringComparison.Ordinal),
            "CSP must not allow 'unsafe-eval' anywhere — it would defeat the script-src XSS mitigation.");

        // style-src legitimately carries 'unsafe-inline' (Scalar / react-aria inject inline styles);
        // script-src must NOT. Guard the script-src token specifically.
        Assert.IsFalse(
            t.Contains("script-src 'self' 'unsafe-inline'", StringComparison.Ordinal),
            "script-src must not allow 'unsafe-inline' — inline scripts are the primary XSS vector.");
    }

    [TestMethod]
    public void WebCsp_LocksDownObjectAndFraming()
    {
        var t = ReadTemplate();

        StringAssert.Contains(t, "object-src 'none'", "CSP must forbid plugins (object-src 'none').");
        StringAssert.Contains(t, "frame-ancestors 'none'", "CSP must forbid being framed (clickjacking) — frame-ancestors 'none'.");
        StringAssert.Contains(t, "base-uri 'self'", "CSP must pin base-uri to 'self' (base-tag injection defense).");
    }
}
