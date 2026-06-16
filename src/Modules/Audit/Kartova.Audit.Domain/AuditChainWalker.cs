namespace Kartova.Audit.Domain;

/// <summary>
/// Forward-only, single-pass state for verifying an audit chain one row at a time. Rows are fed
/// in <c>Seq</c> order via <see cref="Step"/>; the walker holds only the expected next seq and the
/// prior <c>RowHash</c>, so memory stays flat regardless of chain length. The same walker is driven
/// by the in-memory <see cref="AuditChainInspector"/> and the streaming Infrastructure verifier, so
/// both share one walk implementation and cannot diverge.
///
/// <para>A walker can start from genesis (default ctor) or be seeded at a verified checkpoint
/// boundary — see <see cref="AuditChainWalker(long, byte[])"/> — to verify only the tail.</para>
/// </summary>
public sealed class AuditChainWalker
{
    private long _expectedSeq;
    private byte[] _prev;
    private AuditChainVerificationResult? _broken;

    /// <summary>Starts a walk at the genesis row (expected seq 1, predecessor = genesis hash).</summary>
    public AuditChainWalker() : this(1, AuditRowHasher.GenesisHash)
    {
    }

    /// <summary>
    /// Seeds a walk to resume after a trusted checkpoint: <paramref name="startExpectedSeq"/> is the
    /// first seq to verify (checkpoint seq + 1) and <paramref name="startPrevHash"/> is the checkpoint's
    /// <c>RowHash</c>, against which the next row's <c>PrevHash</c> must match.
    /// </summary>
    public AuditChainWalker(long startExpectedSeq, byte[] startPrevHash)
    {
        ArgumentNullException.ThrowIfNull(startPrevHash);
        ArgumentOutOfRangeException.ThrowIfLessThan(startExpectedSeq, 1);
        if (startPrevHash.Length != 32)
            throw new ArgumentException("startPrevHash must be 32 bytes (SHA-256).", nameof(startPrevHash));
        _expectedSeq = startExpectedSeq;
        _prev = startPrevHash.ToArray(); // defensive copy — caller may reuse the buffer
    }

    /// <summary>
    /// Feeds the next row in seq order. Returns <c>true</c> while the chain remains intact and
    /// <c>false</c> once a break is detected; after the first break further rows are ignored, so a
    /// caller can stop streaming early.
    /// </summary>
    public bool Step(AuditLogEntry row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (_broken is not null)
            return false;

        if (row.Seq != _expectedSeq)
        {
            _broken = AuditChainVerificationResult.Broken(row.Seq, $"non-contiguous seq (expected {_expectedSeq})");
            return false;
        }

        if (!row.PrevHash.AsSpan().SequenceEqual(_prev))
        {
            _broken = AuditChainVerificationResult.Broken(row.Seq, "prev_hash does not match prior row_hash");
            return false;
        }

        var recomputed = AuditRowHasher.ComputeRowHash(
            row.TenantId, row.Seq, row.OccurredAt, row.ActorType, row.ActorId,
            row.Action, row.TargetType, row.TargetId, row.Data, row.PrevHash);

        if (!recomputed.AsSpan().SequenceEqual(row.RowHash))
        {
            _broken = AuditChainVerificationResult.Broken(row.Seq, "row_hash does not match recomputed hash");
            return false;
        }

        _prev = row.RowHash;
        _expectedSeq++;
        return true;
    }

    /// <summary>
    /// The verification outcome for the rows fed so far: <see cref="AuditChainVerificationResult.Ok"/>
    /// until a break is seen. Note this reflects only what has been fed — <c>Ok</c> on a walker fed
    /// nothing means "no break seen", not "a chain was verified". Callers must feed every row in seq order.
    /// </summary>
    public AuditChainVerificationResult Result => _broken ?? AuditChainVerificationResult.Ok;
}
