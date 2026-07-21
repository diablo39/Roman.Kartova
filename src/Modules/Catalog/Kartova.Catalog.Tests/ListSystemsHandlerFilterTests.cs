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
/// Unit-tier tests for <see cref="ListSystemsHandler"/> — the <c>teamId</c> and
/// <c>displayNameContains</c> filter predicates (ADR-0107), default sort, and the
/// unknown-sort-field failure path (ADR-0095). Uses the EF Core InMemory provider so
/// this runs without a database (mirrors ListApplicationsHandlerFilterTests).
/// </summary>
[TestClass]
public class ListSystemsHandlerFilterTests
{
    private static readonly TenantId Tenant = new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"));
    private static readonly Guid Creator = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid Team = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid TeamB = Guid.Parse("cccccccc-0000-0000-0000-000000000002");

    private static readonly DateTimeOffset BaseTime =
        new(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);

    private static async Task<CatalogDbContext> BuildDbWithTwoTeamsAsync()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var seed = new CatalogDbContext(options);
        var inA = CatalogSystem.Create("In Team A", "d", Creator, Team, Tenant, BaseTime);
        var inB = CatalogSystem.Create("In Team B", "d", Creator, TeamB, Tenant, BaseTime.AddMinutes(1));
        seed.Systems.Add(inA);
        seed.Systems.Add(inB);
        await seed.SaveChangesAsync();
        return new CatalogDbContext(options);
    }

    private static async Task<CatalogDbContext> BuildDbWithThreeSystemsAsync()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var seed = new CatalogDbContext(options);
        seed.Systems.Add(CatalogSystem.Create("Payments Platform", "d", Creator, Team, Tenant, BaseTime));
        seed.Systems.Add(CatalogSystem.Create("Billing Platform", "d", Creator, Team, Tenant, BaseTime.AddMinutes(1)));
        seed.Systems.Add(CatalogSystem.Create("Notifications", "d", Creator, Team, Tenant, BaseTime.AddMinutes(2)));
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

    private static ListSystemsQuery Query(
        SystemSortField sortBy = SystemSortField.DisplayName,
        SortOrder sortOrder = SortOrder.Asc,
        Guid[]? teamId = null,
        string? displayNameContains = null) =>
        new(sortBy, sortOrder, Cursor: null, Limit: 50,
            TeamId: teamId ?? Array.Empty<Guid>(),
            DisplayNameContains: displayNameContains);

    [TestMethod]
    public async Task Handle_with_no_teamId_filter_returns_all_systems()
    {
        await using var db = await BuildDbWithTwoTeamsAsync();
        var page = await new ListSystemsHandler(NoOpDirectory()).Handle(Query(), db, CancellationToken.None);
        Assert.AreEqual(2, page.Items.Count);
    }

    [TestMethod]
    public async Task Handle_with_teamId_filters_to_that_team()
    {
        await using var db = await BuildDbWithTwoTeamsAsync();
        var page = await new ListSystemsHandler(NoOpDirectory())
            .Handle(Query(teamId: new[] { TeamB }), db, CancellationToken.None);
        Assert.AreEqual(1, page.Items.Count);
        Assert.AreEqual("In Team B", page.Items.Single().DisplayName);
    }

    // NOTE: DisplayNameContains uses EF.Functions.ILike (Npgsql-specific) which cannot run
    // under the EF Core InMemory provider — it throws InvalidOperationException on
    // client-evaluation. This mirrors ListApisHandlerFilterTests/ListServicesHandlerFilterTests,
    // neither of which unit-tests its ILIKE substring filter; the predicate is exercised at the
    // integration tier against real Postgres instead (Task 12, ListSystemsPaginationTests).

    [TestMethod]
    public async Task Handle_default_sort_is_displayName_ascending()
    {
        await using var db = await BuildDbWithThreeSystemsAsync();
        var page = await new ListSystemsHandler(NoOpDirectory())
            .Handle(Query(), db, CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "Billing Platform", "Notifications", "Payments Platform" },
            page.Items.Select(i => i.DisplayName).ToArray());
    }

    [TestMethod]
    public async Task Handle_sorts_by_createdAt_when_requested()
    {
        await using var db = await BuildDbWithThreeSystemsAsync();
        var page = await new ListSystemsHandler(NoOpDirectory())
            .Handle(Query(sortBy: SystemSortField.CreatedAt, sortOrder: SortOrder.Asc), db, CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "Payments Platform", "Billing Platform", "Notifications" },
            page.Items.Select(i => i.DisplayName).ToArray());
    }

    [TestMethod]
    public void Resolve_of_unknown_sort_field_throws_InvalidSortFieldException()
    {
        const SystemSortField unknown = (SystemSortField)99;
        Assert.ThrowsExactly<InvalidSortFieldException>(() => SystemSortSpecs.Resolve(unknown));
    }
}
