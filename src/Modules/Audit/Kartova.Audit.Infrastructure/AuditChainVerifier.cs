using System.Text.Json;
using Kartova.Audit.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Audit.Infrastructure;

/// <summary>
/// Verifies a tenant's audit chain (RLS scopes the read to the current tenant). Rows are
/// <b>streamed</b> via <see cref="EntityFrameworkQueryableExtensions.AsAsyncEnumerable{T}"/> and fed
/// one at a time into the pure <see cref="AuditChainWalker"/>, so peak memory is a single row
/// regardless of chain length — a verify over millions of rows never materializes the whole table.
/// The walk exits early on the first break.
///
/// <para>Two paths (ADR-0105): <see cref="VerifyAsync"/> walks the whole chain from genesis (deep
/// audit / regulator). <see cref="VerifyFromCheckpointAsync"/> trusts the latest checkpoint and
/// re-walks only the tail since it — O(tail) rather than O(whole chain) — for routine checks.</para>
///
/// <para>Phase 1 ships this as an injectable service exercised by tests; the regulator-facing
/// surface (CLI/endpoint) is deferred.</para>
/// </summary>
public sealed class AuditChainVerifier(AuditDbContext db)
{
    /// <summary>Full verification: walks the entire chain from genesis, ignoring checkpoints.</summary>
    public Task<AuditChainVerificationResult> VerifyAsync(TenantId tenantId, CancellationToken ct) =>
        WalkAsync(tenantId, fromSeq: 0, new AuditChainWalker(), ct);

    /// <summary>
    /// Fast verification (ADR-0105): loads the latest checkpoint, confirms the live audit row at the
    /// checkpoint's seq still hashes to the checkpoint's <c>row_hash</c> (one indexed lookup) — a
    /// fabricated or tampered head fails here — then streams only the tail since the checkpoint.
    /// Falls back to a full walk when no checkpoint exists. This trusts the checkpointed prefix
    /// (sound given audit rows are insert-only); use <see cref="VerifyAsync"/> for a deep audit.
    /// </summary>
    public async Task<AuditChainVerificationResult> VerifyFromCheckpointAsync(TenantId tenantId, CancellationToken ct)
    {
        var checkpoint = await db.Checkpoints
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId.Value)
            .OrderByDescending(c => c.Seq)
            .FirstOrDefaultAsync(ct);

        if (checkpoint is null)
            return await VerifyAsync(tenantId, ct);

        // The row the checkpoint attests to must still exist and hash to the recorded value.
        var headHash = await db.AuditEntries
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId.Value && e.Seq == checkpoint.Seq)
            .Select(e => e.RowHash)
            .FirstOrDefaultAsync(ct);

        if (headHash is null)
            return AuditChainVerificationResult.Broken(checkpoint.Seq, "checkpointed row is missing");

        if (!headHash.AsSpan().SequenceEqual(checkpoint.RowHash))
            return AuditChainVerificationResult.Broken(checkpoint.Seq, "checkpointed row_hash does not match live row");

        return await WalkAsync(
            tenantId,
            fromSeq: checkpoint.Seq,
            new AuditChainWalker(checkpoint.Seq + 1, checkpoint.RowHash),
            ct);
    }

    private async Task<AuditChainVerificationResult> WalkAsync(
        TenantId tenantId, long fromSeq, AuditChainWalker walker, CancellationToken ct)
    {
        var rows = db.AuditEntries
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId.Value && e.Seq > fromSeq)
            .OrderBy(e => e.Seq)
            .AsAsyncEnumerable();

        try
        {
            await foreach (var row in rows.WithCancellation(ct))
            {
                if (!walker.Step(row))
                    break; // chain already broken — no point reading the rest
            }
        }
        catch (JsonException ex)
        {
            // A row's jsonb `data` failed to deserialize while the reader streamed it — possible
            // tamper or corruption. Surface as a broken chain (unverifiable) rather than an opaque crash.
            // Seq 0 is a sentinel ("position unknown"): the failure throws mid-materialization, before
            // the offending row's seq is in hand. Real chain positions start at 1.
            return AuditChainVerificationResult.Broken(0, $"data column deserialization failed: {ex.Message}");
        }

        return walker.Result;
    }
}
