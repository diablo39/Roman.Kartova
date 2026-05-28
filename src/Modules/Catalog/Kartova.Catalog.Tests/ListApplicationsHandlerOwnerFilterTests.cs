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

// NOTE: Alias needed — the enclosing `Kartova.Catalog` namespace contains a sibling child
// namespace `Kartova.Catalog.Application` which wins simple-name lookup for `Application`.
using DomainApplication = Kartova.Catalog.Domain.Application;

namespace Kartova.Catalog.Tests;

/// <summary>
/// Unit-tier tests for the <c>OwnerUserId</c> predicate branch of
/// <see cref="ListApplicationsHandler"/> (slice 9 / E2 — spec §6.5). The handler
/// must:
/// <list type="bullet">
///   <item>Return all rows when <c>OwnerUserId</c> is null (default behavior, unchanged).</item>
///   <item>Return only rows whose <c>OwnerUserId</c> matches when the filter is supplied.</item>
///   <item>Apply the predicate <em>before</em> pagination so the keyset bounds are computed
///     against the filtered subset (consistent with the IncludeDecommissioned filter).</item>
/// </list>
/// Endpoint-level validation (422 invalid-owner when the supplied id has no matching
/// users row) is covered by the integration suite; this test class focuses on the
/// query predicate itself, where EF Core InMemory is sufficient.
/// </summary>
[TestClass]
public sealed class ListApplicationsHandlerOwnerFilterTests
{
    private static readonly TenantId Tenant = new(Guid.Parse("aaaaaaaa-2222-0000-0000-000000000001"));
    private static readonly DateTimeOffset BaseTime =
        new(2026, 5, 28, 12, 0, 0, TimeSpan.Zero);

    private static async Task<CatalogDbContext> BuildDbWithAppsAsync(params (Guid OwnerId, string DisplayName)[] apps)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using (var seed = new CatalogDbContext(options))
        {
            for (var i = 0; i < apps.Length; i++)
            {
                var (ownerId, displayName) = apps[i];
                seed.Applications.Add(DomainApplication.Create(
                    displayName: displayName,
                    description: "owner-filter unit test",
                    ownerUserId: ownerId,
                    tenantId: Tenant,
                    clock: TestClocks.At(BaseTime.AddSeconds(i))));
            }
            await seed.SaveChangesAsync();
        }

