using Kartova.Audit.Domain;
using Kartova.SharedKernel.Audit;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Audit.Infrastructure;

/// <summary>
/// Appends one audit row inside the caller's tenant transaction (ADR-0090). Synchronous and
/// fail-closed: <see cref="AuditDbContext"/> shares the request connection + transaction via
/// <c>AddModuleDbContext</c>, so the row commits atomically with the caller's change and any
/// failure here rolls the whole request back.
///
/// <para>Chain ordering: a per-tenant <c>pg_advisory_xact_lock</c> serializes concurrent appends
/// for the same tenant — correct even for the genesis row, where there is no existing row to lock
/// with <c>FOR UPDATE</c>. The lock auto-releases at transaction end.</para>
///
/// <para>Phase 1 writes <see cref="AuditActorType.User"/> only (wired callers arrive in Phase 2;
/// all are authenticated HTTP requests). <c>actor_display</c> is left null here — the offboard
/// caller that needs the snapshot resolves it in Phase 2.</para>
/// </summary>
public sealed class AuditWriter(
    AuditDbContext db,
    ICurrentUser currentUser,
    ITenantContext tenant,
    TimeProvider clock) : IAuditWriter
{
    public async Task AppendAsync(AuditEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var tenantId = tenant.Id.Value;

        // Serialize appends for this tenant within the current transaction.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext('kartova.audit_chain'), hashtext({tenantId.ToString()}))",
            ct);

        var head = await db.AuditEntries
            .Where(e => e.TenantId == tenantId)
            .OrderByDescending(e => e.Seq)
            .Select(e => new { e.Seq, e.RowHash })
            .FirstOrDefaultAsync(ct);

        var seq = (head?.Seq ?? 0) + 1;
        var prevHash = head?.RowHash ?? AuditRowHasher.GenesisHash;

        // Truncate to microseconds so the hashed timestamp matches what Postgres timestamptz
        // stores and returns (PG resolution is 1µs; .NET ticks are 100ns) — otherwise the
        // verifier's recomputed hash would diverge from the stored one on read-back.
        var raw = clock.GetUtcNow().ToUniversalTime();
        var occurredAt = new DateTimeOffset(raw.Ticks - (raw.Ticks % 10), TimeSpan.Zero);

        var row = AuditLogEntry.Create(
            id: Guid.CreateVersion7(occurredAt),
            tenantId: tenantId,
            seq: seq,
            occurredAt: occurredAt,
            actorType: AuditActorType.User,
            actorId: currentUser.UserId,
            actorDisplay: null,
            action: entry.Action,
            targetType: entry.TargetType,
            targetId: entry.TargetId,
            data: entry.Data,
            prevHash: prevHash);

        db.AuditEntries.Add(row);
        await db.SaveChangesAsync(ct);
    }
}
