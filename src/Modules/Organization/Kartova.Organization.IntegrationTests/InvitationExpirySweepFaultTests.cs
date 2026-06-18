using System.Diagnostics.CodeAnalysis;
using Kartova.Organization.Application;
using Kartova.Testing.Auth;
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
/// Fail-closed regression for the invitation-expiry sweep on the fault-injection host:
/// when a tenant's ITenantScope.CommitAsync throws (CommitFailFlag), the sweep's per-tenant
/// try/catch must isolate it — the invitation stays Pending and NO invitation.expired audit
/// row is written (MarkExpired + audit append both roll back). Replaces the coverage of the
/// deleted unit test's "no partial commit on mid-sweep failure" invariant.
/// </summary>
[TestClass]
[ExcludeFromCodeCoverage]
public sealed class InvitationExpirySweepFaultTests : OrganizationFaultInjectionTestBase
{
    private static ExpireInvitationsHostedService NewSweep() => new(
        Fx.Services.GetRequiredService<IServiceScopeFactory>(),
        Substitute.For<IDistributedLock>(),
        Fx.Services.GetRequiredService<TimeProvider>(),
        NullLogger<ExpireInvitationsHostedService>.Instance);

    [TestMethod]
    public async Task Sweep_is_failclosed_when_commit_throws()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var adminEmail = $"admin@expire-fault-{unique}.kartova.local";
        var tenantId = KartovaApiFixtureBase.TenantFor(adminEmail).Value;
        await Fx.SeedOrganizationAsync(tenantId, $"Org-expire-fault-{unique}");
        var past = DateTimeOffset.UtcNow.AddDays(-8);
        var invitationId = await Fx.SeedInvitationAsync(
            tenantId, $"invitee@expire-fault-{unique}.kartova.local", KartovaRoles.Member,
            invitedByUserId: Guid.NewGuid(), keycloakUserId: Guid.NewGuid(),
            invitedAt: past, expiresAt: past.AddDays(7));

        Fx.CommitFailFlag.Fail = true;
        try
        {
            // Must NOT throw — the per-tenant catch isolates the commit failure.
            using var scope = Fx.Services.CreateScope();
            await NewSweep().ExpireDueAsync(scope.ServiceProvider, CancellationToken.None);

            await using var db = new OrganizationDbContext(
                new DbContextOptionsBuilder<OrganizationDbContext>().UseNpgsql(Fx.BypassConnectionString).Options);
            var status = await db.Invitations
                .Where(i => EF.Property<Guid>(i, "_id") == invitationId)
                .Select(i => i.Status).SingleAsync();
            Assert.AreEqual(InvitationStatus.Pending, status, "fail-closed: a commit failure must roll back MarkExpired");

            var rows = await Fx.ReadAuditLogAsync(tenantId);
            Assert.AreEqual(0, rows.Count(r => r.Action == OrganizationAuditActions.InvitationExpired),
                "fail-closed: no audit row may survive a rolled-back tenant txn");
        }
        finally
        {
            Fx.CommitFailFlag.Fail = false; // MUST reset — shared singleton on the fault host
            await Fx.DeleteInvitationsForTenantAsync(tenantId);
            await Fx.DeleteOrganizationsForTenantAsync(tenantId);
        }
    }
}
