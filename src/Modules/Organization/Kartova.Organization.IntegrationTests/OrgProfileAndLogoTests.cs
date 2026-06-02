using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Kartova.Organization.Contracts;
using Kartova.Organization.Infrastructure;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Kartova.Testing.Auth;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.IntegrationTests;

/// <summary>
/// Integration tests for the slice-9 org-profile + org-logo endpoints
/// (spec §4, §6.4 / §11.3 scenarios #13–#16). Run end-to-end against the
/// real KeyCloak + Postgres pair wired by the Organization fixture's
/// <c>UsesKeycloakContainer = true</c> opt-in (none of the endpoints under
/// test actually call KC — the shared fixture just keeps the suite cohesive
/// with the H1 invitation flow). DB verification uses the BYPASSRLS
/// connection because assertion code runs outside an inbound HTTP request
/// (no <c>SET LOCAL app.current_tenant_id</c>).
/// </summary>
[TestClass]
public sealed class OrgProfileAndLogoTests : OrganizationIntegrationTestBase
{
    // 1×1 white JPEG fixture: only the first 3 bytes (FF D8 FF) are
    // load-bearing for LogoValidation.MagicBytesMatch — see
    // LogoValidation.cs:40. The rest is a minimal but valid baseline JFIF
    // header so the bytes can also be decoded by an image viewer if a human
    // ever inspects the persisted blob. Used to verify the
    // upload → ETag → GET round-trip wire contract.
    private static readonly byte[] OnePxWhiteJpeg = new byte[]
    {
        0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01,
        0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0xFF, 0xDB, 0x00, 0x43,
        0x00, 0x08, 0x06, 0x06, 0x07, 0x06, 0x05, 0x08, 0x07, 0x07, 0x07, 0x09,
        0x09, 0x08, 0x0A, 0x0C, 0x14, 0x0D, 0x0C, 0x0B, 0x0B, 0x0C, 0x19, 0x12,
        0x13, 0x0F, 0x14, 0x1D, 0x1A, 0x1F, 0x1E, 0x1D, 0x1A, 0x1C, 0x1C, 0x20,
        0x24, 0x2E, 0x27, 0x20, 0x22, 0x2C, 0x23, 0x1C, 0x1C, 0x28, 0x37, 0x29,
        0x2C, 0x30, 0x31, 0x34, 0x34, 0x34, 0x1F, 0x27, 0x39, 0x3D, 0x38, 0x32,
        0x3C, 0x2E, 0x33, 0x34, 0x32, 0xFF, 0xD9,
    };

