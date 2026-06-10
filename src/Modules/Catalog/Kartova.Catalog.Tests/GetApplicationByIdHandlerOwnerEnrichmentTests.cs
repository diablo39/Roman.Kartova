using Kartova.Catalog.Application;
using Kartova.Catalog.Infrastructure;
using Kartova.SharedKernel;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

// NOTE: Alias needed — the enclosing `Kartova.Catalog` namespace contains a sibling child
// namespace `Kartova.Catalog.Application` which wins simple-name lookup for `Application`.
using DomainApplication = Kartova.Catalog.Domain.Application;

namespace Kartova.Catalog.Tests;

/// <summary>
/// Unit-tier tests for the CreatedBy-enrichment branch of <see cref="GetApplicationByIdHandler"/>
/// (slice 9 / E1 — ADR-0098, renamed slice 10 / ADR-0103). The detail endpoint uses
/// single-shot <c>IUserDirectory.GetAsync</c> rather than the list endpoint's batched
/// <c>GetManyAsync</c>, so the two paths have separate unit coverage. The
/// not-found branch is covered transitively by the existing integration tests
/// and the InMemory provider here is enough to seed/load a single row.
/// </summary>
[TestClass]
public sealed class GetApplicationByIdHandlerOwnerEnrichmentTests
{
    private static readonly TenantId Tenant = new(Guid.Parse("aaaaaaaa-2222-0000-0000-000000000001"));
    private static readonly Guid Team = Guid.Parse("cccccccc-2222-0000-0000-000000000001");
    private static readonly DateTimeOffset BaseTime =
        new(2026, 5, 27, 12, 0, 0, TimeSpan.Zero);

    private static async Task<(CatalogDbContext Db, Guid AppId, Guid CreatorId)> SeedSingleAppAsync()
    {
        var creatorId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        Guid appId;
        await using (var seed = new CatalogDbContext(options))
        {
            var app = DomainApplication.Create(
                displayName: "App For Detail Lookup",
                description: "created-by enrichment unit test",
                createdByUserId: creatorId,
                teamId: Team,
                tenantId: Tenant,
                clock: TestClocks.At(BaseTime));
            seed.Applications.Add(app);
            await seed.SaveChangesAsync();
            appId = app.Id.Value;
        }

        return (new CatalogDbContext(options), appId, creatorId);
    }

    [TestMethod]
    public async Task Handle_populates_CreatedBy_when_directory_returns_matching_user()
    {
        var (db, appId, creatorId) = await SeedSingleAppAsync();
        await using var _ = db;

        var directory = Substitute.For<IUserDirectory>();
        var displayInfo = new UserDisplayInfo(creatorId, "Bob Bridger", "bob@orga.kartova.local");
        directory.GetAsync(creatorId, Arg.Any<CancellationToken>()).Returns(displayInfo);

        var handler = new GetApplicationByIdHandler(directory);
        var resp = await handler.Handle(new GetApplicationByIdQuery(appId), db, CancellationToken.None);

        Assert.IsNotNull(resp);
        Assert.IsNotNull(resp!.CreatedBy, "CreatedBy must be populated when the directory returns a match");
        Assert.AreEqual(creatorId, resp.CreatedBy!.Id);
        Assert.AreEqual("Bob Bridger", resp.CreatedBy.DisplayName);
        Assert.AreEqual("bob@orga.kartova.local", resp.CreatedBy.Email);
        // The directory call must target the application's CreatedByUserId — a mutant that
        // looked up some other guid (e.g., Guid.Empty) would fail this Received check.
        await directory.Received(1).GetAsync(creatorId, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Handle_leaves_CreatedBy_null_when_directory_returns_null()
    {
        // User-deleted-after-application-was-registered branch: GetAsync returns null,
        // the handler must still return the application row, but with CreatedBy = null.
        var (db, appId, creatorId) = await SeedSingleAppAsync();
        await using var _ = db;

        var directory = Substitute.For<IUserDirectory>();
        directory.GetAsync(creatorId, Arg.Any<CancellationToken>()).Returns((UserDisplayInfo?)null);

        var handler = new GetApplicationByIdHandler(directory);
        var resp = await handler.Handle(new GetApplicationByIdQuery(appId), db, CancellationToken.None);

        Assert.IsNotNull(resp);
        Assert.IsNull(resp!.CreatedBy, "CreatedBy must be null when the directory has no entry for CreatedByUserId");
        Assert.AreEqual(creatorId, resp.CreatedByUserId,
            "the row's CreatedByUserId is still the original creator — only the display projection is missing");
    }

    [TestMethod]
    public async Task Handle_returns_null_without_calling_directory_when_application_does_not_exist()
    {
        // Defensive: when the app row is not visible (RLS-hidden or genuinely absent),
        // the handler must short-circuit before issuing the directory call. Avoids a
        // pointless round trip and removes a potential cross-tenant info leak vector.
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new CatalogDbContext(options);

        var directory = Substitute.For<IUserDirectory>();

        var handler = new GetApplicationByIdHandler(directory);
        var resp = await handler.Handle(new GetApplicationByIdQuery(Guid.NewGuid()), db, CancellationToken.None);

        Assert.IsNull(resp);
        await directory.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