        return new CatalogDbContext(options);
    }

    private static IUserDirectory NoOpDirectory()
    {
        // The handler's owner-enrichment step calls GetManyAsync for every page,
        // even an empty one. Stubbing it with an empty dictionary keeps these
        // tests focused on the predicate path; Owner-enrichment branch coverage
        // lives in ListApplicationsHandlerOwnerEnrichmentTests.
        var directory = Substitute.For<IUserDirectory>();
        directory.GetManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, UserDisplayInfo>());
        return directory;
    }

    private static ListApplicationsQuery DefaultQuery(Guid? ownerUserId = null) => new(
        ApplicationSortField.CreatedAt,
        SortOrder.Desc,
        Cursor: null,
        Limit: 50,
        IncludeDecommissioned: false,
        OwnerUserId: ownerUserId);

    [TestMethod]
    public async Task Handle_with_null_OwnerUserId_returns_all_rows()
    {
        // Baseline / regression guard: the filter is optional and the default null
        // path must not change pre-E2 behaviour. Pins a mutant that always applies
        // the predicate (e.g., flipping `is { }` to `is null`).
        var ownerA = Guid.NewGuid();
        var ownerB = Guid.NewGuid();
        await using var db = await BuildDbWithAppsAsync(
            (ownerA, "App A1"), (ownerA, "App A2"), (ownerA, "App A3"),
            (ownerB, "App B1"), (ownerB, "App B2"));

        var handler = new ListApplicationsHandler(NoOpDirectory());
        var page = await handler.Handle(DefaultQuery(ownerUserId: null), db, CancellationToken.None);

        Assert.AreEqual(5, page.Items.Count, "null ownerUserId must return all rows visible under the tenant scope");
    }

    [TestMethod]
    public async Task Handle_with_OwnerUserId_returns_only_apps_for_that_owner()
    {
        // Happy path: filter narrows to the requested subset and excludes the other.
        // Pins the WHERE clause; a mutant replacing `==` with `!=` would invert the
        // counts, and dropping the WHERE entirely would still see all 5.
        var ownerA = Guid.NewGuid();
        var ownerB = Guid.NewGuid();
        await using var db = await BuildDbWithAppsAsync(
            (ownerA, "App A1"), (ownerA, "App A2"), (ownerA, "App A3"),
            (ownerB, "App B1"), (ownerB, "App B2"));

        var handler = new ListApplicationsHandler(NoOpDirectory());

        var pageA = await handler.Handle(DefaultQuery(ownerUserId: ownerA), db, CancellationToken.None);
        Assert.AreEqual(3, pageA.Items.Count, "filter must return exactly the 3 apps owned by ownerA");
        Assert.IsTrue(pageA.Items.All(i => i.OwnerUserId == ownerA),
            "every returned row must have OwnerUserId == filter value");

        var pageB = await handler.Handle(DefaultQuery(ownerUserId: ownerB), db, CancellationToken.None);
        Assert.AreEqual(2, pageB.Items.Count, "filter must return exactly the 2 apps owned by ownerB");
        Assert.IsTrue(pageB.Items.All(i => i.OwnerUserId == ownerB),
            "every returned row must have OwnerUserId == filter value");
    }

    [TestMethod]
    public async Task Handle_with_OwnerUserId_no_matches_returns_empty_page()
    {
        // The endpoint validates that the supplied id resolves to a user in the
        // tenant BEFORE calling the handler. But a user can exist (validation
        // passes) and still own no applications — e.g., a freshly invited member.
        // The handler must return an empty page rather than fall through to
        // "no filter" behaviour. Pins a mutant that swallows the predicate when
        // it produces an empty set.
        var ownerA = Guid.NewGuid();
        var orphanOwner = Guid.NewGuid();
        await using var db = await BuildDbWithAppsAsync(
            (ownerA, "App A1"), (ownerA, "App A2"));

        var handler = new ListApplicationsHandler(NoOpDirectory());
        var page = await handler.Handle(DefaultQuery(ownerUserId: orphanOwner), db, CancellationToken.None);

        Assert.AreEqual(0, page.Items.Count,
            "filter for an owner who owns no apps must return an empty page, not fall back to no filter");
    }

    [TestMethod]
    public async Task Handle_OwnerUserId_filter_composes_with_IncludeDecommissioned()
    {
        // Cross-filter composition: the owner filter must layer on top of the
        // IncludeDecommissioned filter, not replace it. Seed two apps for the same
        // owner and Decommission one; the default IncludeDecommissioned=false must
        // still hide the Decommissioned row even when the owner filter is applied.
        var ownerId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using (var seed = new CatalogDbContext(options))
        {
            var clockActive = TestClocks.At(BaseTime);
            var active = DomainApplication.Create(
                displayName: "Active",
                description: "active",
                ownerUserId: ownerId,
                tenantId: Tenant,
                clock: clockActive);

            var clockSunset = TestClocks.At(BaseTime.AddMinutes(10));
            var clockDecomm = TestClocks.At(BaseTime.AddMinutes(20));
            var decomm = DomainApplication.Create(
                displayName: "Decomm",
                description: "decomm",
                ownerUserId: ownerId,
                tenantId: Tenant,
                clock: TestClocks.At(BaseTime.AddMinutes(1)));
            decomm.Deprecate(sunsetDate: BaseTime.AddMinutes(15), clock: clockSunset);
            decomm.Decommission(clock: clockDecomm);

            seed.Applications.Add(active);
            seed.Applications.Add(decomm);
            await seed.SaveChangesAsync();
        }

        await using var db = new CatalogDbContext(options);
        var handler = new ListApplicationsHandler(NoOpDirectory());
        var page = await handler.Handle(DefaultQuery(ownerUserId: ownerId), db, CancellationToken.None);

        Assert.AreEqual(1, page.Items.Count,
            "owner filter must compose with IncludeDecommissioned=false — Decommissioned row stays hidden");
        Assert.AreEqual("Active", page.Items.Single().DisplayName);
    }
}