    /// <summary>
    /// Best-effort teardown: deletes the seeded organizations row for
    /// <paramref name="tenantId"/> via <see cref="KartovaApiFixture.DeleteOrganizationsForTenantAsync"/>.
    /// The <c>logo_*</c> columns live on the same row, so dropping the row removes
    /// any uploaded logo at the same time (no separate logo table to clean up).
    /// Errors go to <c>Console.Error</c> so a CI failure surfaces the cleanup gap
    /// without masking the original test failure that fired the <c>finally</c>.
    /// </summary>
    private static async Task CleanupTenantOrgAsync(Guid tenantId)
    {
        try
        {
            await Fx.DeleteOrganizationsForTenantAsync(tenantId);
        }
#pragma warning disable CA1031 // best-effort test teardown — log and continue
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(
                $"[cleanup] organizations delete failed for tenant {tenantId}: {ex.Message}");
        }
#pragma warning restore CA1031
    }

    // ---------- Scenario #14 (spec §11.3): SVG with script returns 422 -------

    [TestMethod]
    public async Task Logo_upload_with_svg_containing_script_returns_422()
    {
        var (adminEmail, tenantId) = await NewTenantAsync("logo-svg-script");
        try
        {
            var client = await Fx.CreateAuthenticatedClientAsync(
                adminEmail, new[] { KartovaRoles.OrgAdmin });

            // <svg> with <script> + <rect> — the Ganss.Xss allow-list will strip
            // the <script> element entirely, dropping well over the 20%
            // MaterialChangeThreshold and tripping the Rejected branch in
            // LogoCommands.UploadAsync. Confirms the SVG-script defense
            // (LogoValidation.SanitizeSvg) — not just any 422.
            var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\">"
                    + "<script>alert(1)</script><rect/></svg>";
            var content = new ByteArrayContent(Encoding.UTF8.GetBytes(svg));
            content.Headers.ContentType = new MediaTypeHeaderValue("image/svg+xml");

            var resp = await client.PutAsync("/api/v1/organizations/me/logo", content);
            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);

            await using var problemStream = await resp.Content.ReadAsStreamAsync();
            using var problemDoc = await JsonDocument.ParseAsync(problemStream);
            // Specific type assertion — kills mutants that swap the problem-type
            // URI for ValidationFailed or ResourceNotFound, and pins the 422
            // failure mode to LogoInvalidContent (distinct from 415's
            // UnsupportedLogoMedia and 413's LogoTooLarge).
            Assert.AreEqual(
                ProblemTypes.LogoInvalidContent,
                problemDoc.RootElement.GetProperty("type").GetString());
            // Detail surfaces the Rejected.Reason from LogoCommands.UploadAsync,
            // i.e. "SVG contained disallowed content".
            var detail = problemDoc.RootElement.GetProperty("detail").GetString();
            Assert.IsNotNull(detail);
            StringAssert.Contains(detail!, "SVG");

            // LogoCommands.UploadAsync returns Rejected BEFORE org.SetLogo, so
            // the persisted columns must remain NULL.
            var (bytes, mime, hash) = await Fx.ReadOrgLogoColumnsAsync(tenantId);
            Assert.IsNull(bytes, "Rejected upload must not persist any bytes.");
            Assert.IsNull(mime);
            Assert.IsNull(hash);
        }
        finally
        {
            await CleanupTenantOrgAsync(tenantId);
        }
    }

    // ---------- Scenario #13: JPEG upload → 200 + serve round-trip -----------

    [TestMethod]
    public async Task Logo_upload_with_jpeg_returns_200_and_serve_returns_correct_bytes_and_etag()
    {
        var (adminEmail, tenantId) = await NewTenantAsync("logo-jpeg");
        try
        {
            var client = await Fx.CreateAuthenticatedClientAsync(
                adminEmail, new[] { KartovaRoles.OrgAdmin });

            // ----- Upload -----
            var uploadContent = new ByteArrayContent(OnePxWhiteJpeg);
            uploadContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            var uploadResp = await client.PutAsync(
                "/api/v1/organizations/me/logo", uploadContent);
            Assert.AreEqual(HttpStatusCode.OK, uploadResp.StatusCode);

            var uploadBody = await uploadResp.Content.ReadFromJsonAsync<UploadLogoResponse>(
                KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(uploadBody);
            Assert.AreEqual("image/jpeg", uploadBody!.MimeType);
            // 64 hex chars = SHA-256 hex. OrgLogo.Create computes
            // Convert.ToHexString(SHA256.HashData(bytes)) so the etag is
            // uppercase hex.
            Assert.AreEqual(64, uploadBody.LogoEtag.Length,
                $"Expected 64-char SHA-256 hex etag, got '{uploadBody.LogoEtag}'.");
            Assert.IsTrue(
                uploadBody.LogoEtag.All(c =>
                    (c >= '0' && c <= '9') ||
                    (c >= 'A' && c <= 'F') ||
                    (c >= 'a' && c <= 'f')),
                $"Etag must be hex: '{uploadBody.LogoEtag}'.");

            var etag = uploadBody.LogoEtag;
            var quotedEtag = $"\"{etag}\"";

            // ----- Cache miss: GET without If-None-Match returns full body -----
            var serveResp = await client.GetAsync("/api/v1/organizations/me/logo");
            Assert.AreEqual(HttpStatusCode.OK, serveResp.StatusCode);
            var servedBytes = await serveResp.Content.ReadAsByteArrayAsync();
            CollectionAssert.AreEqual(OnePxWhiteJpeg, servedBytes,
                "Served bytes must exactly match what was uploaded.");
            Assert.AreEqual("image/jpeg",
                serveResp.Content.Headers.ContentType?.MediaType);
            // ETag is a typed property on the response headers; compare its raw
            // wire form to the quoted SHA-256 hex.
            Assert.AreEqual(quotedEtag, serveResp.Headers.ETag?.ToString());
            AssertCachePrivateMaxAge300(serveResp);
            AssertSecurityHeaders(serveResp);

            // ----- Cache hit: GET with matching If-None-Match returns 304 -----
            using (var conditionalReq = new HttpRequestMessage(
                HttpMethod.Get, "/api/v1/organizations/me/logo"))
            {
                conditionalReq.Headers.IfNoneMatch.Add(
                    new EntityTagHeaderValue(quotedEtag));
                var notModified = await client.SendAsync(conditionalReq);
                Assert.AreEqual(HttpStatusCode.NotModified, notModified.StatusCode);
                // 304 body MUST be empty per RFC 7232 §4.1.
                var emptyBody = await notModified.Content.ReadAsByteArrayAsync();
                Assert.AreEqual(0, emptyBody.Length,
                    "304 NotModified response must have an empty body.");
                Assert.AreEqual(quotedEtag, notModified.Headers.ETag?.ToString());
                AssertCachePrivateMaxAge300(notModified);
                AssertSecurityHeaders(notModified);
            }

            // ----- Cache miss with non-matching validator: full 200 returns body again -----
            using (var conditionalReq = new HttpRequestMessage(
                HttpMethod.Get, "/api/v1/organizations/me/logo"))
            {
                // Different but well-formed strong validator; the endpoint's
                // string-equality check on the inner hash will not match, so
                // the response falls through to Results.File (200).
                conditionalReq.Headers.IfNoneMatch.Add(
                    new EntityTagHeaderValue("\"0000000000000000000000000000000000000000000000000000000000000000\""));
                var stale = await client.SendAsync(conditionalReq);
                Assert.AreEqual(HttpStatusCode.OK, stale.StatusCode);
                var staleBytes = await stale.Content.ReadAsByteArrayAsync();
                CollectionAssert.AreEqual(OnePxWhiteJpeg, staleBytes,
                    "Mismatched If-None-Match must serve the full payload.");
                Assert.AreEqual(quotedEtag, stale.Headers.ETag?.ToString());
            }
        }
        finally
        {
            await CleanupTenantOrgAsync(tenantId);
        }
    }

    // ---------- Scenario #15: 300 KiB upload returns 413 ---------------------

    [TestMethod]
    public async Task Logo_upload_above_256_kb_returns_413()
    {
        var (adminEmail, tenantId) = await NewTenantAsync("logo-too-large");
        try
        {
            var client = await Fx.CreateAuthenticatedClientAsync(
                adminEmail, new[] { KartovaRoles.OrgAdmin });

            // 300 KiB of zeros — over LogoMaxBytes (256 KiB). The endpoint's
            // size check fires BEFORE LogoCommands.UploadAsync (and thus
            // before the magic-byte check), so this never needs to be a
            // structurally valid PNG. See OrganizationEndpointDelegates.cs:389.
            var oversized = new byte[300 * 1024];
            var content = new ByteArrayContent(oversized);
            content.Headers.ContentType = new MediaTypeHeaderValue("image/png");

            var resp = await client.PutAsync("/api/v1/organizations/me/logo", content);
            Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, resp.StatusCode);

            await using var problemStream = await resp.Content.ReadAsStreamAsync();
            using var problemDoc = await JsonDocument.ParseAsync(problemStream);
            // Pins the 413 failure mode to LogoTooLarge (distinct from 415's
            // UnsupportedLogoMedia and 422's LogoInvalidContent).
            Assert.AreEqual(
                ProblemTypes.LogoTooLarge,
                problemDoc.RootElement.GetProperty("type").GetString());
            var detail = problemDoc.RootElement.GetProperty("detail").GetString();
            Assert.IsNotNull(detail);
            // The delegate emits "Logo bytes must be <= {N:N0} bytes" where
            // {N:N0} is the LogoMaxBytes literal with thousands separators.
            // Match a robust substring rather than the exact culture-formatted
            // number so the assertion survives a culture flip on the host.
            StringAssert.Contains(detail!, "bytes");

            // The handler never ran — persisted columns remain NULL.
            var (bytes, mime, hash) = await Fx.ReadOrgLogoColumnsAsync(tenantId);
            Assert.IsNull(bytes, "413 short-circuit must not persist any bytes.");
            Assert.IsNull(mime);
            Assert.IsNull(hash);
        }
        finally
        {
            await CleanupTenantOrgAsync(tenantId);
        }
    }

    // ---------- Scenario #16: invalid timezone returns 400 -------------------
    //
    // Spec §11.3 #16 reads "..._returns_422" but production behavior is 400.
    // The domain Organization.UpdateProfile throws ArgumentException for an
    // unknown IANA timezone id (Organization.cs:85), which
    // DomainValidationExceptionHandler maps to RFC 7807 400 with
    // type = ProblemTypes.ValidationFailed (DomainValidationExceptionHandler.cs:41).
    // The endpoint's documented OpenAPI surface also declares
    // .ProducesProblem(StatusCodes.Status400BadRequest) (OrganizationModule.cs:55).
    // Test name reflects the implementation, not the stale spec.

    [TestMethod]
    public async Task Org_profile_update_with_invalid_timezone_returns_400()
    {
        var (adminEmail, tenantId) = await NewTenantAsync("profile-bad-tz");
        try
        {
            var client = await Fx.CreateAuthenticatedClientAsync(
                adminEmail, new[] { KartovaRoles.OrgAdmin });

            var resp = await client.PutAsJsonAsync(
                "/api/v1/organizations/me",
                new UpdateOrgProfileRequest(
                    DisplayName: "Org X",
                    Description: "test description",
                    DefaultTimeZone: "Mars/Olympus"));

            Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);

            await using var problemStream = await resp.Content.ReadAsStreamAsync();
            using var problemDoc = await JsonDocument.ParseAsync(problemStream);
            Assert.AreEqual(
                ProblemTypes.ValidationFailed,
                problemDoc.RootElement.GetProperty("type").GetString());
            // Domain throws "Unknown IANA time-zone id." with ParamName "tz".
            // The handler surfaces the param name as a field-level error key in
            // the "errors" object — assert both presence + the field name to kill
            // mutants that drop the ParamName branch.
            Assert.IsTrue(
                problemDoc.RootElement.TryGetProperty("errors", out var errors),
                "Validation 400 must expose field-level 'errors' map.");
            Assert.IsTrue(
                errors.TryGetProperty("tz", out var tzErrors),
                "Errors map must key on the ArgumentException ParamName ('tz').");
            Assert.AreEqual(JsonValueKind.Array, tzErrors.ValueKind);
            Assert.AreEqual(1, tzErrors.GetArrayLength());
            StringAssert.Contains(
                tzErrors[0].GetString() ?? "", "IANA time-zone");

            // Verify nothing was persisted: the seeded display name + UTC
            // default still stand on the org row.
            await using var db = new OrganizationDbContext(BypassOptions());
            var profile = await db.Organizations
                .Where(o => o.TenantId == new TenantId(tenantId))
                .Select(o => new { o.DisplayName, o.DefaultTimeZone, o.Description })
                .SingleAsync();
            Assert.AreEqual("Org-profile-bad-tz", profile.DisplayName);
            Assert.AreEqual("UTC", profile.DefaultTimeZone);
            Assert.IsNull(profile.Description);
        }
        finally
        {
            await CleanupTenantOrgAsync(tenantId);
        }
    }

    // ---------- MT1 (slice-9 carry-forward): unsupported Content-Type → 415 ---

    [TestMethod]
    public async Task Logo_upload_with_unsupported_content_type_returns_415()
    {
        // The endpoint enforces a fixed Content-Type allow-list
        // (image/png|jpeg|svg+xml) BEFORE LogoCommands.UploadAsync runs. A
        // request with image/gif must short-circuit at the delegate guard and
        // surface ProblemTypes.UnsupportedLogoMedia — never the 422 magic-byte
        // failure mode (which is for the *content* not matching its declared
        // type) or the 413 size-limit failure.
        var (adminEmail, tenantId) = await NewTenantAsync("logo-415-gif");
        try
        {
            var client = await Fx.CreateAuthenticatedClientAsync(
                adminEmail, new[] { KartovaRoles.OrgAdmin });

            // Any tiny payload is fine — the endpoint rejects on Content-Type
            // alone before reading the body.
            var content = new ByteArrayContent(new byte[] { 0x47, 0x49, 0x46 });
            content.Headers.ContentType = new MediaTypeHeaderValue("image/gif");

            var resp = await client.PutAsync("/api/v1/organizations/me/logo", content);
            Assert.AreEqual(HttpStatusCode.UnsupportedMediaType, resp.StatusCode);

            await using var problemStream = await resp.Content.ReadAsStreamAsync();
            using var problemDoc = await JsonDocument.ParseAsync(problemStream);
            Assert.AreEqual(
                ProblemTypes.UnsupportedLogoMedia,
                problemDoc.RootElement.GetProperty("type").GetString());

            // 415 short-circuit — handler never ran, columns must be NULL.
            var (bytes, mime, hash) = await Fx.ReadOrgLogoColumnsAsync(tenantId);
            Assert.IsNull(bytes);
            Assert.IsNull(mime);
            Assert.IsNull(hash);
        }
        finally
        {
            await CleanupTenantOrgAsync(tenantId);
        }
    }

    // ---------- MT2: declared JPEG but PNG bytes → 422 -----------------------

    [TestMethod]
    public async Task Logo_upload_declared_jpeg_with_png_bytes_returns_422()
    {
        // The endpoint passes the Content-Type allow-list (image/jpeg) but the
        // body's magic bytes are PNG (0x89 0x50 0x4E 0x47). LogoValidation.MagicBytesMatch
        // returns false; LogoCommands.UploadAsync surfaces Rejected("magic-byte mismatch").
        // The endpoint maps that to a 422 with type=LogoInvalidContent and a detail
        // line that contains "magic-byte".
        var (adminEmail, tenantId) = await NewTenantAsync("logo-422-magic");
        try
        {
            var client = await Fx.CreateAuthenticatedClientAsync(
                adminEmail, new[] { KartovaRoles.OrgAdmin });

            // PNG magic header — first 8 bytes are the spec'd PNG signature.
            var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0xDE, 0xAD };
            var content = new ByteArrayContent(pngBytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");

            var resp = await client.PutAsync("/api/v1/organizations/me/logo", content);
            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);

            await using var problemStream = await resp.Content.ReadAsStreamAsync();
            using var problemDoc = await JsonDocument.ParseAsync(problemStream);
            Assert.AreEqual(
                ProblemTypes.LogoInvalidContent,
                problemDoc.RootElement.GetProperty("type").GetString());
            var detail = problemDoc.RootElement.GetProperty("detail").GetString();
            Assert.IsNotNull(detail);
            StringAssert.Contains(detail!, "magic-byte",
                "422 detail must surface the Rejected.Reason from LogoCommands.UploadAsync.");

            // Persisted columns must remain NULL.
            var (bytes, mime, hash) = await Fx.ReadOrgLogoColumnsAsync(tenantId);
            Assert.IsNull(bytes);
            Assert.IsNull(mime);
            Assert.IsNull(hash);
        }
        finally
        {
            await CleanupTenantOrgAsync(tenantId);
        }
    }

    // ---------- MT7: 256 KiB exact boundary + 256 KiB + 1 byte ---------------

    [TestMethod]
    public async Task Logo_upload_at_exactly_256_KiB_succeeds()
    {
        // OrgLogo.Create enforces `bytes.Length > 256 * 1024 ⇒ reject`. A
        // payload at exactly 262144 bytes must therefore SUCCEED — boundary
        // value test that complements the strictly-over-the-limit case below.
        // Padding strategy: keep the JPEG magic + JFIF header bytes intact so
        // MagicBytesMatch passes, then pad with 0x00 to hit 262144 total.
        var (adminEmail, tenantId) = await NewTenantAsync("logo-256k-exact");
        try
        {
            var client = await Fx.CreateAuthenticatedClientAsync(
                adminEmail, new[] { KartovaRoles.OrgAdmin });

            var payload = new byte[262144];
            // First 3 bytes are FFD8FF — the JPEG magic LogoValidation checks.
            payload[0] = 0xFF; payload[1] = 0xD8; payload[2] = 0xFF;
            // The rest can be zero — magic-byte check is positional, not full-file.

            var content = new ByteArrayContent(payload);
            content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");

            var resp = await client.PutAsync("/api/v1/organizations/me/logo", content);
            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);

            var body = await resp.Content.ReadFromJsonAsync<UploadLogoResponse>(
                KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(body);
            Assert.AreEqual("image/jpeg", body!.MimeType);
            Assert.AreEqual(64, body.LogoEtag.Length);

            // Persistence side-check: the persisted blob matches the input length.
            var (bytes, _, _) = await Fx.ReadOrgLogoColumnsAsync(tenantId);
            Assert.IsNotNull(bytes);
            Assert.AreEqual(262144, bytes!.Length);
        }
        finally
        {
            await CleanupTenantOrgAsync(tenantId);
        }
    }

    [TestMethod]
    public async Task Logo_upload_at_256_KiB_plus_one_returns_413()
    {
        // Boundary-just-over-limit pair to MT7's success case: 262145 bytes
        // must be rejected with 413 + LogoTooLarge. The endpoint's size check
        // fires BEFORE LogoCommands.UploadAsync, so we don't need a structurally
        // valid magic header here.
        var (adminEmail, tenantId) = await NewTenantAsync("logo-256k-plus1");
        try
        {
            var client = await Fx.CreateAuthenticatedClientAsync(
                adminEmail, new[] { KartovaRoles.OrgAdmin });

            var oversized = new byte[262145];
            var content = new ByteArrayContent(oversized);
            content.Headers.ContentType = new MediaTypeHeaderValue("image/png");

            var resp = await client.PutAsync("/api/v1/organizations/me/logo", content);
            Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, resp.StatusCode);

            await using var problemStream = await resp.Content.ReadAsStreamAsync();
            using var problemDoc = await JsonDocument.ParseAsync(problemStream);
            Assert.AreEqual(
                ProblemTypes.LogoTooLarge,
                problemDoc.RootElement.GetProperty("type").GetString());

            // 413 short-circuit — handler never ran.
            var (bytes, mime, hash) = await Fx.ReadOrgLogoColumnsAsync(tenantId);
            Assert.IsNull(bytes);
            Assert.IsNull(mime);
            Assert.IsNull(hash);
        }
        finally
        {
            await CleanupTenantOrgAsync(tenantId);
        }
    }

    // ---------- helpers ------------------------------------------------------

    /// <summary>
    /// Asserts <c>Cache-Control: private, max-age=300</c> by inspecting the
    /// typed <c>CacheControlHeaderValue</c> directly. We avoid
    /// <c>ToString()</c> string comparison because
    /// <c>HttpClient</c> re-emits directives in canonical order
    /// (<c>max-age=300, private</c>), which would otherwise be a wire-equivalent
    /// but textually different match for the value the server sent.
    /// </summary>
    private static void AssertCachePrivateMaxAge300(HttpResponseMessage resp)
    {
        var cc = resp.Headers.CacheControl;
        Assert.IsNotNull(cc, "Logo response must carry a Cache-Control header.");
        Assert.IsTrue(cc!.Private, "Cache-Control must include 'private'.");
        Assert.AreEqual(
            TimeSpan.FromSeconds(300), cc.MaxAge,
            "Cache-Control max-age must be 300 seconds.");
    }

    /// <summary>
    /// Asserts the two security headers OrganizationEndpointDelegates.GetLogoAsync
    /// emits unconditionally on every 200 / 304 logo response (defense-in-depth
    /// for the SVG render path, spec §6.4). Single-purpose helper because the
    /// same set must hold on both the body-returning and the 304 path.
    /// </summary>
    private static void AssertSecurityHeaders(HttpResponseMessage resp)
    {
        Assert.IsTrue(
            resp.Headers.TryGetValues("Content-Security-Policy", out var cspValues),
            "Logo response must carry Content-Security-Policy header.");
        Assert.AreEqual(
            "default-src 'none'; style-src 'unsafe-inline'; sandbox",
            cspValues!.Single());

        Assert.IsTrue(
            resp.Headers.TryGetValues("X-Content-Type-Options", out var nosniffValues),
            "Logo response must carry X-Content-Type-Options header.");
        Assert.AreEqual("nosniff", nosniffValues!.Single());
    }
}
