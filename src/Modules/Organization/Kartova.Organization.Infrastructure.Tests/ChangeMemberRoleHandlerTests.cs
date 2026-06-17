using Kartova.Organization.Application;
using Kartova.Organization.Domain;
using Kartova.SharedKernel.Audit;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Kartova.Organization.Infrastructure.Tests;

/// <summary>
/// Behavioral unit tests for <see cref="ChangeMemberRoleHandler"/> — slice-10
/// Task 5 review fix. Covers guard short-circuiting (KC not called before
/// guards), last-admin projection unchanged, and write-through on success.
/// Uses in-memory <see cref="OrganizationDbContext"/> + NSubstitute KC mock.
/// </summary>
[TestClass]
public sealed class ChangeMemberRoleHandlerTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());

    private static DbContextOptions<OrganizationDbContext> NewOptions() =>
        new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseInMemoryDatabase($"role-{Guid.NewGuid()}")
            .Options;

    private static User SeedUser(Guid id, string realmRole) => new()
    {
        Id = id,
        TenantId = Tenant,
        Email = $"{id:N}@example.com",
        DisplayName = $"User {id:N}",
        CreatedAt = DateTimeOffset.UtcNow,
        RealmRole = realmRole,
    };

    // -------------------------------------------------------------------------
    // Test 1: invalid role → InvalidRoleResult; KC never called
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task Invalid_role_returns_InvalidRoleResult_and_KC_not_called()
    {
        await using var db = new OrganizationDbContext(NewOptions());
        var kc = Substitute.For<IKeycloakAdminClient>();
        var sut = new ChangeMemberRoleHandler(kc, Substitute.For<IAuditWriter>());

        var result = await sut.Handle(
            new ChangeMemberRoleCommand(Guid.NewGuid(), "BogusRole"), db, CancellationToken.None);

        Assert.AreEqual(ChangeMemberRoleOutcome.InvalidRole, result);
        await kc.DidNotReceive().ChangeRealmRoleAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // Test 2: no user row → NotFoundResult; KC never called
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task Not_found_returns_NotFoundResult_and_KC_not_called()
    {
        await using var db = new OrganizationDbContext(NewOptions());
        var kc = Substitute.For<IKeycloakAdminClient>();
        var sut = new ChangeMemberRoleHandler(kc, Substitute.For<IAuditWriter>());

        var result = await sut.Handle(
            new ChangeMemberRoleCommand(Guid.NewGuid(), KartovaRoles.Member), db, CancellationToken.None);

        Assert.AreEqual(ChangeMemberRoleOutcome.NotFound, result);
        await kc.DidNotReceive().ChangeRealmRoleAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // Test 3: last-admin demotion → LastOrgAdminResult; KC not called; projection unchanged
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task Last_admin_demotion_returns_LastOrgAdminResult_and_projection_unchanged()
    {
        var opts = NewOptions();
        var userId = Guid.NewGuid();

        // Seed exactly ONE OrgAdmin — the guard must fire.
        await using (var seedDb = new OrganizationDbContext(opts))
        {
            seedDb.Users.Add(SeedUser(userId, KartovaRoles.OrgAdmin));
            await seedDb.SaveChangesAsync();
        }

        var kc = Substitute.For<IKeycloakAdminClient>();
        await using var db = new OrganizationDbContext(opts);
        var sut = new ChangeMemberRoleHandler(kc, Substitute.For<IAuditWriter>());

        var result = await sut.Handle(
            new ChangeMemberRoleCommand(userId, KartovaRoles.Member), db, CancellationToken.None);

        Assert.AreEqual(ChangeMemberRoleOutcome.LastOrgAdmin, result);

        // KC must not be called — guard fires before the side-effect.
        await kc.DidNotReceive().ChangeRealmRoleAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        // Write-through must NOT have mutated the projection.
        var user = await db.Users.SingleAsync(u => u.Id == userId);
        Assert.AreEqual(KartovaRoles.OrgAdmin, user.RealmRole,
            "Projection must remain OrgAdmin after last-admin guard fires.");
    }

    // -------------------------------------------------------------------------
    // Test 4: success → Success result; KC called once; projection updated
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task Promote_to_orgadmin_returns_Success_calls_KC_and_updates_projection()
    {
        var opts = NewOptions();
        var userId = Guid.NewGuid();

        // Seed a Member — promoting to OrgAdmin will never trip the last-admin guard.
        await using (var seedDb = new OrganizationDbContext(opts))
        {
            seedDb.Users.Add(SeedUser(userId, KartovaRoles.Member));
            await seedDb.SaveChangesAsync();
        }

        var kc = Substitute.For<IKeycloakAdminClient>();
        await using var db = new OrganizationDbContext(opts);
        var sut = new ChangeMemberRoleHandler(kc, Substitute.For<IAuditWriter>());

        var result = await sut.Handle(
            new ChangeMemberRoleCommand(userId, KartovaRoles.OrgAdmin), db, CancellationToken.None);

        Assert.AreEqual(ChangeMemberRoleOutcome.Success, result);

        // KC must have been called exactly once with the correct arguments.
        await kc.Received(1).ChangeRealmRoleAsync(
            userId, KartovaRoles.OrgAdmin, Arg.Any<CancellationToken>());

        // Write-through: projection row must reflect the new role.
        var user = await db.Users.SingleAsync(u => u.Id == userId);
        Assert.AreEqual(KartovaRoles.OrgAdmin, user.RealmRole,
            "Projection must be updated to OrgAdmin after successful role change.");
    }
}
