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
/// Unit-tier tests for the Owner-enrichment branch of <see cref="ListApplicationsHandler"/>
/// (slice 9 / E1 — ADR-0098). The handler must:
/// <list type="bullet">
///   <item>Issue exactly one batched <c>GetManyAsync</c> call with the distinct owner ids on the page.</item>
///   <item>Populate <see cref="ApplicationResponse.Owner"/> when the directory returns a matching entry.</item>
///   <item>Leave <see cref="ApplicationResponse.Owner"/> as <c>null</c> when the directory does not return a match
///     (e.g., the user row was deleted after the application was registered).</item>
/// </list>
/// Database-shape concerns (sort, cursor, RLS) live in the integration suite; the EF Core
/// InMemory provider here keeps the unit test free of a Postgres container.
/// </summary>
[TestClass]
public sealed class ListApplicationsHandlerOwnerEnrichmentTests
{
    private static readonly TenantId Tenant = new(Guid.Parse("aaaaaaaa-1111-0000-0000-000000000001"));
    private static readonly DateTimeOffset BaseTime =
        new(2026, 5, 27, 12, 0, 0, TimeSpan.Zero);

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
                    description: "owner-enrichment unit test",
                    ownerUserId: ownerId,
                    tenantId: Tenant,
                    clock: TestClocks.At(BaseTime.AddSeconds(i))));
            }
            await seed.SaveChangesAsync();
        }

        return new CatalogDbContext(options);
    }

    private static ListApplicationsQuery DefaultQuery() => new(
        ApplicationSortField.CreatedAt,
        SortOrder.Desc,
        Cursor: null,
        Limit: 50,
        IncludeDecommissioned: false);

    [TestMethod]
    public async Task Handle_populates_Owner_when_directory_returns_matching_user()
    {
        // Single app + matching directory entry → Owner must be the directory value verbatim.
        // This is the happy-path branch: TryGetValue succeeds and the `with { Owner = ... }`
        // mutator runs.
        var ownerId = Guid.NewGuid();
        await using var db = await BuildDbWithAppsAsync((ownerId, "App With Owner"));

        var directory = Substitute.For<IUserDirectory>();
        var displayInfo = new UserDisplayInfo(ownerId, "Alice Anderson", "alice@orga.kartova.local");
        directory.GetManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, UserDisplayInfo> { [ownerId] = displayInfo });

        var handler = new ListApplicationsHandler(directory);
        var page = await handler.Handle(DefaultQuery(), db, CancellationToken.None);

        var item = page.Items.Single();
        Assert.IsNotNull(item.Owner, "Owner must be populated when the directory returns a match");
        Assert.AreEqual(ownerId, item.Owner!.Id);
        Assert.AreEqual("Alice Anderson", item.Owner.DisplayName);
        Assert.AreEqual("alice@orga.kartova.local", item.Owner.Email);
    }

    [TestMethod]
    public async Task Handle_leaves_Owner_null_when_directory_has_no_entry_for_owner()
    {
        // The user-deleted-after-application-was-registered branch: the directory returns
        // an empty dictionary, so TryGetValue is false and Owner must stay null.
        // Mutating the `?` to `:` (replacing `with { Owner = owner }` with the bare
        // `resp` in the false branch) would silently drop the Owner-on-match case, but
        // this test pins the false branch on its own.
        var ownerId = Guid.NewGuid();
        await using var db = await BuildDbWithAppsAsync((ownerId, "Orphaned App"));

        var directory = Substitute.For<IUserDirectory>();
        directory.GetManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, UserDisplayInfo>());

        var handler = new ListApplicationsHandler(directory);
        var page = await handler.Handle(DefaultQuery(), db, CancellationToken.None);

        var item = page.Items.Single();
        Assert.IsNull(item.Owner, "Owner must be null when the directory returns no entry for the OwnerUserId");
        // Sanity: the row itself was still returned, only the enrichment was skipped.
        Assert.AreEqual("Orphaned App", item.DisplayName);
        Assert.AreEqual(ownerId, item.OwnerUserId);
    }

    [TestMethod]
    public async Task Handle_batches_owner_lookup_with_distinct_ids()
    {
        // Two apps owned by the same user must result in a single GetManyAsync call
        // with a single distinct id (not two). Pins the `Distinct().ToList()` in the
        // handler — removing Distinct would still pass the populate/null tests above,
        // but would silently re-fetch the same user N times on a page of size N.
        var sharedOwner = Guid.NewGuid();
        await using var db = await BuildDbWithAppsAsync(
            (sharedOwner, "App One"),
            (sharedOwner, "App Two"));

        var directory = Substitute.For<IUserDirectory>();
        directory.GetManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, UserDisplayInfo>
            {
                [sharedOwner] = new UserDisplayInfo(sharedOwner, "Shared Owner", "shared@orga.kartova.local"),
            });

        var handler = new ListApplicationsHandler(directory);
        var page = await handler.Handle(DefaultQuery(), db, CancellationToken.None);

        Assert.AreEqual(2, page.Items.Count);
        Assert.IsTrue(page.Items.All(i => i.Owner is not null && i.Owner.Id == sharedOwner),
            "both rows must have Owner populated from the shared directory entry");

        // Exactly one call, exactly one distinct id in the argument.
        await directory.Received(1).GetManyAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Single() == sharedOwner),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Handle_partial_directory_match_populates_only_matched_rows()
    {
        // Two apps with different owners; the directory returns only one of them.
        // Asserts the per-row TryGetValue decision is independent — one Owner populated,
        // the other null. Catches a mutant that always populates Owner from the first
        // match, or always leaves it null after the first miss.
        var matchedOwner = Guid.NewGuid();
        var unmatchedOwner = Guid.NewGuid();
        await using var db = await BuildDbWithAppsAsync(
            (matchedOwner, "Matched App"),
            (unmatchedOwner, "Unmatched App"));

        var directory = Substitute.For<IUserDirectory>();
        directory.GetManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, UserDisplayInfo>
            {
                [matchedOwner] = new UserDisplayInfo(matchedOwner, "Matched", "matched@orga.kartova.local"),
            });

        var handler = new ListApplicationsHandler(directory);
        var page = await handler.Handle(DefaultQuery(), db, CancellationToken.None);

        var matched = page.Items.Single(i => i.OwnerUserId == matchedOwner);
        var unmatched = page.Items.Single(i => i.OwnerUserId == unmatchedOwner);
        Assert.IsNotNull(matched.Owner);
        Assert.AreEqual("Matched", matched.Owner!.DisplayName);
        Assert.IsNull(unmatched.Owner);
    }

    [TestMethod]
    public async Task Handle_empty_page_still_calls_directory_with_empty_collection()
    {
        // Defensive: an empty result set must not skip the directory call entirely
        // (that would diverge from the documented batched-lookup contract). The
        // production GetManyAsync short-circuits on an empty ids list, so the cost
        // is negligible. This test pins that we don't conditionally bypass the call.
        await using var db = await BuildDbWithAppsAsync(); // no apps seeded

        var directory = Substitute.For<IUserDirectory>();
        directory.GetManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, UserDisplayInfo>());

        var handler = new ListApplicationsHandler(directory);
        var page = await handler.Handle(DefaultQuery(), db, CancellationToken.None);

        Assert.AreEqual(0, page.Items.Count);
        await directory.Received(1).GetManyAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 0),
            Arg.Any<CancellationToken>());
    }
}
