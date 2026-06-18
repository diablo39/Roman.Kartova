using System.Text.Json;
using Kartova.Organization.Application;
using Kartova.Organization.Domain;
using Kartova.Organization.Infrastructure;
using Kartova.Organization.Infrastructure.Admin;
using Kartova.SharedKernel;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Kartova.Organization.IntegrationTests;

/// <summary>
/// Gate-5 real-seam tests for the invitation-expiry sweep's audit wiring (design §8):
/// the sweep records one System-actor <c>invitation.expired</c> row per expired invitation,
/// inside each tenant's RLS-scoped chain. Drives the public <c>ExpireDueAsync</c> work method
/// against the running API host's service provider (mirrors AuditCheckpointHostedServiceTests).
/// Seeded invitations carry a random KC user id that does not exist in the Keycloak container,
/// so the real <c>IKeycloakAdminClient.DeleteUserAsync</c> returns NotFound (swallowed) — which
/// also exercises the idempotent-delete path.
/// </summary>
[TestClass]
public sealed class InvitationExpirySweepAuditTests : OrganizationIntegrationTestBase
{
    private static ExpireInvitationsHostedService NewSweep() => new(
        Fx.Services.GetRequiredService<IServiceScopeFactory>(),
        Substitute.For<IDistributedLock>(),               // unused by ExpireDueAsync
        Fx.Services.GetRequiredService<TimeProvider>(),   // unused by ExpireDueAsync (timer only)
        NullLogger<ExpireInvitationsHostedService>.Instance);

    private static async Task RunSweepAsync()
    {
        using var scope = Fx.Services.CreateScope();
        await NewSweep().ExpireDueAsync(scope.ServiceProvider, CancellationToken.None);
    }

    private static async Task<InvitationStatus> ReadStatusAsync(Guid invitationId)
    {
        await using var db = new OrganizationDbContext(BypassOptions());
        return await db.Invitations
            .Where(i => EF.Property<Guid>(i, "_id") == invitationId)
            .Select(i => i.Status)
            .SingleAsync();
    }

    // --- Happy: a past-due invitation is expired and audited as System ---
    [TestMethod]
    public async Task Sweep_expires_pastdue_invitation_and_writes_System_audit_row()
    {
        var (_, tenantId) = await NewTenantAsync("expire-happy");
        var pastInvitedAt = DateTimeOffset.UtcNow.AddDays(-8);
        var invitationId = await Fx.SeedInvitationAsync(
            tenantId, "expiree@expire-happy.kartova.local", KartovaRoles.Member,
            invitedByUserId: Guid.NewGuid(), keycloakUserId: Guid.NewGuid(),
            invitedAt: pastInvitedAt, expiresAt: pastInvitedAt.AddDays(7)); // expired 1 day ago

        try
        {
            await RunSweepAsync();

            Assert.AreEqual(InvitationStatus.Expired, await ReadStatusAsync(invitationId));

            var rows = await Fx.ReadAuditLogAsync(tenantId);
            var row = rows.Single(r => r.Action == OrganizationAuditActions.InvitationExpired);
            Assert.AreEqual("System", row.ActorType);
            Assert.IsNull(row.ActorId, "System actor row must have NULL actor_id");
            Assert.AreEqual("System", row.ActorDisplay);
            Assert.AreEqual(AuditTargetTypes.Invitation, row.TargetType);
            Assert.AreEqual(invitationId.ToString(), row.TargetId);
            using var data = JsonDocument.Parse(row.DataJson!);
            Assert.AreEqual("expiree@expire-happy.kartova.local", data.RootElement.GetProperty("email").GetString());
            Assert.AreEqual(KartovaRoles.Member, data.RootElement.GetProperty("role").GetString());
        }
        finally
        {
            await Fx.DeleteInvitationsForTenantAsync(tenantId);
            await Fx.DeleteOrganizationsForTenantAsync(tenantId);
        }
    }

