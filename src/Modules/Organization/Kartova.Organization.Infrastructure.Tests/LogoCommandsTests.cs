using System.Security.Cryptography;
using System.Text;
using Kartova.Organization.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Kartova.Organization.Infrastructure.Tests;

[TestClass]
public sealed class LogoCommandsTests
{
    // Canonical PNG magic-byte signature shared by all "valid PNG" cases. Mirrors
    // the literal in LogoValidation.PngMagic (verified by D3 tests).
    private static readonly byte[] PngMagic =
        { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private static DbContextOptions<OrganizationDbContext> NewOptions() =>
        new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseInMemoryDatabase($"org-logo-{Guid.NewGuid()}")
            .Options;

    private static OrganizationDbContext NewInMemory() => new(NewOptions());

    private static byte[] ValidPngBytes()
    {
        // 8-byte magic + 1 trailing byte — minimum payload that passes both
        // MagicBytesMatch (length >= 8) and OrgLogo.Create (length >= 1).
        var bytes = new byte[PngMagic.Length + 1];
        Array.Copy(PngMagic, bytes, PngMagic.Length);
        bytes[^1] = 0x00;
        return bytes;
    }

    [TestMethod]
    public async Task UploadAsync_rejects_when_magic_bytes_mismatch()
    {
        await using var db = NewInMemory();
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-05-27T10:00:00Z"));
        db.Organizations.Add(Domain.Organization.Create("Acme", clock));
        await db.SaveChangesAsync();

        var sut = new LogoCommands(db);
        var notAPng = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 };

        var result = await sut.UploadAsync(notAPng, "image/png", CancellationToken.None);

        var rejected = result as UploadLogoResult.Rejected;
        Assert.IsNotNull(rejected);
        Assert.AreEqual("magic-byte mismatch", rejected!.Reason);
    }

    [TestMethod]
    public async Task UploadAsync_returns_NotFound_when_no_organization()
    {
        await using var db = NewInMemory();
        var sut = new LogoCommands(db);

        var result = await sut.UploadAsync(ValidPngBytes(), "image/png", CancellationToken.None);

        Assert.IsInstanceOfType<UploadLogoResult.NotFound>(result);
    }

    [TestMethod]
    public async Task UploadAsync_accepts_valid_png_and_returns_etag()
    {
        // Three-context persistence pattern: a missing SaveChangesAsync would
        // leave the assertDb with no Logo, surfacing the mutation.
        var opts = NewOptions();

        await using (var seedDb = new OrganizationDbContext(opts))
        {
            var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-05-27T10:00:00Z"));
            seedDb.Organizations.Add(Domain.Organization.Create("Acme", clock));
            await seedDb.SaveChangesAsync();
        }

        var bytes = ValidPngBytes();
        var expectedHash = Convert.ToHexString(SHA256.HashData(bytes));
        UploadLogoResult.Accepted accepted;

        await using (var actDb = new OrganizationDbContext(opts))
        {
            var sut = new LogoCommands(actDb);
            var result = await sut.UploadAsync(bytes, "image/png", CancellationToken.None);
            accepted = (result as UploadLogoResult.Accepted)!;
            Assert.IsNotNull(accepted, "Expected Accepted result");
            Assert.AreEqual(expectedHash, accepted.Etag);
            Assert.AreEqual("image/png", accepted.MimeType);
        }

        await using (var assertDb = new OrganizationDbContext(opts))
        {
            var reloaded = await assertDb.Organizations.SingleAsync();
            Assert.IsNotNull(reloaded.Logo);
            Assert.AreEqual(expectedHash, reloaded.Logo!.ContentHash);
            Assert.AreEqual("image/png", reloaded.Logo.MimeType);
            CollectionAssert.AreEqual(bytes, reloaded.Logo.Bytes);
        }
    }

    [TestMethod]
    public async Task UploadAsync_rejects_svg_with_script()
    {
        await using var db = NewInMemory();
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-05-27T10:00:00Z"));
        db.Organizations.Add(Domain.Organization.Create("Acme", clock));
        await db.SaveChangesAsync();

        var sut = new LogoCommands(db);
        // Mirrors the hostile-SVG fixture used by LogoValidationTests
        // (D3 / src/Modules/Organization/Kartova.Organization.Tests/LogoValidationTests.cs):
        // short enough that stripping the <script> tag breaches the 20%
        // material-change threshold.
        var hostileSvg = "<svg><script>alert(1)</script><circle r=\"5\"/></svg>";
        var bytes = Encoding.UTF8.GetBytes(hostileSvg);

        var result = await sut.UploadAsync(bytes, "image/svg+xml", CancellationToken.None);

        var rejected = result as UploadLogoResult.Rejected;
        Assert.IsNotNull(rejected);
        Assert.AreEqual("SVG contained disallowed content", rejected!.Reason);
    }

