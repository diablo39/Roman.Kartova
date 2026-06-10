using Kartova.Organization.Application;
using Kartova.Organization.Domain;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Kartova.Organization.Infrastructure.Tests;

/// <summary>
/// Behavioral unit tests for <see cref="OffboardMemberHandler"/> (slice-10 Task 6).
/// Covers each guard (not-found, self, invalid-successor, last-admin) asserting that NEITHER
/// the KeyCloak delete NOR the cross-module reassigner is invoked, plus the success path
/// (reassigner + KC delete called once with the right ids; target user + memberships removed).
/// In-memory <see cref="OrganizationDbContext"/> + NSubstitute mocks, same pattern as
/// <see cref="ChangeMemberRoleHandlerTests"/> / <c>CreateInvitationHandlerTests</c>.
/// </summary>
[TestClass]
public sealed class OffboardMemberHandlerTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());

    private static DbContextOptions<OrganizationDbContext> NewOptions() =>
        new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseInMemoryDatabase($"offboard-{Guid.NewGuid()}")
            .Options;

    private static User SeedUser(Guid id, string realmRole = KartovaRoles.Member) => new()
    {
        Id = id,
        TenantId = Tenant,
        Email = $"{id:N}@example.com",
        DisplayName = $"User {id:N}",
        CreatedAt = DateTimeOffset.UtcNow,
        RealmRole = realmRole,
    };

    private static (OffboardMemberHandler sut, IKeycloakAdminClient kc, IApplicationOwnerReassigner reassigner) MakeSut()
    {
        var kc = Substitute.For<IKeycloakAdminClient>();
        var reassigner = Substitute.For<IApplicationOwnerReassigner>();
        return (new OffboardMemberHandler(kc, reassigner), kc, reassigner);
    }

    private static async Task AssertNoSideEffects(IKeycloakAdminClient kc, IApplicationOwnerReassigner reassigner)
    {
        await kc.DidNotReceive().DeleteUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await reassigner.DidNotReceive().ReassignOwnerAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // Guard 1: unknown target → NotFoundResult; no side-effects
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task Unknown_target_returns_NotFound_and_no_side_effects()
    {
        await using var db = new OrganizationDbContext(NewOptions());
        var (sut, kc, reassigner) = MakeSut();

        var result = await sut.Handle(
            new OffboardMemberCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            db, CancellationToken.None);

        Assert.AreEqual(OffboardMemberResult.NotFoundResult, result);
        await AssertNoSideEffects(kc, reassigner);
    }

    // -------------------------------------------------------------------------
    // Guard 2: acting == target → SelfResult; no side-effects
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task Self_offboard_returns_CannotOffboardSelf_and_no_side_effects()
    {
        var opts = NewOptions();
        var userId = Guid.NewGuid();
        await using (var seedDb = new OrganizationDbContext(opts))
        {
            seedDb.Users.Add(SeedUser(userId));
            await seedDb.SaveChangesAsync();
        }

        await using var db = new OrganizationDbContext(opts);
        var (sut, kc, reassigner) = MakeSut();

        // ActingUserId == UserId → self.
        var result = await sut.Handle(
            new OffboardMemberCommand(userId, Guid.NewGuid(), userId),
            db, CancellationToken.None);

        Assert.AreEqual(OffboardMemberResult.SelfResult, result);
        await AssertNoSideEffects(kc, reassigner);
    }

    // -------------------------------------------------------------------------
    // Guard 3: successor not found → InvalidSuccessorResult; no side-effects
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task Unknown_successor_returns_InvalidSuccessor_and_no_side_effects()
    {
        var opts = NewOptions();
        var targetId = Guid.NewGuid();
        await using (var seedDb = new OrganizationDbContext(opts))
        {
            seedDb.Users.Add(SeedUser(targetId));
            await seedDb.SaveChangesAsync();
        }

        await using var db = new OrganizationDbContext(opts);
        var (sut, kc, reassigner) = MakeSut();

        // Successor id does not resolve to a seeded user.
        var result = await sut.Handle(
            new OffboardMemberCommand(targetId, Guid.NewGuid(), Guid.NewGuid()),
            db, CancellationToken.None);

        Assert.AreEqual(OffboardMemberResult.InvalidSuccessorResult, result);
        await AssertNoSideEffects(kc, reassigner);
    }

    [TestMethod]
    public async Task Successor_equal_to_target_returns_InvalidSuccessor_and_no_side_effects()
    {
        // Even when the id resolves to a real user, naming the offboarded user as their own
        // successor is rejected (you cannot reassign apps to the person being removed).
        var opts = NewOptions();
        var targetId = Guid.NewGuid();
        await using (var seedDb = new OrganizationDbContext(opts))
        {
            seedDb.Users.Add(SeedUser(targetId));
            await seedDb.SaveChangesAsync();
        }

        await using var db = new OrganizationDbContext(opts);
        var (sut, kc, reassigner) = MakeSut();

        var result = await sut.Handle(
            new OffboardMemberCommand(targetId, targetId, Guid.NewGuid()),
            db, CancellationToken.None);

        Assert.AreEqual(OffboardMemberResult.InvalidSuccessorResult, result);
        await AssertNoSideEffects(kc, reassigner);
    }

    // -------------------------------------------------------------------------
    // Guard 4: last OrgAdmin → LastOrgAdminResult; no side-effects
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task Last_orgadmin_target_returns_LastOrgAdmin_and_no_side_effects()
    {
        var opts = NewOptions();
        var adminId = Guid.NewGuid();
        var successorId = Guid.NewGuid();
        var actingId = Guid.NewGuid();

        await using (var seedDb = new OrganizationDbContext(opts))
        {
            // Exactly ONE OrgAdmin (the target). Successor is a Member, so it does not count.
            seedDb.Users.Add(SeedUser(adminId, KartovaRoles.OrgAdmin));
            seedDb.Users.Add(SeedUser(successorId, KartovaRoles.Member));
            await seedDb.SaveChangesAsync();
        }

        await using var db = new OrganizationDbContext(opts);
        var (sut, kc, reassigner) = MakeSut();

        var result = await sut.Handle(
            new OffboardMemberCommand(adminId, successorId, actingId),
            db, CancellationToken.None);

        Assert.AreEqual(OffboardMemberResult.LastOrgAdminResult, result);
        await AssertNoSideEffects(kc, reassigner);

        // Target must remain in the projection.
        Assert.IsTrue(await db.Users.AnyAsync(u => u.Id == adminId));
    }

    // -------------------------------------------------------------------------
    // Success: reassign + KC delete + remove memberships + remove user row
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task Success_reassigns_deletes_kc_user_and_removes_member_and_memberships()
    {
        var opts = NewOptions();
        var targetId = Guid.NewGuid();
        var successorId = Guid.NewGuid();
        var actingId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var teamId = new TeamId(Guid.NewGuid());
        var otherTeamId = new TeamId(Guid.NewGuid());
        var clock = TimeProvider.System;

        await using (var seedDb = new OrganizationDbContext(opts))
        {
            // Two OrgAdmins so the last-admin guard cannot fire when the target is an admin.
            seedDb.Users.Add(SeedUser(targetId, KartovaRoles.OrgAdmin));
            seedDb.Users.Add(SeedUser(successorId, KartovaRoles.OrgAdmin));
            seedDb.Users.Add(SeedUser(otherUserId, KartovaRoles.Member));

            // Two memberships for the target (must both be removed) + one for another user
            // (must be left intact, proving the WHERE filter is on UserId).
            seedDb.TeamMembers.Add(TeamMembership.Create(teamId, targetId, TeamRole.Member, clock));
            seedDb.TeamMembers.Add(TeamMembership.Create(otherTeamId, targetId, TeamRole.Admin, clock));
            seedDb.TeamMembers.Add(TeamMembership.Create(teamId, otherUserId, TeamRole.Member, clock));
            await seedDb.SaveChangesAsync();
        }

        await using var db = new OrganizationDbContext(opts);
        var (sut, kc, reassigner) = MakeSut();
        reassigner.ReassignOwnerAsync(targetId, successorId, Arg.Any<CancellationToken>()).Returns(2);

        var result = await sut.Handle(
            new OffboardMemberCommand(targetId, successorId, actingId),
            db, CancellationToken.None);

        Assert.IsTrue(result.Offboarded);
        Assert.AreEqual(2, result.AppsReassigned);

        // Cross-module reassignment: exactly once, target → successor.
        await reassigner.Received(1).ReassignOwnerAsync(targetId, successorId, Arg.Any<CancellationToken>());
        // KeyCloak identity deleted exactly once for the target.
        await kc.Received(1).DeleteUserAsync(targetId, Arg.Any<CancellationToken>());

        // Target user removed; successor + other user remain.
        Assert.IsFalse(await db.Users.AnyAsync(u => u.Id == targetId));
        Assert.IsTrue(await db.Users.AnyAsync(u => u.Id == successorId));
        Assert.IsTrue(await db.Users.AnyAsync(u => u.Id == otherUserId));

        // Both target memberships removed; the other user's membership survives.
        Assert.AreEqual(0, await db.TeamMembers.CountAsync(m => m.UserId == targetId));
        Assert.AreEqual(1, await db.TeamMembers.CountAsync(m => m.UserId == otherUserId));
    }

    // -------------------------------------------------------------------------
    // KC-failure rollback: DeleteUserAsync throws → exception propagates;
    // target row + memberships are NOT deleted (SaveChangesAsync never reached).
    // Pins spec §7.2 headline limitation: a KC outage cannot leave the DB in a
    // partially-offboarded state.
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task KeycloakDeleteFailure_propagates_exception_and_target_row_not_deleted()
    {
        // Test tier: handler unit test (in-memory OrganizationDbContext + NSubstitute).
        // The integration harness's KartovaApiFaultInjectionFixture can only inject a
        // failing ITenantScope — there is no hook to substitute IKeycloakAdminClient at
        // the integration layer, so a unit test is the correct and most targeted tier here.

        var opts = NewOptions();
        var targetId = Guid.NewGuid();
        var successorId = Guid.NewGuid();
        var actingId = Guid.NewGuid();
        var teamId = new TeamId(Guid.NewGuid());
        var clock = TimeProvider.System;

        await using (var seedDb = new OrganizationDbContext(opts))
        {
            // Two OrgAdmins so the last-admin guard does not fire.
            seedDb.Users.Add(SeedUser(targetId, KartovaRoles.OrgAdmin));
            seedDb.Users.Add(SeedUser(successorId, KartovaRoles.OrgAdmin));
            seedDb.TeamMembers.Add(TeamMembership.Create(teamId, targetId, TeamRole.Member, clock));
            await seedDb.SaveChangesAsync();
        }

        await using var db = new OrganizationDbContext(opts);
        var (sut, kc, reassigner) = MakeSut();

        // ReassignOwnerAsync succeeds (called before KC delete).
        reassigner.ReassignOwnerAsync(targetId, successorId, Arg.Any<CancellationToken>()).Returns(1);

        // DeleteUserAsync throws — simulates KC outage after the reassignment flush.
        kc.DeleteUserAsync(targetId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new KeycloakAdminException(KeycloakAdminError.Unexpected,
                "Simulated Keycloak outage"));

        // The exception must propagate — handler does NOT catch it (by design per spec §7.2).
        await Assert.ThrowsExactlyAsync<KeycloakAdminException>(
            () => sut.Handle(new OffboardMemberCommand(targetId, successorId, actingId),
                db, CancellationToken.None));

        // Reassigner was called (it ran before the KC delete).
        await reassigner.Received(1).ReassignOwnerAsync(targetId, successorId, Arg.Any<CancellationToken>());

        // Target user and membership are still present — SaveChangesAsync was never reached.
        Assert.IsTrue(await db.Users.AnyAsync(u => u.Id == targetId),
            "Target user row must still exist: SaveChangesAsync not reached after KC failure.");
        Assert.AreEqual(1, await db.TeamMembers.CountAsync(m => m.UserId == targetId),
            "Target membership must still exist: RemoveRange + SaveChangesAsync not reached.");
    }
}
