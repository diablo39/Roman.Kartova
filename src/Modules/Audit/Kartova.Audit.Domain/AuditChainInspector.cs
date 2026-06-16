namespace Kartova.Audit.Domain;

/// <summary>
/// Pure verification of a tenant's audit chain held in memory. Given rows ordered by <c>Seq</c>,
/// asserts the sequence is contiguous from 1, each row's <c>PrevHash</c> equals the prior row's
/// <c>RowHash</c>, and each stored <c>RowHash</c> recomputes from the row's fields. The walk logic
/// lives in <see cref="AuditChainWalker"/>; the streaming Infrastructure verifier drives the same
/// walker so the two paths cannot diverge.
/// </summary>
public static class AuditChainInspector
{
    public static AuditChainVerificationResult Inspect(IReadOnlyList<AuditLogEntry> rowsOrderedBySeq)
    {
        ArgumentNullException.ThrowIfNull(rowsOrderedBySeq);

        var walker = new AuditChainWalker();
        foreach (var row in rowsOrderedBySeq)
        {
            if (!walker.Step(row))
                break;
        }

        return walker.Result;
    }
}
