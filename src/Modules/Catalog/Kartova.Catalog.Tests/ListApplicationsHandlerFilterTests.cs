using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.Catalog.Infrastructure;
using Kartova.SharedKernel.Multitenancy;
using Kartova.SharedKernel.Pagination;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

// NOTE: Alias needed — the enclosing `Kartova.Catalog` namespace contains a sibling child
// namespace `Kartova.Catalog.Application` which wins simple-name lookup for `Application`.
using DomainApplication = Kartova.Catalog.Domain.Application;

namespace Kartova.Catalog.Tests;

/// <summary>
/// Unit-tier test for the <c>IncludeDecommissioned</c> predicate in
/// <see cref="ListApplicationsHandler"/>. Uses the EF Core InMemory provider so this
/// runs without a database. Spec §8 (slice 6).
/// </summary>
[TestClass]
public class ListApplicationsHandlerFilterTests
{
    private static readonly TenantId Tenant = new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"));
    private static readonly Guid Owner = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");

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
            name: "active-app",
            displayName: "Active App",
            description: "An active application.",
            ownerUserId: Owner,
            tenantId: Tenant,
            clock: activeClock);

        // Drive the decommission state machine: Active → Deprecated → Decommissioned.
        var sunsetClock = Clock(BaseTime.AddMinutes(10));
        var decommClock = Clock(BaseTime.AddMinutes(20));
        var decomm = DomainApplication.Create(
            name: "decomm-app",
            displayName: "Decomm App",
            description: "A decommissioned application.",
            ownerUserId: Owner,
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

    [TestMethod]
    public async Task Handle_with_IncludeDecommissioned_false_excludes_Decommissioned_rows()
    {
        await using var db = await BuildDbWithBothLifecyclesAsync();

        var handler = new ListApplicationsHandler();
        var query = new ListApplicationsQuery(
            ApplicationSortField.CreatedAt,
            SortOrder.Desc,
            Cursor: null,
            Limit: 50,
            IncludeDecommissioned: false);

        var page = await handler.Handle(query, db, CancellationToken.None);

        Assert.AreEqual(1, page.Items.Count, "only Active rows are visible when IncludeDecommissioned=false");
        Assert.AreEqual("active-app", page.Items.Single().Name);
    }

    [TestMethod]
    public async Task Handle_with_IncludeDecommissioned_true_returns_both_lifecycles()
    {
        await using var db = await BuildDbWithBothLifecyclesAsync();

        var handler = new ListApplicationsHandler();
        var query = new ListApplicationsQuery(
            ApplicationSortField.CreatedAt,
            SortOrder.Desc,
            Cursor: null,
            Limit: 50,
            IncludeDecommissioned: true);

        var page = await handler.Handle(query, db, CancellationToken.None);

        Assert.AreEqual(2, page.Items.Count, "both Active and Decommissioned rows are returned when IncludeDecommissioned=true");
        CollectionAssert.AreEquivalent(
            new[] { "active-app", "decomm-app" },
            page.Items.Select(i => i.Name).ToArray());
    }
}
