using Kartova.Organization.Domain;
using Kartova.SharedKernel;
using Kartova.SharedKernel.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kartova.Organization.Infrastructure.Admin;

/// <summary>
/// Hourly leader-elected sweep that expires past-due pending invitations and
/// deletes their corresponding KeyCloak directory users — slice 9 spec §6.9.
/// Lives in <c>Kartova.Organization.Infrastructure.Admin</c> because it depends on
/// <see cref="AdminOrganizationDbContext"/> (BYPASSRLS pool) which is also defined
/// here; <c>Kartova.Organization.Infrastructure</c> cannot reference Admin without
/// creating a circular project reference.
/// </summary>
public sealed class ExpireInvitationsHostedService(
    IServiceScopeFactory scopes,
    IDistributedLock locks,
    TimeProvider clock,
    ILogger<ExpireInvitationsHostedService> logger)
    : LeaderElectedPeriodicService(scopes, locks, clock, logger)
{
    private readonly ILogger<ExpireInvitationsHostedService> _logger = logger;

    protected override string LockName => "expire-invitations";
    protected override TimeSpan Interval => TimeSpan.FromHours(1);

    protected override Task ExecuteLeaderWorkAsync(IServiceProvider services, CancellationToken ct)
        => ExpireDueAsync(services, ct);

    /// <summary>
    /// Exposed for direct unit testing — the base class wraps this in scope + lock
    /// setup, both of which are timing/integration concerns. Tests can call this
    /// method with a constructed <see cref="IServiceProvider"/> that resolves
    /// <see cref="AdminOrganizationDbContext"/>, <see cref="IKeycloakAdminClient"/>,
    /// and <see cref="TimeProvider"/>.
    /// </summary>
    public async Task ExpireDueAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<AdminOrganizationDbContext>();
        var kc = services.GetRequiredService<IKeycloakAdminClient>();
        var workClock = services.GetRequiredService<TimeProvider>();
        var now = workClock.GetUtcNow();

        var due = await db.Invitations
            .Where(i => i.Status == InvitationStatus.Pending && i.ExpiresAt < now)
            .ToListAsync(ct);

        foreach (var inv in due)
        {
            if (inv.KeycloakUserId is { } kid)
            {
                try
                {
                    await kc.DeleteUserAsync(kid, ct);
                }
                catch (KeycloakAdminException ex) when (ex.Error == KeycloakAdminError.NotFound)
                {
                    // Idempotent: the KC user is already gone, which is the desired end state.
                }
                // Non-NotFound KC errors propagate, aborting the loop before SaveChangesAsync.
                // No partial commit — the next tick retries. Matches RevokeInvitationHandler's
                // posture on the same KC failure class.
            }
            inv.MarkExpired(workClock);
        }

        if (due.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Expired {Count} invitations.", due.Count);
        }
    }
}
