namespace Kartova.Audit.Domain;

/// <summary>
/// Pure verification of a tenant's audit chain. Given rows ordered by <c>Seq</c>, asserts the
/// sequence is contiguous from 1, each row's <c>PrevHash</c> equals the prior row's <c>RowHash</c>,
/// and each stored <c>RowHash</c> recomputes from the row's fields. DB I/O lives in the
/// Infrastructure verifier; this function is the testable core.
/// </summary>
public static class AuditChainInspector
{
    public static AuditChainVerificationResult Inspect(IReadOnlyList<AuditLogEntry> rowsOrderedBySeq)
    {
        ArgumentNullException.ThrowIfNull(rowsOrderedBySeq);

        long expectedSeq = 1;
        var prev = AuditRowHasher.GenesisHash;

        foreach (var row in rowsOrderedBySeq)
        {
            if (row.Seq != expectedSeq)
                return AuditChainVerificationResult.Broken(row.Seq, $"non-contiguous seq (expected {expectedSeq})");

            if (!row.PrevHash.AsSpan().SequenceEqual(prev))
                return AuditChainVerificationResult.Broken(row.Seq, "prev_hash does not match prior row_hash");

            var recomputed = AuditRowHasher.ComputeRowHash(
                row.TenantId, row.Seq, row.OccurredAt, row.ActorType, row.ActorId,
                row.Action, row.TargetType, row.TargetId, row.Data, row.PrevHash);

            if (!recomputed.AsSpan().SequenceEqual(row.RowHash))
                return AuditChainVerificationResult.Broken(row.Seq, "row_hash does not match recomputed hash");

            prev = row.RowHash;
            expectedSeq++;
        }

        return AuditChainVerificationResult.Ok;
    }
}
