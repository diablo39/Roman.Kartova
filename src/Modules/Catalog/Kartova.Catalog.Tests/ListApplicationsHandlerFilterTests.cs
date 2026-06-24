using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.Catalog.Infrastructure;
using Kartova.SharedKernel;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Multitenancy;
using Kartova.SharedKernel.Pagination;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

// NOTE: Alias needed — the enclosing `Kartova.Catalog` namespace contains a sibling child
// namespace `Kartova.Catalog.Application` which wins simple-name lookup for `Application`.
using DomainApplication = Kartova.Catalog.Domain.Application;

namespace Kartova.Catalog.Tests;

/// <summary>
/// Unit-tier tests for the lifecycle and teamId predicates in
/// <see cref="ListApplicationsHandler"/> (ADR-0107). Uses the EF Core InMemory
/// provider so this runs without a database.
/// <para>
/// Lifecycle filter: empty ⇒ ADR-0073 default view (hide Decommissioned);
/// non-empty ⇒ exactly the selected states (<c>IN</c> predicate).
/// TeamId filter: non-empty ⇒ rows whose <c>TeamId</c> is in the supplied set.
/// Both filters are encoded into the cursor <c>f</c>-map when non-empty.
/// </para>
/// </summary>
[TestClass]
public class ListApplicationsHandlerFilterTests
{
    private static readonly TenantId Tenant = new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"));
    private static readonly Guid Creator = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid Team = Guid.Parse("cccccccc-0000-0000-0000-000000000001");

    // Second team for teamId filter tests.
    private static readonly Guid TeamB = Guid.Parse("cccccccc-0000-0000-0000-000000000002");

    private static readonly DateTimeOffset BaseTime =
        new(2026, 5, 7, 12, 0, 0, TimeSpan.Zero);

    private static FakeTimeProvider Clock(DateTimeOffset? at = null) => TestClocks.At(at ?? BaseTime);

    /// <summary>
    /// Builds a fresh InMemory <see cref="CatalogDbContext"/> with one Active and
    /// one Decommissioned application already saved. The two apps use distinct
    /// <c>CreatedAt</c> timestamps (1 minute apart) so the deterministic ordering
    /// assertion is stable regardless of insertion order.
    /// </summary>
    private static async Task<CatalogDbContext> BuildDbWithBothLifecyclesAsync()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var seed = new CatalogDbContext(options);

        var activeClock = Clock(BaseTime);
        var active = DomainApplication.Create(
            displayName: "Active App",
            description: "An active application.",
            createdByUserId: Creator,
            teamId: Team,
            tenantId: Tenant,
            clock: activeClock);

        // Drive the decommission state machine: Active → Deprecated → Decommissioned.
        var sunsetClock = Clock(BaseTime.AddMinutes(10));
        var decommClock = Clock(BaseTime.AddMinutes(20));
        var decomm = DomainApplication.Create(
            displayName: "Decomm App",
            description: "A decommissioned application.",
            createdByUserId: Creator,
            teamId: Team,
            tenantId: Tenant,
            clock: Clock(BaseTime.AddMinutes(1)));
        decomm.Deprecate(sunsetDate: BaseTime.AddMinutes(15), clock: sunsetClock);
        decomm.Decommission(clock: decommClock);

        seed.Applications.Add(active);
        seed.Applications.Add(decomm);
        await seed.SaveChangesAsync();

