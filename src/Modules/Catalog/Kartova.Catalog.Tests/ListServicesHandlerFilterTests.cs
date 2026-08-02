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

    /// <summary>
    /// Returns an <see cref="ISystemMembershipEnricher"/> stub whose lookup always resolves to an
    /// empty dictionary. These tests exercise the teamId/health/systemId predicate paths only —
    /// stubbing this port keeps them off the EF Core InMemory provider's ComplexProperty
    /// translation gap (see the interface's doc); System-column rendering is proven against real
    /// Postgres by <c>SystemEnrichmentTranslationTests</c>.
    /// </summary>
    private static ISystemMembershipEnricher NoOpSystemMembership()
    {
        var enricher = Substitute.For<ISystemMembershipEnricher>();
        enricher.SystemsForComponentsAsync(
                Arg.Any<CatalogDbContext>(), Arg.Any<EntityKind>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, SystemRef>());
        return enricher;
    }

    private static ListServicesQuery Query(
        Guid[]? teamId = null, HealthStatus[]? health = null, Guid[]? systemId = null, int limit = 50) =>
        new(ServiceSortField.DisplayName, SortOrder.Asc, Cursor: null, Limit: limit,
            TeamId: teamId ?? Array.Empty<Guid>(),
            Health: health ?? Array.Empty<HealthStatus>(),
            SystemId: systemId);

    [TestMethod]
    public async Task Handle_with_no_teamId_filter_returns_all_services()
    {
        await using var db = await BuildDbWithTwoTeamsAsync();
        var page = await new ListServicesHandler(NoOpDirectory(), NoOpSystemMembership()).Handle(Query(), db, CancellationToken.None);
        Assert.AreEqual(2, page.Items.Count, "empty teamId filter must return all services (no default-hide rule)");
    }

    [TestMethod]
    public async Task Handle_with_teamId_filters_to_that_team()
    {
        await using var db = await BuildDbWithTwoTeamsAsync();
        var page = await new ListServicesHandler(NoOpDirectory(), NoOpSystemMembership())
            .Handle(Query(teamId: new[] { TeamB }), db, CancellationToken.None);
        Assert.AreEqual(1, page.Items.Count);
        Assert.AreEqual("In Team B", page.Items.Single().DisplayName);
    }

    [TestMethod]
    public async Task Handle_with_no_health_filter_returns_all_services()
    {
        await using var db = await BuildDbWithTwoServicesAsync();
        var page = await new ListServicesHandler(NoOpDirectory(), NoOpSystemMembership()).Handle(Query(), db, CancellationToken.None);
        Assert.AreEqual(2, page.Items.Count, "empty health filter must return all services (no default-hide rule)");
    }

    [TestMethod]
    public async Task Handle_with_health_Unknown_returns_seeded_unknown_services()
    {
        // All seeded services default to Unknown; filtering to [Unknown] must return them all.
        // This confirms the predicate is correctly applied (includes matching rows).
        await using var db = await BuildDbWithTwoServicesAsync();
        var page = await new ListServicesHandler(NoOpDirectory(), NoOpSystemMembership())
            .Handle(Query(health: new[] { HealthStatus.Unknown }), db, CancellationToken.None);
        Assert.AreEqual(2, page.Items.Count, "Health:[Unknown] must return all seeded Unknown services");
    }

    [TestMethod]
    public async Task Handle_with_health_Healthy_returns_zero_rows()
    {
        // No seeded service has Health = Healthy; the predicate must exclude all rows.
        // A missing Where would let all rows through, killing this assertion on the mutant.
        await using var db = await BuildDbWithTwoServicesAsync();
        var page = await new ListServicesHandler(NoOpDirectory(), NoOpSystemMembership())
            .Handle(Query(health: new[] { HealthStatus.Healthy }), db, CancellationToken.None);
        Assert.AreEqual(0, page.Items.Count, "Health:[Healthy] must exclude all Unknown services");
    }

    [TestMethod]
    public async Task Handle_with_empty_systemId_filter_applies_no_System_predicate()
    {
        // Services builds `filters` lazily (null until a filter applies). An empty systemId must
        // NOT be enough to allocate the dictionary — otherwise the cursor stops being
        // byte-identical to a filterless one.
        await using var db = await BuildDbWithTwoTeamsAsync();
        var handler = new ListServicesHandler(NoOpDirectory(), NoOpSystemMembership());

        // Limit: 1 for the same reason as the Applications twin — at the default 50 both
        // NextCursors are null and the assertion cannot fail.
        var withNull = await handler.Handle(Query(limit: 1), db, CancellationToken.None);
        var withEmpty = await handler.Handle(Query(limit: 1, systemId: []), db, CancellationToken.None);

        Assert.IsNotNull(withNull.NextCursor, "guard: otherwise the comparison below is vacuous");
        Assert.AreEqual(1, withEmpty.Items.Count, "limit is honored");
        Assert.AreEqual(withNull.NextCursor, withEmpty.NextCursor,
            "empty systemId must not allocate the f-map");
    }
}
