using Kartova.Organization.Contracts;
using Kartova.SharedKernel.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Kartova.Organization.Infrastructure.Tests;

[TestClass]
public sealed class UpdateOrgProfileHandlerTests
{
    private static OrganizationDbContext NewInMemory()
    {
        var opts = new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseInMemoryDatabase($"org-profile-update-{Guid.NewGuid()}")
            .Options;
        return new OrganizationDbContext(opts);
    }

    [TestMethod]
    public async Task HandleAsync_returns_NotFound_when_no_organization()
    {
        await using var db = NewInMemory();
        var sut = new UpdateOrgProfileHandler(db, Substitute.For<IAuditWriter>());
        var request = new UpdateOrgProfileRequest("New Name", "desc", "UTC");

        var result = await sut.HandleAsync(request, ifMatch: null, CancellationToken.None);

        Assert.AreEqual(UpdateOrgProfileResult.NotFound, result);
    }

    [TestMethod]
    public async Task HandleAsync_updates_aggregate_when_valid()
    {
        // Three distinct DbContexts against the same DbContextOptions: seed,
        // act, and assert each open their own context so the assert phase
        // reloads from the store rather than the EF change-tracker. This
        // makes a missing SaveChangesAsync (or a mutator that deletes it)
        // visible — without persistence the assert context would see the
        // original "Original" name.
        var opts = new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseInMemoryDatabase($"org-profile-update-{Guid.NewGuid()}")
            .Options;

        await using (var seedDb = new OrganizationDbContext(opts))
        {
            var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-05-27T10:00:00Z"));
            var org = Domain.Organization.Create("Original", clock);
            seedDb.Organizations.Add(org);
            await seedDb.SaveChangesAsync();
        }

        await using (var actDb = new OrganizationDbContext(opts))
        {
            var sut = new UpdateOrgProfileHandler(actDb, Substitute.For<IAuditWriter>());
            var request = new UpdateOrgProfileRequest("Renamed", "New description", "Europe/Oslo");
            var result = await sut.HandleAsync(request, ifMatch: null, CancellationToken.None);
            Assert.AreEqual(UpdateOrgProfileResult.Ok, result);
        }

        await using (var assertDb = new OrganizationDbContext(opts))
        {
            var reloaded = await assertDb.Organizations.SingleAsync();
            Assert.AreEqual("Renamed", reloaded.DisplayName);
            Assert.AreEqual("New description", reloaded.Description);
            Assert.AreEqual("Europe/Oslo", reloaded.DefaultTimeZone);
        }
    }

    [TestMethod]
    public async Task HandleAsync_propagates_ArgumentException_on_invalid_display_name()
    {
        await using var db = NewInMemory();
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-05-27T10:00:00Z"));
        var org = Domain.Organization.Create("Original", clock);
        db.Organizations.Add(org);
        await db.SaveChangesAsync();

        var sut = new UpdateOrgProfileHandler(db, Substitute.For<IAuditWriter>());
        var request = new UpdateOrgProfileRequest("", "desc", "UTC");

        var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(
            async () => await sut.HandleAsync(request, ifMatch: null, CancellationToken.None));
        Assert.AreEqual("displayName", ex.ParamName);
    }
}
