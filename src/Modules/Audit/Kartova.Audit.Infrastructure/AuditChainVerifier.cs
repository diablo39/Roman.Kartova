using Kartova.Audit.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Audit.Infrastructure;

/// <summary>
/// Loads a tenant's audit rows (RLS scopes the read to the current tenant) ordered by seq and
/// delegates to the pure <see cref="AuditChainInspector"/>. Phase 1 ships this as an injectable
/// service exercised by tests; the regulator-facing surface (CLI/endpoint) is deferred.
/// </summary>
public sealed class AuditChainVerifier(AuditDbContext db)
{
    public async Task<AuditChainVerificationResult> VerifyAsync(TenantId tenantId, CancellationToken ct)
    {
        var rows = await db.AuditEntries
            .Where(e => e.TenantId == tenantId.Value)
            .OrderBy(e => e.Seq)
            .ToListAsync(ct);

        return AuditChainInspector.Inspect(rows);
    }
}
