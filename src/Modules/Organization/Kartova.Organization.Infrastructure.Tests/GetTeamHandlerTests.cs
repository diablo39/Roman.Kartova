using Kartova.Organization.Application;
using Kartova.Organization.Domain;
using Kartova.SharedKernel;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Kartova.Organization.Infrastructure.Tests;

/// <summary>
/// Behavioral tests for <see cref="GetTeamHandler"/> — slice 9 / E3
/// (ADR-0098). The handler's observable behavior is the wire shape of
/// <see cref="Contracts.TeamDetailResponse"/>; tests use NSubstitute for the
/// cross-module seams (<see cref="IApplicationsByTeamReader"/>,
/// <see cref="IUserDirectory"/>) and a real <see cref="OrganizationDbContext"/>
/// against the InMemory provider — same pattern as sibling D-phase handler
/// tests (<see cref="SessionStartHandlerTests"/>).
/// </summary>
[TestClass]
public sealed class GetTeamHandlerTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-05-27T10:00:00Z");

    private static OrganizationDbContext NewInMemory(out TenantId tenant)
    {
        tenant = new TenantId(Guid.NewGuid());
        var opts = new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseInMemoryDatabase($"get-team-{Guid.NewGuid()}").Options;
        return new OrganizationDbContext(opts);
    }

    private static Team SeedTeam(OrganizationDbContext db, TenantId tenant, string displayName = "Platform")
    {
        var clock = new FakeTimeProvider(T0);
        var team = Team.Create(displayName, description: null, tenantId: tenant, clock: clock);
        db.Teams.Add(team);
        db.SaveChanges();
        return team;
    }

    private static TeamMembership AddMember(
        OrganizationDbContext db,
        Team team,
        Guid userId,
        TeamRole role,
        TimeProvider clock)
    {
        var m = TeamMembership.Create(team.Id, userId, role, clock);
        db.TeamMembers.Add(m);
        db.SaveChanges();
        return m;
    }

    private static IApplicationsByTeamReader EmptyAppsReader()
    {
        var apps = Substitute.For<IApplicationsByTeamReader>();
        apps.GetByTeamAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ApplicationByTeamSummary>());
        return apps;
    }

    [TestMethod]
    public async Task Handle_returns_null_when_team_not_found()
    {
        await using var db = NewInMemory(out _);
        var sut = new GetTeamHandler(EmptyAppsReader(), Substitute.For<IUserDirectory>());

        var result = await sut.Handle(new GetTeamQuery(Guid.NewGuid()), db, CancellationToken.None);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task Handle_populates_DisplayName_and_Email_for_members_with_directory_entries()
    {
        await using var db = NewInMemory(out var tenant);
        var team = SeedTeam(db, tenant);
        var clock = new FakeTimeProvider(T0);
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        AddMember(db, team, alice, TeamRole.Admin, clock);
        clock.Advance(TimeSpan.FromMinutes(1));
        AddMember(db, team, bob, TeamRole.Member, clock);

        var directory = Substitute.For<IUserDirectory>();
        directory
            .GetManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, UserDisplayInfo>
            {
                [alice] = new(alice, "Alice Anderson", "alice@example.com"),
                [bob] = new(bob, "Bob Brown", "bob@example.com"),
            });

        var sut = new GetTeamHandler(EmptyAppsReader(), directory);
        var result = await sut.Handle(new GetTeamQuery(team.Id.Value), db, CancellationToken.None);

        Assert.IsNotNull(result);
        var members = result!.Members.ToList();
        Assert.AreEqual(2, members.Count);

        // Members ordered by AddedAt — Alice (T0) before Bob (T0 + 1m).
        Assert.AreEqual(alice, members[0].UserId);
        Assert.AreEqual("Admin", members[0].Role);
        Assert.AreEqual("Alice Anderson", members[0].DisplayName);
        Assert.AreEqual("alice@example.com", members[0].Email);

        Assert.AreEqual(bob, members[1].UserId);
        Assert.AreEqual("Member", members[1].Role);
        Assert.AreEqual("Bob Brown", members[1].DisplayName);
        Assert.AreEqual("bob@example.com", members[1].Email);
    }

    [TestMethod]
    public async Task Handle_falls_back_to_empty_strings_when_directory_returns_no_match()
    {
        // Spec §6.6 non-nullable contract: when the IUserDirectory lookup
        // returns no entry for a member's UserId (user deleted, projection
        // lag), both DisplayName and Email surface as the empty string.
        await using var db = NewInMemory(out var tenant);
        var team = SeedTeam(db, tenant);
        var clock = new FakeTimeProvider(T0);
        var orphan = Guid.NewGuid();
        AddMember(db, team, orphan, TeamRole.Member, clock);

        var directory = Substitute.For<IUserDirectory>();
        directory
            .GetManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, UserDisplayInfo>());

        var sut = new GetTeamHandler(EmptyAppsReader(), directory);
        var result = await sut.Handle(new GetTeamQuery(team.Id.Value), db, CancellationToken.None);

        Assert.IsNotNull(result);
        var members = result!.Members.ToList();
        Assert.AreEqual(1, members.Count);
        Assert.AreEqual(orphan, members[0].UserId);
        Assert.AreEqual("", members[0].DisplayName);
        Assert.AreEqual("", members[0].Email);
    }

    [TestMethod]
    public async Task Handle_uses_correct_directory_entry_per_member_when_only_some_resolve()
    {
        // Mixed lookup: Alice resolves, Bob does not. The two rows must not
        // cross-contaminate — Alice keeps her real info, Bob falls back.
        await using var db = NewInMemory(out var tenant);
        var team = SeedTeam(db, tenant);
        var clock = new FakeTimeProvider(T0);
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        AddMember(db, team, alice, TeamRole.Admin, clock);
        clock.Advance(TimeSpan.FromMinutes(1));
        AddMember(db, team, bob, TeamRole.Member, clock);

        var directory = Substitute.For<IUserDirectory>();
        directory
            .GetManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, UserDisplayInfo>
            {
                [alice] = new(alice, "Alice Anderson", "alice@example.com"),
            });

        var sut = new GetTeamHandler(EmptyAppsReader(), directory);
        var result = await sut.Handle(new GetTeamQuery(team.Id.Value), db, CancellationToken.None);

        Assert.IsNotNull(result);
        var members = result!.Members.ToList();
        Assert.AreEqual(2, members.Count);

        Assert.AreEqual(alice, members[0].UserId);
        Assert.AreEqual("Alice Anderson", members[0].DisplayName);
        Assert.AreEqual("alice@example.com", members[0].Email);

        Assert.AreEqual(bob, members[1].UserId);
        Assert.AreEqual("", members[1].DisplayName);
        Assert.AreEqual("", members[1].Email);
    }

    [TestMethod]
    public async Task Handle_invokes_directory_GetManyAsync_once_with_all_member_ids()
    {
        // Pins the contract that the handler issues a single batch call rather
        // than N individual GetAsync hits. (The HashSet de-dup step is defensive
        // — the team_members PK guarantees UserId uniqueness within a team — so
        // this test does not exercise the dedup branch; OrganizationUserDirectory
        // tests cover GetManyAsync's own input contract.)
        await using var db = NewInMemory(out var tenant);
        var team = SeedTeam(db, tenant);
        var clock = new FakeTimeProvider(T0);
        var u1 = Guid.NewGuid();
        var u2 = Guid.NewGuid();
        AddMember(db, team, u1, TeamRole.Admin, clock);
        clock.Advance(TimeSpan.FromMinutes(1));
        AddMember(db, team, u2, TeamRole.Member, clock);

        var directory = Substitute.For<IUserDirectory>();
        directory
            .GetManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, UserDisplayInfo>());

        var sut = new GetTeamHandler(EmptyAppsReader(), directory);
        _ = await sut.Handle(new GetTeamQuery(team.Id.Value), db, CancellationToken.None);

        await directory.Received(1).GetManyAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(u1) && ids.Contains(u2)),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Handle_returns_empty_members_list_for_team_with_no_members()
    {
        // Empty membership path — GetManyAsync may still be called with an
        // empty set, which the OrganizationUserDirectory implementation
        // handles by short-circuiting. The response shape must still have
        // an empty (not null) members list.
        await using var db = NewInMemory(out var tenant);
        var team = SeedTeam(db, tenant);

        var directory = Substitute.For<IUserDirectory>();
        directory
            .GetManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, UserDisplayInfo>());

        var sut = new GetTeamHandler(EmptyAppsReader(), directory);
        var result = await sut.Handle(new GetTeamQuery(team.Id.Value), db, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(0, result!.Members.Count);
    }
}
