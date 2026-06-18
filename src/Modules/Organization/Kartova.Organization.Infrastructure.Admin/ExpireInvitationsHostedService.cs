using Kartova.Organization.Application;
using Kartova.Organization.Domain;
using Kartova.SharedKernel;
using Kartova.SharedKernel.Audit;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kartova.Organization.Infrastructure.Admin;

/// <summary>
/// Hourly leader-elected sweep that expires past-due pending invitations, deletes their
/// corresponding KeyCloak directory users, and records one tamper-evident
/// <c>invitation.expired</c> audit row per expiry as the <c>System</c> actor.
///
/// <para>Tenant enumeration is cross-tenant maintenance and uses the BYPASSRLS
/// <see cref="AdminOrganizationDbContext"/> (read-only). Each affected tenant is then processed
/// inside its own tenant scope via the app role (following the same per-tenant isolation pattern as <c>AuditCheckpointHostedService</c>),
/// so the invitation update + audit append both pass the RLS WITH CHECK and ride one transaction
/// — the sweep cannot expire or audit the wrong tenant even by mistake (ADR-0018 + ADR-0090).
/// The periodic job is the transport adapter here: it owns Begin/Commit; the writer/handler never
/// touch the scope.</para>
/// </summary>
public sealed class ExpireInvitationsHostedService(
    IServiceScopeFactory scopes,
    IDistributedLock locks,
    TimeProvider clock,
    ILogger<ExpireInvitationsHostedService> logger)
    : LeaderElectedPeriodicService(scopes, locks, clock, logger)
{
    private readonly IServiceScopeFactory _scopes = scopes;
    private readonly ILogger<ExpireInvitationsHostedService> _logger = logger;

    protected override string LockName => "expire-invitations";
    protected override TimeSpan Interval => TimeSpan.FromHours(1);

    protected override Task ExecuteLeaderWorkAsync(IServiceProvider services, CancellationToken ct)
        => ExpireDueAsync(services, ct);

    /// <summary>
    /// Exposed for direct integration testing — the base class wraps this in scope + lock
    /// setup, both of which are timing/integration concerns. <paramref name="services"/> must
    /// resolve <see cref="AdminOrganizationDbContext"/> for enumeration; per-tenant work runs
    /// in fresh scopes created from the injected <see cref="IServiceScopeFactory"/>.
    /// </summary>
    public async Task ExpireDueAsync(IServiceProvider services, CancellationToken ct)
    {
        var admin = services.GetRequiredService<AdminOrganizationDbContext>();
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();

        // Cross-tenant read: which tenants currently have a past-due pending invitation?
        // Materialize then dedupe in memory to avoid translating value-object projections.
        var dueTenantIds = (await admin.Invitations
                .Where(i => i.Status == InvitationStatus.Pending && i.ExpiresAt < now)
                .AsNoTracking()
                .Select(i => i.TenantId)
                .ToListAsync(ct))
            .Select(t => t.Value)
            .Distinct()
            .ToList();

        int tenants = 0, expired = 0, failed = 0;
        foreach (var tenantId in dueTenantIds)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                expired += await ProcessTenantAsync(tenantId, ct);
                tenants++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Isolate per-tenant failures (KC outage, transient DB error): one tenant's
                // failure must not abort the sweep for the others. Its txn rolls back (nothing
                // expired or audited for it); the next hourly tick retries. Matches
                // AuditCheckpointHostedService's per-tenant isolation.
                failed++;
                _logger.LogError(ex, "Invitation-expiry sweep errored for tenant {TenantId}.", tenantId);
            }
        }

        if (expired > 0 || failed > 0)
            _logger.LogInformation(
                "Invitation-expiry sweep: {Expired} expired across {Tenants} tenant(s), {Failed} errored.",
                expired, tenants, failed);
    }

    private async Task<int> ProcessTenantAsync(Guid tenantId, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var tenant = new TenantId(tenantId);

        // The periodic job is the transport adapter (ADR-0090): it owns Begin/Commit.
        var tenantScope = sp.GetRequiredService<ITenantScope>();
        await using var handle = await tenantScope.BeginAsync(tenant, ct);

        var db = sp.GetRequiredService<OrganizationDbContext>();
        var kc = sp.GetRequiredService<IKeycloakAdminClient>();
        var audit = sp.GetRequiredService<IAuditWriter>();
        var workClock = sp.GetRequiredService<TimeProvider>();
        var now = workClock.GetUtcNow();

        // Re-read through the RLS context: SET LOCAL scopes this to the current tenant, and the
        // Status re-filter ignores any invitation accepted/revoked since enumeration.
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
                // Non-NotFound KC errors propagate, rolling back this tenant's txn; the
                // outer loop catches + isolates them. The KC delete already happened, but
                // the next tick re-deletes (NotFound swallowed) and retries — no partial state.
            }

            inv.MarkExpired(workClock);
            await audit.AppendSystemAsync(tenant, new AuditEntry(
                OrganizationAuditActions.InvitationExpired,
                AuditTargetTypes.Invitation,
                inv.Id.Value.ToString(),
                new Dictionary<string, string?> { ["email"] = inv.Email, ["role"] = inv.Role }), ct);
        }

        if (due.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
        await handle.CommitAsync(ct);
        return due.Count;
    }
}