        // Return a fresh context over the same database so the in-memory store is shared
        // but the identity map is clean.
        return new CatalogDbContext(options);
    }

    private static async Task<CatalogDbContext> BuildDbWithTwoTeamsAsync()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var seed = new CatalogDbContext(options);
        var inA = DomainApplication.Create("In Team A", "d", Creator, Team, Tenant, Clock(BaseTime));
        var inB = DomainApplication.Create("In Team B", "d", Creator, TeamB, Tenant, Clock(BaseTime.AddMinutes(1)));
        seed.Applications.Add(inA);
        seed.Applications.Add(inB);
        await seed.SaveChangesAsync();
        return new CatalogDbContext(options);
    }

    /// <summary>
    /// Returns an <see cref="IUserDirectory"/> stub whose <c>GetManyAsync</c> always
    /// resolves to an empty dictionary. The filter tests intentionally exercise the
    /// lifecycle/teamId predicate paths only — Owner-enrichment branch coverage
    /// lives in <see cref="ListApplicationsHandlerOwnerEnrichmentTests"/>.
    /// </summary>
    private static IUserDirectory NoOpDirectory()
    {
        var directory = Substitute.For<IUserDirectory>();
        directory.GetManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, UserDisplayInfo>());
        return directory;
    }

    private static ListApplicationsQuery Query(Lifecycle[]? lifecycle = null, Guid[]? teamId = null) =>
        new(ApplicationSortField.CreatedAt, SortOrder.Desc, Cursor: null, Limit: 50,
            Lifecycle: lifecycle ?? Array.Empty<Lifecycle>(),
            TeamId: teamId ?? Array.Empty<Guid>());

    [TestMethod]
    public async Task Handle_with_no_lifecycle_filter_excludes_Decommissioned_rows()
    {
        await using var db = await BuildDbWithBothLifecyclesAsync();
        var page = await new ListApplicationsHandler(NoOpDirectory()).Handle(Query(), db, CancellationToken.None);
        Assert.AreEqual(1, page.Items.Count, "empty lifecycle filter applies the ADR-0073 default view");
        Assert.AreEqual("Active App", page.Items.Single().DisplayName);
    }

    [TestMethod]
    public async Task Handle_with_lifecycle_decommissioned_returns_only_decommissioned()
    {
        await using var db = await BuildDbWithBothLifecyclesAsync();
        var page = await new ListApplicationsHandler(NoOpDirectory())
            .Handle(Query(lifecycle: new[] { Lifecycle.Decommissioned }), db, CancellationToken.None);
        Assert.AreEqual(1, page.Items.Count);
        Assert.AreEqual("Decomm App", page.Items.Single().DisplayName);
    }

    [TestMethod]
    public async Task Handle_with_all_lifecycles_returns_both()
    {
        await using var db = await BuildDbWithBothLifecyclesAsync();
        var page = await new ListApplicationsHandler(NoOpDirectory())
            .Handle(Query(lifecycle: new[] { Lifecycle.Active, Lifecycle.Deprecated, Lifecycle.Decommissioned }), db, CancellationToken.None);
        Assert.AreEqual(2, page.Items.Count);
        CollectionAssert.AreEquivalent(new[] { "Active App", "Decomm App" }, page.Items.Select(i => i.DisplayName).ToArray());
    }

    [TestMethod]
    public async Task Handle_with_lifecycle_deprecated_returns_only_deprecated()
    {
        // Pins the middle enum value (Deprecated = 2): the Contains predicate must
        // match it without confusing it for Active (1) or Decommissioned (3).
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using (var seed = new CatalogDbContext(options))
        {
            var active = DomainApplication.Create("Active App", "d", Creator, Team, Tenant, Clock(BaseTime));
            var deprecated = DomainApplication.Create("Deprecated App", "d", Creator, Team, Tenant, Clock(BaseTime.AddMinutes(1)));
            deprecated.Deprecate(sunsetDate: BaseTime.AddMinutes(15), clock: Clock(BaseTime.AddMinutes(10)));
            seed.Applications.Add(active);
            seed.Applications.Add(deprecated);
            await seed.SaveChangesAsync();
        }
        await using var db = new CatalogDbContext(options);

        var page = await new ListApplicationsHandler(NoOpDirectory())
            .Handle(Query(lifecycle: new[] { Lifecycle.Deprecated }), db, CancellationToken.None);

        Assert.AreEqual(1, page.Items.Count);
        Assert.AreEqual("Deprecated App", page.Items.Single().DisplayName);
    }

    [TestMethod]
    public async Task Handle_with_teamId_filters_to_that_team()
    {
        await using var db = await BuildDbWithTwoTeamsAsync();
        var page = await new ListApplicationsHandler(NoOpDirectory())
            .Handle(Query(teamId: new[] { TeamB }), db, CancellationToken.None);
        Assert.AreEqual(1, page.Items.Count);
        Assert.AreEqual("In Team B", page.Items.Single().DisplayName);
    }
}
