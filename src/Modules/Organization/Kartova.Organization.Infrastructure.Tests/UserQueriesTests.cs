using Kartova.Organization.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Kartova.Organization.Infrastructure.Tests;

[TestClass]
public sealed class UserQueriesTests
{
    private static readonly TenantId TestTenant =
        new(Guid.Parse("00000000-0000-0000-0000-0000000000a1"));

    private static OrganizationDbContext NewInMemory() =>
        new(new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseInMemoryDatabase($"user-queries-{Guid.NewGuid()}")
            .Options);

    private static User MakeUser(string email, string displayName,
        string? given = null, string? family = null, DateTimeOffset? createdAt = null,
        DateTimeOffset? lastSeenAt = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = TestTenant,
            Email = email,
            DisplayName = displayName,
            GivenName = given,
            FamilyName = family,
            CreatedAt = createdAt ?? DateTimeOffset.Parse("2026-05-27T10:00:00Z"),
            LastSeenAt = lastSeenAt,
        };

    private static FakeTimeProvider Clock() =>
        new(DateTimeOffset.Parse("2026-05-27T10:00:00Z"));

    // ---- SearchAsync --------------------------------------------------------

    [TestMethod]
    public async Task SearchAsync_throws_ArgumentException_when_query_too_short()
    {
        await using var db = NewInMemory();
        var sut = new UserQueries(db);

        var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => sut.SearchAsync("a", 20, CancellationToken.None));

        Assert.AreEqual("q", ex.ParamName);
    }

    [TestMethod]
    public async Task SearchAsync_finds_users_by_email_substring()
    {
        await using var db = NewInMemory();
        db.Users.Add(MakeUser("alice@example.com", "Alice"));
        db.Users.Add(MakeUser("bob@other.org", "Bob"));
        await db.SaveChangesAsync();

        var sut = new UserQueries(db);
        var results = await sut.SearchAsync("example", 20, CancellationToken.None);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("alice@example.com", results[0].Email);
    }

    [TestMethod]
    public async Task SearchAsync_finds_users_by_display_name_substring()
    {
        await using var db = NewInMemory();
        db.Users.Add(MakeUser("a@x.com", "Alice Smith"));
        db.Users.Add(MakeUser("b@x.com", "Bob Jones"));
        await db.SaveChangesAsync();

        var sut = new UserQueries(db);
        var results = await sut.SearchAsync("Smith", 20, CancellationToken.None);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("Alice Smith", results[0].DisplayName);
    }

    [TestMethod]
    public async Task SearchAsync_is_case_insensitive()
    {
        await using var db = NewInMemory();
        db.Users.Add(MakeUser("alice@x.com", "Alice Smith"));
        await db.SaveChangesAsync();

        var sut = new UserQueries(db);
        var results = await sut.SearchAsync("ALI", 20, CancellationToken.None);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("Alice Smith", results[0].DisplayName);
    }

    [TestMethod]
    public async Task SearchAsync_respects_limit_cap_of_20()
    {
        await using var db = NewInMemory();
        for (var i = 0; i < 25; i++)
        {
            // Padded numeric suffix keeps display-name ordering deterministic so
            // we know we are taking a prefix of the sorted result set, not a
            // random slice — verifies Take() comes AFTER OrderBy().
            db.Users.Add(MakeUser($"match{i:00}@x.com", $"match-user-{i:00}"));
        }
        await db.SaveChangesAsync();

        var sut = new UserQueries(db);
        var results = await sut.SearchAsync("match", limit: 30, CancellationToken.None);

        Assert.AreEqual(20, results.Count);
    }

    [TestMethod]
    public async Task SearchAsync_returns_empty_when_no_match()
    {
        await using var db = NewInMemory();
        db.Users.Add(MakeUser("alice@x.com", "Alice"));
        await db.SaveChangesAsync();

        var sut = new UserQueries(db);
        var results = await sut.SearchAsync("zzzzz", 20, CancellationToken.None);

        Assert.AreEqual(0, results.Count);
    }

    // ---- GetDetailAsync -----------------------------------------------------

    [TestMethod]
    public async Task GetDetailAsync_returns_null_when_user_does_not_exist()
    {
        await using var db = NewInMemory();
        var sut = new UserQueries(db);

        var result = await sut.GetDetailAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task GetDetailAsync_returns_user_with_empty_team_list_when_no_memberships()
    {
        await using var db = NewInMemory();
        var user = MakeUser("alice@x.com", "Alice", "Alice", "Smith",
            createdAt: DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            lastSeenAt: DateTimeOffset.Parse("2026-05-01T00:00:00Z"));
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var sut = new UserQueries(db);
        var result = await sut.GetDetailAsync(user.Id, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(user.Id, result!.Id);
        Assert.AreEqual("alice@x.com", result.Email);
        Assert.AreEqual("Alice", result.DisplayName);
        Assert.AreEqual("Alice", result.GivenName);
        Assert.AreEqual("Smith", result.FamilyName);
        Assert.AreEqual(user.CreatedAt, result.CreatedAt);
        Assert.AreEqual(user.LastSeenAt, result.LastSeenAt);
        Assert.AreEqual(0, result.Teams.Count);
    }

    [TestMethod]
    public async Task GetDetailAsync_returns_user_with_team_memberships()
    {
        await using var db = NewInMemory();
        var clock = Clock();

        var team1 = Team.Create("Platform", "ops", TestTenant, clock);
        var team2 = Team.Create("Frontend", null, TestTenant, clock);
        db.Teams.Add(team1);
        db.Teams.Add(team2);

        var user = MakeUser("alice@x.com", "Alice", "Alice", "Smith");
        db.Users.Add(user);

        db.TeamMembers.Add(TeamMembership.Create(team1.Id, user.Id, TeamRole.Admin, clock));
        db.TeamMembers.Add(TeamMembership.Create(team2.Id, user.Id, TeamRole.Member, clock));

        await db.SaveChangesAsync();

        var sut = new UserQueries(db);
        var result = await sut.GetDetailAsync(user.Id, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(2, result!.Teams.Count);

        var platform = result.Teams.SingleOrDefault(t => t.TeamId == team1.Id.Value);
        Assert.IsNotNull(platform);
        Assert.AreEqual("Platform", platform!.TeamName);
        Assert.AreEqual("Admin", platform.Role);

        var frontend = result.Teams.SingleOrDefault(t => t.TeamId == team2.Id.Value);
        Assert.IsNotNull(frontend);
        Assert.AreEqual("Frontend", frontend!.TeamName);
        Assert.AreEqual("Member", frontend.Role);
    }
}
