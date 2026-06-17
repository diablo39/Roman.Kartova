using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kartova.Organization.Application;
using Kartova.Organization.Contracts;
using Kartova.Organization.Infrastructure;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Multitenancy;
using Kartova.Testing.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kartova.Organization.IntegrationTests;

[TestClass]
public sealed class AuditWiringTests : OrganizationIntegrationTestBase
{
    // --- Happy: role change writes a correct, chained audit row ---
    [TestMethod]
    public async Task ChangeRole_WritesMemberRoleChangedAuditRow()
    {
        var (admin, adminEmail, target, tenantId) = await SeedAdminAndMemberAsync(nameClaim: "Ada Lovelace");
        Guid? kcUserId = target;

        try
        {
            // Act
            var resp = await admin.PutAsJsonAsync(
                $"/api/v1/organizations/users/{target}/role", new { role = "OrgAdmin" });
            Assert.AreEqual(HttpStatusCode.NoContent, resp.StatusCode,
                $"Expected 204. Body: {await resp.Content.ReadAsStringAsync()}");

            // Assert
            var rows = await Fx.ReadAuditLogAsync(tenantId);
            var row = rows.Single(r => r.Action == OrganizationAuditActions.MemberRoleChanged);
            Assert.AreEqual(await Fx.GetSubClaimAsync(adminEmail), row.ActorId);
            Assert.AreEqual("Ada Lovelace", row.ActorDisplay);
            Assert.AreEqual(AuditTargetTypes.User, row.TargetType);
            Assert.AreEqual(target.ToString(), row.TargetId);
            using var data = JsonDocument.Parse(row.DataJson!);
            Assert.AreEqual("Member", data.RootElement.GetProperty("oldRole").GetString());
            Assert.AreEqual("OrgAdmin", data.RootElement.GetProperty("newRole").GetString());
            AssertChainLinked(rows);
        }
        finally
        {
            await CleanupAsync(tenantId, kcUserId);
        }
    }

    // --- Happy: offboard snapshot survives the target's hard-delete ---
    [TestMethod]
    public async Task Offboard_WritesSnapshotThatSurvivesTargetDeletion()
    {
        var (admin, adminEmail, target, tenantId) = await SeedAdminAndMemberAsync(nameClaim: "Grace H");
        Guid? kcUserId = target;

        try
        {
            var resp = await admin.DeleteAsync($"/api/v1/organizations/users/{target}");
            Assert.AreEqual(HttpStatusCode.NoContent, resp.StatusCode,
                $"Expected 204. Body: {await resp.Content.ReadAsStringAsync()}");

            var rows = await Fx.ReadAuditLogAsync(tenantId);
            var row = rows.Single(r => r.Action == OrganizationAuditActions.MemberOffboarded);
            Assert.AreEqual(target.ToString(), row.TargetId);
            Assert.AreEqual(await Fx.GetSubClaimAsync(adminEmail), row.ActorId);
            Assert.AreEqual("Grace H", row.ActorDisplay);
            using var data = JsonDocument.Parse(row.DataJson!);
            Assert.IsFalse(string.IsNullOrEmpty(data.RootElement.GetProperty("email").GetString()));
            Assert.IsFalse(string.IsNullOrEmpty(data.RootElement.GetProperty("displayName").GetString()));
            // The users row was hard-deleted in the same txn, yet the snapshot persisted.
            AssertChainLinked(rows);
        }
        finally
        {
            await CleanupAsync(tenantId, kcUserId);
        }
    }

    // --- Negative: a rejected mutation writes no audit row ---
    [TestMethod]
    public async Task ChangeRole_LastOrgAdminGuard_WritesNoAuditRow()
    {
        var (admin, _, soleAdminId, tenantId) = await SeedSoleOrgAdminAsync();

        try
        {
            var resp = await admin.PutAsJsonAsync(
                $"/api/v1/organizations/users/{soleAdminId}/role", new { role = "Member" });
            Assert.AreEqual(HttpStatusCode.Conflict, resp.StatusCode,
                $"Expected 409. Body: {await resp.Content.ReadAsStringAsync()}");

            var rows = await Fx.ReadAuditLogAsync(tenantId);
            Assert.AreEqual(0, rows.Count(r => r.Action == OrganizationAuditActions.MemberRoleChanged));
        }
        finally
        {
            await Fx.DeleteUserInOrganizationAsync(soleAdminId);
            await Fx.DeleteOrganizationsForTenantAsync(tenantId);
        }
    }

    private static void AssertChainLinked(IReadOnlyList<KartovaApiFixture.AuditRowRecord> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            Assert.AreEqual(i + 1, rows[i].Seq, "seq must be contiguous from 1");
            var expectedPrev = i == 0 ? new byte[32] : rows[i - 1].RowHash;
            CollectionAssert.AreEqual(expectedPrev, rows[i].PrevHash, "prev_hash must link to predecessor row_hash");
        }
    }

