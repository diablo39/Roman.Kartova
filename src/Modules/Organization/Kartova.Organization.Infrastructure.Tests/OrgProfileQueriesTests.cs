using Kartova.Organization.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Kartova.Organization.Infrastructure.Tests;

[TestClass]
public sealed class OrgProfileQueriesTests
{
    private static OrganizationDbContext NewInMemory()
    {
        var opts = new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseInMemoryDatabase($"org-profile-{Guid.NewGuid()}")
            .Options;
        return new OrganizationDbContext(opts);
    }

    [TestMethod]
    public async Task GetMyOrgAsync_returns_null_when_no_organization()
    {
        await using var db = NewInMemory();
        var sut = new OrgProfileQueries(db);

        var result = await sut.GetMyOrgAsync(CancellationToken.None);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task GetMyOrgAsync_returns_profile_with_defaults_when_no_logo()
    {
        await using var db = NewInMemory();
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-05-27T10:00:00Z"));
        var org = Domain.Organization.Create("Acme Inc", clock);
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        var sut = new OrgProfileQueries(db);
        var result = await sut.GetMyOrgAsync(CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(org.Id.Value, result!.Id);
        Assert.AreEqual("Acme Inc", result.DisplayName);
        Assert.IsNull(result.Description);
        Assert.AreEqual("UTC", result.DefaultTimeZone);
        Assert.IsNull(result.LogoEtag);
        Assert.IsNull(result.LogoMimeType);
        Assert.AreEqual(clock.GetUtcNow(), result.CreatedAt);
    }

    [TestMethod]
    public async Task GetMyOrgAsync_populates_logo_fields_when_logo_set()
    {
        await using var db = NewInMemory();
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-05-27T10:00:00Z"));
        var org = Domain.Organization.Create("Acme Inc", clock);
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var logo = OrgLogo.Create(bytes, "image/png");
        org.SetLogo(logo);
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        var sut = new OrgProfileQueries(db);
        var result = await sut.GetMyOrgAsync(CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(logo.ContentHash, result!.LogoEtag);
        Assert.AreEqual("image/png", result.LogoMimeType);
    }
}
