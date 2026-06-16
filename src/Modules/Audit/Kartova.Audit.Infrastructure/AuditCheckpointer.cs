using System.Text.Json;
using Kartova.Audit.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Audit.Infrastructure;

/// <summary>
/// Creates audit-chain checkpoints (ADR-0105). Verifies the tail since the previous checkpoint (or
/// genesis), and — only if that tail is intact — persists the current head as a new checkpoint.
/// Rows are streamed via <see cref="EntityFrameworkQueryableExtensions.AsAsyncEnumerable{T}"/> so a
/// checkpoint over a large tail stays flat in memory. A broken tail writes no checkpoint and returns
/// the failing verification: a checkpoint is never created over an unverified prefix.
///
/// <para>No per-tenant append lock is taken — a snapshot of a valid prefix is valid whether or not a
/// concurrent append advances the head; it is merely slightly stale. The only production caller is
/// the single-leader daily sweep, which cannot race with itself; were a second concurrent creator
/// ever added, the unique <c>(tenant_id, seq)</c> index would reject the duplicate as a
/// unique-violation that the caller must handle (it propagates from here, it is not swallowed).</para>
/// </summary>
public sealed class AuditCheckpointer(AuditDbContext db, TimeProvider clock)
{
    public async Task<AuditCheckpointResult> CreateAsync(TenantId tenantId, CancellationToken ct)
    {
        var latest = await db.Checkpoints
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId.Value)
            .OrderByDescending(c => c.Seq)
            .FirstOrDefaultAsync(ct);

        var fromSeq = latest?.Seq ?? 0;
        var walker = latest is null
            ? new AuditChainWalker()
            : new AuditChainWalker(latest.Seq + 1, latest.RowHash);

        var headSeq = fromSeq;
        byte[]? headHash = latest?.RowHash;

        var tail = db.AuditEntries
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId.Value && e.Seq > fromSeq)
            .OrderBy(e => e.Seq)
            .AsAsyncEnumerable();

        try
        {
            await foreach (var row in tail.WithCancellation(ct))
            {
                if (!walker.Step(row))
                    return AuditCheckpointResult.ChainBroken(walker.Result);

                headSeq = row.Seq;
                headHash = row.RowHash; // aliases the row's buffer; AuditCheckpoint.Create copies it defensively
            }
        }
        catch (JsonException ex)
        {
            // Seq 0 is a sentinel ("position unknown") — the failure throws mid-materialization,
            // before the offending row's seq is in hand. Real chain positions start at 1.
            return AuditCheckpointResult.ChainBroken(
                AuditChainVerificationResult.Broken(0, $"data column deserialization failed: {ex.Message}"));
        }

        // Empty chain, or no rows since the last checkpoint — nothing new to attest.
        if (headHash is null || headSeq == fromSeq)
            return AuditCheckpointResult.UpToDate(latest);

        var createdAt = clock.GetUtcNow().ToUniversalTime();
        var checkpoint = AuditCheckpoint.Create(
            id: Guid.CreateVersion7(createdAt),
            tenantId: tenantId.Value,
            seq: headSeq,
            rowHash: headHash,
            createdAt: createdAt);

        db.Checkpoints.Add(checkpoint);
        await db.SaveChangesAsync(ct);

        return AuditCheckpointResult.Created(checkpoint);
    }
}
