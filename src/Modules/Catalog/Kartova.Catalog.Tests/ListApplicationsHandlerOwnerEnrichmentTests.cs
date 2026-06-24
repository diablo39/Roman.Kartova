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
/// Unit-tier tests for the CreatedBy-enrichment branch of <see cref="ListApplicationsHandler"/>
/// (slice 9 / E1 — ADR-0098, renamed slice 10 / ADR-0103). The handler must:
/// <list type="bullet">
///   <item>Issue exactly one batched <c>GetManyAsync</c> call with the distinct created-by ids on the page.</item>
///   <item>Populate <see cref="ApplicationResponse.CreatedBy"/> when the directory returns a matching entry.</item>
///   <item>Leave <see cref="ApplicationResponse.CreatedBy"/> as <c>null</c> when the directory does not return a match
///     (e.g., the user row was deleted after the application was registered).</item>
/// </list>
/// Database-shape concerns (sort, cursor, RLS) live in the integration suite; the EF Core
/// InMemory provider here keeps the unit test free of a Postgres container.
/// </summary>
[TestClass]
public sealed class ListApplicationsHandlerOwnerEnrichmentTests
{
    private static readonly TenantId Tenant = new(Guid.Parse("aaaaaaaa-1111-0000-0000-000000000001"));
    private static readonly Guid Team = Guid.Parse("cccccccc-1111-0000-0000-000000000001");
    private static readonly DateTimeOffset BaseTime =
        new(2026, 5, 27, 12, 0, 0, TimeSpan.Zero);

    private static async Task<CatalogDbContext> BuildDbWithAppsAsync(params (Guid CreatorId, string DisplayName)[] apps)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using (var seed = new CatalogDbContext(options))
        {
            for (var i = 0; i < apps.Length; i++)
            {
                var (creatorId, displayName) = apps[i];
                seed.Applications.Add(DomainApplication.Create(
                    displayName: displayName,
                    description: "created-by-enrichment unit test",
                    createdByUserId: creatorId,
                    teamId: Team,
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
        Lifecycle: Array.Empty<Lifecycle>(),
        TeamId: Array.Empty<Guid>());

    [TestMethod]
    public async Task Handle_populates_CreatedBy_when_directory_returns_matching_user()
    {
        // Single app + matching directory entry → CreatedBy must be the directory value verbatim.
        // This is the happy-path branch: TryGetValue succeeds and the `with { CreatedBy = ... }`
        // mutator runs.
        var creatorId = Guid.NewGuid();
        await using var db = await BuildDbWithAppsAsync((creatorId, "App With Creator"));

        var directory = Substitute.For<IUserDirectory>();
        var displayInfo = new UserDisplayInfo(creatorId, "Alice Anderson", "alice@orga.kartova.local");
        directory.GetManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, UserDisplayInfo> { [creatorId] = displayInfo });

        var handler = new ListApplicationsHandler(directory);
        var page = await handler.Handle(DefaultQuery(), db, CancellationToken.None);

        var item = page.Items.Single();
        Assert.IsNotNull(item.CreatedBy, "CreatedBy must be populated when the directory returns a match");
        Assert.AreEqual(creatorId, item.CreatedBy!.Id);
        Assert.AreEqual("Alice Anderson", item.CreatedBy.DisplayName);
        Assert.AreEqual("alice@orga.kartova.local", item.CreatedBy.Email);
    }

    [TestMethod]
    public async Task Handle_leaves_CreatedBy_null_when_directory_has_no_entry_for_creator()
    {
        // The user-deleted-after-application-was-registered branch: the directory returns
        // an empty dictionary, so TryGetValue is false and CreatedBy must stay null.
        // Mutating the `?` to `:` (replacing `with { CreatedBy = creator }` with the bare
        // `resp` in the false branch) would silently drop the CreatedBy-on-match case, but
        // this test pins the false branch on its own.
        var creatorId = Guid.NewGuid();
        await using var db = await BuildDbWithAppsAsync((creatorId, "Orphaned App"));

        var directory = Substitute.For<IUserDirectory>();
        directory.GetManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, UserDisplayInfo>());

        var handler = new ListApplicationsHandler(directory);
        var page = await handler.Handle(DefaultQuery(), db, CancellationToken.None);

        var item = page.Items.Single();
        Assert.IsNull(item.CreatedBy, "CreatedBy must be null when the directory returns no entry for the CreatedByUserId");
        // Sanity: the row itself was still returned, only the enrichment was skipped.
        Assert.AreEqual("Orphaned App", item.DisplayName);
        Assert.AreEqual(creatorId, item.CreatedByUserId);
    }

    [TestMethod]
    public async Task Handle_batches_creator_lookup_with_distinct_ids()
    {
        // Two apps created by the same user must result in a single GetManyAsync call
        // with a single distinct id (not two). Pins the `Distinct().ToList()` in the
        // handler — removing Distinct would still pass the populate/null tests above,
        // but would silently re-fetch the same user N times on a page of size N.
        var sharedCreator = Guid.NewGuid();
        await using var db = await BuildDbWithAppsAsync(
            (sharedCreator, "App One"),
            (sharedCreator, "App Two"));

        var directory = Substitute.For<IUserDirectory>();
        directory.GetManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, UserDisplayInfo>
            {
                [sharedCreator] = new UserDisplayInfo(sharedCreator, "Shared Creator", "shared@orga.kartova.local"),
            });

        var handler = new ListApplicationsHandler(directory);
        var page = await handler.Handle(DefaultQuery(), db, CancellationToken.None);

        Assert.AreEqual(2, page.Items.Count);
        Assert.IsTrue(page.Items.All(i => i.CreatedBy is not null && i.CreatedBy.Id == sharedCreator),
            "both rows must have CreatedBy populated from the shared directory entry");

        // Exactly one call, exactly one distinct id in the argument.
        await directory.Received(1).GetManyAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Single() == sharedCreator),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Handle_partial_directory_match_populates_only_matched_rows()
    {
        // Two apps with different creators; the directory returns only one of them.
        // Asserts the per-row TryGetValue decision is independent — one CreatedBy populated,
        // the other null. Catches a mutant that always populates CreatedBy from the first
        // match, or always leaves it null after the first miss.
        var matchedCreator = Guid.NewGuid();
        var unmatchedCreator = Guid.NewGuid();
        await using var db = await BuildDbWithAppsAsync(
            (matchedCreator, "Matched App"),
            (unmatchedCreator, "Unmatched App"));

        var directory = Substitute.For<IUserDirectory>();
        directory.GetManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, UserDisplayInfo>
            {
                [matchedCreator] = new UserDisplayInfo(matchedCreator, "Matched", "matched@orga.kartova.local"),
            });

        var handler = new ListApplicationsHandler(directory);
        var page = await handler.Handle(DefaultQuery(), db, CancellationToken.None);

        var matched = page.Items.Single(i => i.CreatedByUserId == matchedCreator);
        var unmatched = page.Items.Single(i => i.CreatedByUserId == unmatchedCreator);
        Assert.IsNotNull(matched.CreatedBy);
        Assert.AreEqual("Matched", matched.CreatedBy!.DisplayName);
        Assert.IsNull(unmatched.CreatedBy);
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