    [TestMethod]
    public async Task UploadAsync_accepts_clean_svg()
    {
        var opts = NewOptions();

        await using (var seedDb = new OrganizationDbContext(opts))
        {
            var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-05-27T10:00:00Z"));
            seedDb.Organizations.Add(Domain.Organization.Create("Acme", clock));
            await seedDb.SaveChangesAsync();
        }

        // Minimal clean SVG — fits comfortably inside the sanitizer's 80%-of-input
        // material-change threshold so it is accepted as Accepted, not Rejected.
        var cleanSvg = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><circle cx="5" cy="5" r="4" fill="red"/><circle cx="5" cy="5" r="3" fill="blue"/></svg>""";
        var bytes = Encoding.UTF8.GetBytes(cleanSvg);

        await using (var actDb = new OrganizationDbContext(opts))
        {
            var sut = new LogoCommands(actDb);
            var result = await sut.UploadAsync(bytes, "image/svg+xml", CancellationToken.None);
            Assert.IsInstanceOfType<UploadLogoResult.Accepted>(result);
        }

        await using (var assertDb = new OrganizationDbContext(opts))
        {
            var reloaded = await assertDb.Organizations.SingleAsync();
            Assert.IsNotNull(reloaded.Logo);
            Assert.AreEqual("image/svg+xml", reloaded.Logo!.MimeType);
            // Bytes may differ slightly from input (sanitizer re-serialization); just
            // confirm something non-empty was stored and the hash matches the
            // re-serialized payload.
            Assert.IsTrue(reloaded.Logo.Bytes.Length > 0);
            Assert.AreEqual(
                Convert.ToHexString(SHA256.HashData(reloaded.Logo.Bytes)),
                reloaded.Logo.ContentHash);
        }
    }

    [TestMethod]
    public async Task ClearAsync_returns_false_when_no_organization()
    {
        await using var db = NewInMemory();
        var sut = new LogoCommands(db);

        var ok = await sut.ClearAsync(CancellationToken.None);

        Assert.IsFalse(ok);
    }

    [TestMethod]
    public async Task ClearAsync_returns_true_and_clears_logo()
    {
        var opts = NewOptions();

        await using (var seedDb = new OrganizationDbContext(opts))
        {
            var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-05-27T10:00:00Z"));
            var org = Domain.Organization.Create("Acme", clock);
            org.SetLogo(OrgLogo.Create(ValidPngBytes(), "image/png"));
            seedDb.Organizations.Add(org);
            await seedDb.SaveChangesAsync();
        }

        await using (var actDb = new OrganizationDbContext(opts))
        {
            var sut = new LogoCommands(actDb);
            var ok = await sut.ClearAsync(CancellationToken.None);
            Assert.IsTrue(ok);
        }

        await using (var assertDb = new OrganizationDbContext(opts))
        {
            var reloaded = await assertDb.Organizations.SingleAsync();
            Assert.IsNull(reloaded.Logo);
        }
    }

    [TestMethod]
    public async Task GetServeDataAsync_returns_null_when_no_logo()
    {
        await using var db = NewInMemory();
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-05-27T10:00:00Z"));
        db.Organizations.Add(Domain.Organization.Create("Acme", clock));
        await db.SaveChangesAsync();

        var sut = new LogoCommands(db);
        var result = await sut.GetServeDataAsync(CancellationToken.None);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task GetServeDataAsync_returns_data_when_logo_set()
    {
        await using var db = NewInMemory();
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-05-27T10:00:00Z"));
        var org = Domain.Organization.Create("Acme", clock);
        var bytes = ValidPngBytes();
        var logo = OrgLogo.Create(bytes, "image/png");
        org.SetLogo(logo);
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        var sut = new LogoCommands(db);
        var result = await sut.GetServeDataAsync(CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual("image/png", result!.MimeType);
        Assert.AreEqual(logo.ContentHash, result.ContentHash);
        CollectionAssert.AreEqual(bytes, result.Bytes);
    }
}
