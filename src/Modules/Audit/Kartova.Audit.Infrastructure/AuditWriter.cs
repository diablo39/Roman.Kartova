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
/// <para>Two actor paths are supported. The <c>User</c> path (authenticated HTTP requests) records
/// <see cref="AuditActorType.User"/> with the actor sourced from <c>ICurrentUser</c>;
/// <c>currentUser.DisplayName</c> throws if there is no <c>HttpContext</c> or the JWT carries
/// none of <c>name</c> / <c>preferred_username</c> / <c>email</c> / <c>sub</c>. The <c>System</c>
/// path (background jobs with no HTTP principal) takes the tenant explicitly and records
/// <c>actor_type=System</c>, <c>actor_id=NULL</c>, <c>actor_display="System"</c>.</para>
/// </summary>
public sealed class AuditWriter(
    AuditDbContext db,
    ICurrentUser currentUser,
    ITenantContext tenant,
    TimeProvider clock) : IAuditWriter
{
    /// <summary>Display snapshot for background (no-principal) System appends.</summary>
    private const string SystemActorDisplay = "System";

    public Task AppendAsync(AuditEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return AppendCoreAsync(
            tenant.Id.Value, AuditActorType.User, currentUser.UserId, currentUser.DisplayName, entry, ct);
    }

    public Task AppendSystemAsync(TenantId t, AuditEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return AppendCoreAsync(
            t.Value, AuditActorType.System, actorId: null, actorDisplay: SystemActorDisplay, entry, ct);
    }

    private async Task AppendCoreAsync(
        Guid tenantId,
        AuditActorType actorType,
        Guid? actorId,
        string? actorDisplay,
        AuditEntry entry,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);

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
            actorType: actorType,
            actorId: actorId,
            actorDisplay: actorDisplay,
            action: entry.Action,
            targetType: entry.TargetType,
            targetId: entry.TargetId,
            data: entry.Data,
            prevHash: prevHash);

        db.AuditEntries.Add(row);
        await db.SaveChangesAsync(ct);
    }
}