#pragma warning disable CA1031
    private static async Task CleanupAsync(Guid tenantId, Guid? kcUserId)
    {
        try { await Fx.DeleteInvitationsForTenantAsync(tenantId); }
        catch (Exception ex) { await Console.Error.WriteLineAsync($"[cleanup] invitations {tenantId}: {ex.Message}"); }
        if (kcUserId is not null)
        {
            try { await Fx.DeleteUserInOrganizationAsync(kcUserId.Value); }
            catch (Exception ex) { await Console.Error.WriteLineAsync($"[cleanup] user {kcUserId}: {ex.Message}"); }
            try
            {
                using var scope = Fx.Services.CreateScope();
                var kc = scope.ServiceProvider.GetRequiredService<IKeycloakAdminClient>();
                await kc.DeleteUserAsync(kcUserId.Value, CancellationToken.None);
            }
            catch (Exception ex) { await Console.Error.WriteLineAsync($"[cleanup] kc user {kcUserId}: {ex.Message}"); }
        }
        try { await Fx.DeleteOrganizationsForTenantAsync(tenantId); }
        catch (Exception ex) { await Console.Error.WriteLineAsync($"[cleanup] org {tenantId}: {ex.Message}"); }
    }
#pragma warning restore CA1031

    /// <summary>
    /// Provisions an OrgAdmin client (with nameClaim) + a real KC Member via the invitation flow.
    /// Returns (adminClient, adminEmail, kcMemberUserId, tenantId).
    /// Caller is responsible for cleanup in finally.
    /// </summary>
    private static async Task<(HttpClient Admin, string AdminEmail, Guid TargetUserId, Guid TenantId)>
        SeedAdminAndMemberAsync(string nameClaim)
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var (adminEmail, tenantId) = await NewTenantAsync($"audit-wiring-{unique}");

        // Seed a second OrgAdmin (projection only) so the last-admin guard cannot fire.
        await Fx.SeedUserInOrganizationAsync(
            new TenantId(tenantId),
            $"Guard Admin-{unique}",
            $"guard-admin-{unique}@audit-wiring-{unique}.kartova.local",
            KartovaRoles.OrgAdmin);

        var adminClient = await Fx.CreateAuthenticatedClientAsync(
            adminEmail, new[] { KartovaRoles.OrgAdmin }, nameClaim: nameClaim);

        // Provision a real KC user via the invitation flow.
        var memberEmail = $"member-{unique}@audit-wiring-{unique}.kartova.local";
        var inviteResp = await adminClient.PostAsJsonAsync(
            "/api/v1/organizations/invitations",
            new CreateInvitationRequest(memberEmail, KartovaRoles.Member));
        Assert.AreEqual(HttpStatusCode.Created, inviteResp.StatusCode,
            $"Expected 201 from CreateInvitation. Body: {await inviteResp.Content.ReadAsStringAsync()}");

        var inviteBody = await inviteResp.Content.ReadFromJsonAsync<CreateInvitationResponse>(
            KartovaApiFixtureBase.WireJson);
        Assert.IsNotNull(inviteBody);

        Guid kcUserId;
        await using (var db = new OrganizationDbContext(BypassOptions()))
        {
            var id = await db.Invitations
                .Where(i => EF.Property<Guid>(i, "_id") == inviteBody!.Invitation.Id)
                .Select(i => i.KeycloakUserId)
                .SingleAsync();
            Assert.IsNotNull(id, "Invitation must have a KC user id.");
            kcUserId = id!.Value;
        }

        return (adminClient, adminEmail, kcUserId, tenantId);
    }

    /// <summary>
    /// Creates a tenant with exactly ONE OrgAdmin (projection-only, no real KC user).
    /// Returns (adminClient, adminEmail, orgAdminId, tenantId).
    /// </summary>
    private static async Task<(HttpClient Admin, string AdminEmail, Guid OrgAdminId, Guid TenantId)>
        SeedSoleOrgAdminAsync()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var (adminEmail, tenantId) = await NewTenantAsync($"audit-sole-{unique}");

        var orgAdminId = await Fx.SeedUserInOrganizationAsync(
            new TenantId(tenantId),
            $"Solo Admin-{unique}",
            $"solo-admin-{unique}@audit-sole-{unique}.kartova.local",
            KartovaRoles.OrgAdmin);

        var adminClient = await Fx.CreateAuthenticatedClientAsync(adminEmail, new[] { KartovaRoles.OrgAdmin });

        return (adminClient, adminEmail, orgAdminId, tenantId);
    }
}
