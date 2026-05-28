using Kartova.Organization.Application;
using Kartova.Organization.Contracts;
using Kartova.Organization.Domain;
using Kartova.SharedKernel.Multitenancy;
using Kartova.SharedKernel.Pagination;
using Kartova.SharedKernel.Postgres.Pagination;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Kartova.Organization.Infrastructure.Tests;

/// <summary>
/// Behavioral tests for <see cref="ListInvitationsHandler"/> — slice 9
/// spec §6.7. Covers empty result, sort order, and the optional status
/// filter. Sort / cursor edge cases live in the shared
/// <c>QueryablePagingExtensions</c> tests.
/// </summary>
[TestClass]
public sealed class ListInvitationsHandlerTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-05-27T10:00:00Z");

    private static DbContextOptions<OrganizationDbContext> NewOptions() =>
        new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseInMemoryDatabase($"list-inv-{Guid.NewGuid()}")
            .Options;

    [TestMethod]
    public async Task Returns_empty_page_when_no_invitations()
    {
        await using var db = new OrganizationDbContext(NewOptions());
        var sut = new ListInvitationsHandler();
        var q = new ListInvitationsQuery(
            InvitationSortField.InvitedAt, SortOrder.Desc,
            Cursor: null, Limit: QueryablePagingExtensions.DefaultLimit,
            StatusFilter: null);

        var page = await sut.Handle(q, db, CancellationToken.None);

        Assert.AreEqual(0, page.Items.Count);
        Assert.IsNull(page.NextCursor);
    }

    [TestMethod]
    public async Task Returns_invitations_sorted_by_invitedAt_desc()
    {
        await using var db = new OrganizationDbContext(NewOptions());
        var tenant = new TenantId(Guid.NewGuid());
        var clock = new FakeTimeProvider(T0);

        // Seed three Pending invitations spaced 1h apart, all with unique emails
        // so the sort comparison is unambiguous.
        var older = Invitation.Create("a@x.com", KartovaRoles.Member, Guid.NewGuid(), Guid.NewGuid(), tenant, clock);
        clock.Advance(TimeSpan.FromHours(1));
        var middle = Invitation.Create("b@x.com", KartovaRoles.Member, Guid.NewGuid(), Guid.NewGuid(), tenant, clock);
        clock.Advance(TimeSpan.FromHours(1));
        var newest = Invitation.Create("c@x.com", KartovaRoles.Member, Guid.NewGuid(), Guid.NewGuid(), tenant, clock);

        db.Invitations.AddRange(older, middle, newest);
        await db.SaveChangesAsync();

        var sut = new ListInvitationsHandler();
        var q = new ListInvitationsQuery(
            InvitationSortField.InvitedAt, SortOrder.Desc,
            Cursor: null, Limit: 50, StatusFilter: null);

        var page = await sut.Handle(q, db, CancellationToken.None);

        Assert.AreEqual(3, page.Items.Count);
        Assert.AreEqual("c@x.com", page.Items[0].Email);
        Assert.AreEqual("b@x.com", page.Items[1].Email);
        Assert.AreEqual("a@x.com", page.Items[2].Email);
    }

    [TestMethod]
    public async Task Filters_by_status_when_statusFilter_set()
    {
        await using var db = new OrganizationDbContext(NewOptions());
        var tenant = new TenantId(Guid.NewGuid());
        var clock = new FakeTimeProvider(T0);

        var pending = Invitation.Create("pending@x.com", KartovaRoles.Member, Guid.NewGuid(), Guid.NewGuid(), tenant, clock);
        var toRevoke = Invitation.Create("revoked@x.com", KartovaRoles.Member, Guid.NewGuid(), Guid.NewGuid(), tenant, clock);
        toRevoke.Revoke(clock);

        db.Invitations.AddRange(pending, toRevoke);
        await db.SaveChangesAsync();

        var sut = new ListInvitationsHandler();
        var q = new ListInvitationsQuery(
            InvitationSortField.InvitedAt, SortOrder.Desc,
            Cursor: null, Limit: 50, StatusFilter: InvitationStatus.Pending);

        var page = await sut.Handle(q, db, CancellationToken.None);

        Assert.AreEqual(1, page.Items.Count);
        Assert.AreEqual("pending@x.com", page.Items[0].Email);
        Assert.AreEqual("Pending", page.Items[0].Status);
    }
}