    // --- Multi-tenant: each expiry lands only in its own tenant's chain ---
    [TestMethod]
    public async Task Sweep_isolates_audit_rows_per_tenant()
    {
        var (_, tenantA) = await NewTenantAsync("expire-iso-a");
        var (_, tenantB) = await NewTenantAsync("expire-iso-b");
        var past = DateTimeOffset.UtcNow.AddDays(-8);
        var invA = await Fx.SeedInvitationAsync(tenantA, "a@expire-iso-a.kartova.local", KartovaRoles.Member,
            Guid.NewGuid(), Guid.NewGuid(), past, past.AddDays(7));
        var invB = await Fx.SeedInvitationAsync(tenantB, "b@expire-iso-b.kartova.local", KartovaRoles.Viewer,
            Guid.NewGuid(), Guid.NewGuid(), past, past.AddDays(7));

        try
        {
            await RunSweepAsync();

            var rowsA = await Fx.ReadAuditLogAsync(tenantA);
            var rowsB = await Fx.ReadAuditLogAsync(tenantB);

            var rowA = rowsA.Single(r => r.Action == OrganizationAuditActions.InvitationExpired);
            Assert.AreEqual(invA.ToString(), rowA.TargetId);
            Assert.IsFalse(rowsA.Any(r => r.TargetId == invB.ToString()), "tenant A chain must not contain tenant B's row");

            var rowB = rowsB.Single(r => r.Action == OrganizationAuditActions.InvitationExpired);
            Assert.AreEqual(invB.ToString(), rowB.TargetId);
            Assert.IsFalse(rowsB.Any(r => r.TargetId == invA.ToString()), "tenant B chain must not contain tenant A's row");
        }
        finally
        {
            await Fx.DeleteInvitationsForTenantAsync(tenantA);
            await Fx.DeleteInvitationsForTenantAsync(tenantB);
            await Fx.DeleteOrganizationsForTenantAsync(tenantA);
            await Fx.DeleteOrganizationsForTenantAsync(tenantB);
        }
    }

    // --- Negative: a not-yet-due invitation is left alone and writes no row ---
    [TestMethod]
    public async Task Sweep_leaves_future_invitation_pending_and_writes_no_row()
    {
        var (_, tenantId) = await NewTenantAsync("expire-future");
        var invitationId = await Fx.SeedInvitationAsync(
            tenantId, "future@expire-future.kartova.local", KartovaRoles.Member,
            invitedByUserId: Guid.NewGuid(), keycloakUserId: Guid.NewGuid(),
            invitedAt: DateTimeOffset.UtcNow, expiresAt: DateTimeOffset.UtcNow.AddDays(7)); // not due

        try
        {
            await RunSweepAsync();

            Assert.AreEqual(InvitationStatus.Pending, await ReadStatusAsync(invitationId));
            var rows = await Fx.ReadAuditLogAsync(tenantId);
            Assert.AreEqual(0, rows.Count(r => r.Action == OrganizationAuditActions.InvitationExpired),
                "a non-due invitation must not be audited");
        }
        finally
        {
            await Fx.DeleteInvitationsForTenantAsync(tenantId);
            await Fx.DeleteOrganizationsForTenantAsync(tenantId);
        }
    }

    // --- An accepted invitation is never re-expired, even past its ExpiresAt ---
    [TestMethod]
    public async Task Sweep_leaves_accepted_invitation_untouched_even_if_past_due()
    {
        var (_, tenantId) = await NewTenantAsync("expire-accepted");
        var past = DateTimeOffset.UtcNow.AddDays(-8);
        var invitationId = await Fx.SeedAcceptedInvitationAsync(
            tenantId, "accepted@expire-accepted.kartova.local", KartovaRoles.Member,
            invitedAt: past, expiresAt: past.AddDays(7)); // expired 1 day ago, but Accepted

        try
        {
            await RunSweepAsync();

            Assert.AreEqual(InvitationStatus.Accepted, await ReadStatusAsync(invitationId));
            var rows = await Fx.ReadAuditLogAsync(tenantId);
            Assert.AreEqual(0, rows.Count(r => r.Action == OrganizationAuditActions.InvitationExpired),
                "an accepted invitation must not produce an expiry audit row");
        }
        finally
        {
            await Fx.DeleteInvitationsForTenantAsync(tenantId);
            await Fx.DeleteOrganizationsForTenantAsync(tenantId);
        }
    }

    // --- Idempotency: a second sweep does not re-expire or double-audit ---
    [TestMethod]
    public async Task Sweep_run_twice_does_not_double_audit()
    {
        var (_, tenantId) = await NewTenantAsync("expire-twice");
        var past = DateTimeOffset.UtcNow.AddDays(-8);
        await Fx.SeedInvitationAsync(tenantId, "twice@expire-twice.kartova.local", KartovaRoles.Member,
            Guid.NewGuid(), Guid.NewGuid(), past, past.AddDays(7));

        try
        {
            await RunSweepAsync();
            await RunSweepAsync(); // second tick: invitation is no longer Pending → nothing to do

            var rows = await Fx.ReadAuditLogAsync(tenantId);
            Assert.AreEqual(1, rows.Count(r => r.Action == OrganizationAuditActions.InvitationExpired),
                "the second sweep must not write a second invitation.expired row");
        }
        finally
        {
            await Fx.DeleteInvitationsForTenantAsync(tenantId);
            await Fx.DeleteOrganizationsForTenantAsync(tenantId);
        }
    }
}
