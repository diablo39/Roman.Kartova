using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.Catalog.Infrastructure;
using Kartova.SharedKernel;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Multitenancy;
using Kartova.SharedKernel.Pagination;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Kartova.Catalog.Tests;

/// <summary>
/// Unit-tier tests for the teamId and health predicates in
/// <see cref="ListServicesHandler"/> (ADR-0107). Uses the EF Core InMemory
/// provider so this runs without a database.
/// <para>
/// TeamId filter: non-empty ⇒ rows whose <c>TeamId</c> is in the supplied set.
/// Health filter: non-empty ⇒ rows whose <c>Health</c> is in the supplied set.
/// Both filters are encoded into the cursor <c>f</c>-map when non-empty.
/// Empty ⇒ no predicate (show ALL services — no ADR-0073 default-view rule applies).
/// </para>
/// </summary>
[TestClass]
public class ListServicesHandlerFilterTests
{
    private static readonly TenantId Tenant = new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"));
    private static readonly Guid Creator = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid TeamA = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid TeamB = Guid.Parse("cccccccc-0000-0000-0000-000000000002");

    private static readonly DateTimeOffset BaseTime =
        new(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);

    private static Service MakeService(string displayName, Guid teamId, int minuteOffset = 0) =>
        Service.Create(
            displayName: displayName,
            description: "test service",
            createdByUserId: Creator,
            teamId: teamId,
            endpoints: Array.Empty<ServiceEndpoint>(),
            tenantId: Tenant,
            createdAt: BaseTime.AddMinutes(minuteOffset));

    private static async Task<CatalogDbContext> BuildDbWithTwoTeamsAsync()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var seed = new CatalogDbContext(options);
        seed.Services.Add(MakeService("In Team A", TeamA, 0));
        seed.Services.Add(MakeService("In Team B", TeamB, 1));
        await seed.SaveChangesAsync();
        return new CatalogDbContext(options);
    }

    /// <summary>
    /// Seeds two services both with the default <c>HealthStatus.Unknown</c> (Health has
    /// a private setter and no domain transition method yet — promotion lands in E-15/E-16).
    /// The tests exercise the health predicate by asserting:
    /// <list type="bullet">
    /// <item><c>Health:[Unknown]</c> returns both Unknown rows (predicate includes).</item>
    /// <item><c>Health:[Healthy]</c> returns zero rows (predicate excludes Unknown).</item>
    /// </list>
    /// Both assertions kill the predicate mutant (a missing <c>Where</c> would make
    /// the <c>[Healthy]</c> filter return rows instead of zero).
    /// </summary>
    private static async Task<CatalogDbContext> BuildDbWithTwoServicesAsync()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var seed = new CatalogDbContext(options);
        seed.Services.Add(MakeService("Svc One", TeamA, 0));
        seed.Services.Add(MakeService("Svc Two", TeamA, 1));
        await seed.SaveChangesAsync();
        return new CatalogDbContext(options);
    }

    private static IUserDirectory NoOpDirectory()
    {
        var directory = Substitute.For<IUserDirectory>();
        directory.GetManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, UserDisplayInfo>());
        return directory;
    }

    private static ListServicesQuery Query(Guid[]? teamId = null, HealthStatus[]? health = null) =>
        new(ServiceSortField.DisplayName, SortOrder.Asc, Cursor: null, Limit: 50,
            TeamId: teamId ?? Array.Empty<Guid>(),
            Health: health ?? Array.Empty<HealthStatus>());

    [TestMethod]
    public async Task Handle_with_no_teamId_filter_returns_all_services()
    {
        await using var db = await BuildDbWithTwoTeamsAsync();
        var page = await new ListServicesHandler(NoOpDirectory()).Handle(Query(), db, CancellationToken.None);
        Assert.AreEqual(2, page.Items.Count, "empty teamId filter must return all services (no default-hide rule)");
    }

    [TestMethod]
    public async Task Handle_with_teamId_filters_to_that_team()
    {
        await using var db = await BuildDbWithTwoTeamsAsync();
        var page = await new ListServicesHandler(NoOpDirectory())
            .Handle(Query(teamId: new[] { TeamB }), db, CancellationToken.None);
        Assert.AreEqual(1, page.Items.Count);
        Assert.AreEqual("In Team B", page.Items.Single().DisplayName);
    }

    [TestMethod]
    public async Task Handle_with_no_health_filter_returns_all_services()
    {
        await using var db = await BuildDbWithTwoServicesAsync();
        var page = await new ListServicesHandler(NoOpDirectory()).Handle(Query(), db, CancellationToken.None);
        Assert.AreEqual(2, page.Items.Count, "empty health filter must return all services (no default-hide rule)");
    }

    [TestMethod]
    public async Task Handle_with_health_Unknown_returns_seeded_unknown_services()
    {
        // All seeded services default to Unknown; filtering to [Unknown] must return them all.
        // This confirms the predicate is correctly applied (includes matching rows).
        await using var db = await BuildDbWithTwoServicesAsync();
        var page = await new ListServicesHandler(NoOpDirectory())
            .Handle(Query(health: new[] { HealthStatus.Unknown }), db, CancellationToken.None);
        Assert.AreEqual(2, page.Items.Count, "Health:[Unknown] must return all seeded Unknown services");
    }

    [TestMethod]
    public async Task Handle_with_health_Healthy_returns_zero_rows()
    {
        // No seeded service has Health = Healthy; the predicate must exclude all rows.
        // A missing Where would let all rows through, killing this assertion on the mutant.
        await using var db = await BuildDbWithTwoServicesAsync();
        var page = await new ListServicesHandler(NoOpDirectory())
            .Handle(Query(health: new[] { HealthStatus.Healthy }), db, CancellationToken.None);
        Assert.AreEqual(0, page.Items.Count, "Health:[Healthy] must exclude all Unknown services");
    }
}
