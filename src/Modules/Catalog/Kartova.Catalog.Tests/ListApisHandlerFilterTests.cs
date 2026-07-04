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
using DomainApi = Kartova.Catalog.Domain.Api;

namespace Kartova.Catalog.Tests;

/// <summary>Unit-tier tests for the teamId / style / displayNameContains predicates in
/// <see cref="ListApisHandler"/> (ADR-0107). EF Core InMemory provider — no database.
/// Empty filter ⇒ no predicate (show all). Non-empty ⇒ narrows + is encoded in the cursor f-map.</summary>
[TestClass]
public class ListApisHandlerFilterTests
{
    private static readonly TenantId Tenant = new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"));
    private static readonly Guid Creator = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid TeamA = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid TeamB = Guid.Parse("cccccccc-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset BaseTime = new(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);

    private static DomainApi MakeApi(string displayName, Guid teamId, ApiStyle style, int minute = 0) =>
        DomainApi.Create(displayName, "test api", style, "v1", specUrl: null,
            createdByUserId: Creator, teamId: teamId, tenantId: Tenant, createdAt: BaseTime.AddMinutes(minute));

    private static async Task<CatalogDbContext> BuildAsync(params DomainApi[] apis)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var seed = new CatalogDbContext(options);
        seed.Apis.AddRange(apis);
        await seed.SaveChangesAsync();
        return new CatalogDbContext(options);
    }

    private static IUserDirectory NoOpDirectory()
    {
        var d = Substitute.For<IUserDirectory>();
        d.GetManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, UserDisplayInfo>());
        return d;
    }

    private static ListApisQuery Query(Guid[]? teamId = null, ApiStyle[]? style = null, string? name = null) =>
        new(ApiSortField.DisplayName, SortOrder.Asc, Cursor: null, Limit: 50,
            TeamId: teamId ?? Array.Empty<Guid>(),
            Style: style ?? Array.Empty<ApiStyle>(),
            DisplayNameContains: name);

    [TestMethod]
    public async Task No_filters_returns_all()
    {
        await using var db = await BuildAsync(
            MakeApi("Alpha", TeamA, ApiStyle.Rest, 0), MakeApi("Beta", TeamB, ApiStyle.Grpc, 1));
        var page = await new ListApisHandler(NoOpDirectory()).Handle(Query(), db, CancellationToken.None);
        Assert.AreEqual(2, page.Items.Count);
    }

    [TestMethod]
    public async Task TeamId_filter_narrows_to_team()
    {
        await using var db = await BuildAsync(
            MakeApi("Alpha", TeamA, ApiStyle.Rest, 0), MakeApi("Beta", TeamB, ApiStyle.Grpc, 1));
        var page = await new ListApisHandler(NoOpDirectory())
            .Handle(Query(teamId: new[] { TeamB }), db, CancellationToken.None);
        Assert.AreEqual(1, page.Items.Count);
        Assert.AreEqual("Beta", page.Items.Single().DisplayName);
    }

    [TestMethod]
    public async Task Style_filter_narrows_to_style()
    {
        await using var db = await BuildAsync(
            MakeApi("Alpha", TeamA, ApiStyle.Rest, 0),
            MakeApi("Beta", TeamA, ApiStyle.Grpc, 1),
            MakeApi("Gamma", TeamA, ApiStyle.GraphQL, 2));
        var page = await new ListApisHandler(NoOpDirectory())
            .Handle(Query(style: new[] { ApiStyle.Grpc, ApiStyle.GraphQL }), db, CancellationToken.None);
        CollectionAssert.AreEquivalent(new[] { "Beta", "Gamma" }, page.Items.Select(i => i.DisplayName).ToList());
    }

    [TestMethod]
    public async Task Style_filter_excludes_non_matching()
    {
        await using var db = await BuildAsync(MakeApi("Alpha", TeamA, ApiStyle.Rest, 0));
        var page = await new ListApisHandler(NoOpDirectory())
            .Handle(Query(style: new[] { ApiStyle.GraphQL }), db, CancellationToken.None);
        Assert.AreEqual(0, page.Items.Count, "Style:[GraphQL] must exclude the Rest api");
    }

    // NOTE: DisplayNameContains uses EF.Functions.ILike (Npgsql-specific) which cannot run
    // under the EF Core InMemory provider — it throws InvalidOperationException on
    // client-evaluation. This mirrors ListServicesHandlerFilterTests, which likewise does
    // NOT unit-test its ILIKE substring filter; the predicate is exercised at the integration
    // tier against real Postgres (cf. ListTeamsTests). The teamId/style predicates above run
    // fine on InMemory and are unit-tested here.
}
