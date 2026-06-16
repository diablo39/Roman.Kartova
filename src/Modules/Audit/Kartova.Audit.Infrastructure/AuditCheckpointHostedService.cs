using Kartova.Audit.Domain;
using Kartova.SharedKernel;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kartova.Audit.Infrastructure;

/// <summary>
/// Leader-elected periodic sweep (ADR-0099) that checkpoints each tenant's audit chain (ADR-0105),
/// so routine verification can re-walk only the tail since the last checkpoint instead of the whole
/// chain. Runs daily; only one replica wins the <c>audit-checkpoint</c> advisory lock per tick.
///
/// <para>Tenant enumeration is cross-tenant maintenance and uses the BYPASSRLS
/// <see cref="AdminAuditDbContext"/> (read-only). Each due tenant is then checkpointed inside its own
/// tenant scope via the app role, so the checkpoint INSERT still passes the RLS WITH CHECK — the
/// sweep cannot write a checkpoint for the wrong tenant even by mistake — and the per-tenant work
/// reuses <see cref="AuditCheckpointer"/> unchanged.</para>
/// </summary>
public sealed class AuditCheckpointHostedService(
    IServiceScopeFactory scopes,
    IDistributedLock locks,
    TimeProvider clock,
    ILogger<AuditCheckpointHostedService> logger)
    : LeaderElectedPeriodicService(scopes, locks, clock, logger)
{
    private readonly IServiceScopeFactory _scopes = scopes;
    private readonly ILogger<AuditCheckpointHostedService> _logger = logger;

    protected override string LockName => "audit-checkpoint";

    // Daily cadence: checkpoints are a verification-cost optimization, not time-critical, and sit
    // squarely in the hourly/daily tick envelope ADR-0099's advisory-lock primitive was designed for.
    protected override TimeSpan Interval => TimeSpan.FromHours(24);

    protected override Task ExecuteLeaderWorkAsync(IServiceProvider services, CancellationToken ct)
        => CheckpointAllTenantsAsync(services, ct);

    /// <summary>
    /// Public for direct testing — the base class wraps this in scope + lock acquisition, which are
    /// timing/integration concerns. <paramref name="services"/> must resolve
    /// <see cref="AdminAuditDbContext"/> for enumeration; per-tenant work runs in fresh scopes.
    /// </summary>
    public async Task CheckpointAllTenantsAsync(IServiceProvider services, CancellationToken ct)
    {
        var due = await FindTenantsNeedingCheckpointAsync(services, ct);

        int created = 0, broken = 0, failed = 0;
        foreach (var tenantId in due)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var result = await CheckpointTenantAsync(tenantId, ct);
                switch (result.Outcome)
                {
                    case AuditCheckpointOutcome.Created:
                        created++;
                        break;
                    case AuditCheckpointOutcome.ChainBroken:
                        broken++;
                        // A detected break in a tamper-evident compliance log is the highest-severity
                        // event this sweep can observe — it must never share the silent UpToDate path.
                        _logger.LogCritical(
                            "Audit chain BROKEN for tenant {TenantId} at seq {Seq}: {Reason}. No checkpoint written.",
                            tenantId, result.Verification.FirstBrokenSeq, result.Verification.Reason);
                        break;
                    case AuditCheckpointOutcome.UpToDate:
                        break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Isolate per-tenant failures: a transient error on one tenant must not abort the
                // sweep for the others. The next tick retries. (A broken chain is NOT an exception —
                // it is the ChainBroken outcome above, logged at Critical.)
                failed++;
                _logger.LogError(ex, "Audit checkpoint sweep errored for tenant {TenantId}.", tenantId);
            }
        }

        if (created > 0 || broken > 0 || failed > 0)
            _logger.LogInformation(
                "Audit checkpoint sweep: {Created} created, {Broken} broken chain(s), {Failed} errored.",
                created, broken, failed);
    }

    // Tenants whose chain head has advanced past their latest checkpoint (or that have none).
    private static async Task<List<Guid>> FindTenantsNeedingCheckpointAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<AdminAuditDbContext>();

        var heads = await db.AuditEntries
            .GroupBy(e => e.TenantId)
            .Select(g => new { TenantId = g.Key, Head = g.Max(e => e.Seq) })
            .ToListAsync(ct);

        var checkpoints = await db.Checkpoints
            .GroupBy(c => c.TenantId)
            .Select(g => new { TenantId = g.Key, Seq = g.Max(c => c.Seq) })
            .ToDictionaryAsync(x => x.TenantId, x => x.Seq, ct);

        return heads
            .Where(h => h.Head > (checkpoints.TryGetValue(h.TenantId, out var cp) ? cp : 0))
            .Select(h => h.TenantId)
            .ToList();
    }

    private async Task<AuditCheckpointResult> CheckpointTenantAsync(Guid tenantId, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var tenant = new TenantId(tenantId);

        // The periodic job is the transport adapter here (ADR-0090): it owns Begin/Commit. The
        // checkpointer never touches the scope.
        var tenantScope = sp.GetRequiredService<ITenantScope>();
        await using var handle = await tenantScope.BeginAsync(tenant, ct);

        var result = await sp.GetRequiredService<AuditCheckpointer>().CreateAsync(tenant, ct);
        await handle.CommitAsync(ct);
        return result;
    }
}
