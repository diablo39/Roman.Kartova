namespace Kartova.Audit.Domain;

/// <summary>
/// An immutable snapshot of a tenant's audit-chain head at a point in time (ADR-0105). Records that
/// the prefix <c>1..Seq</c> was verified intact and headed by <see cref="RowHash"/> as of
/// <see cref="CreatedAt"/>. Like <see cref="AuditLogEntry"/>, the database REVOKEs UPDATE/DELETE so a
/// checkpoint can never change once written. A later verification trusts the latest checkpoint and
/// re-walks only the tail since <see cref="Seq"/>, turning routine verification from O(whole chain)
/// into O(tail).
/// </summary>
public sealed class AuditCheckpoint
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public long Seq { get; private set; }
    public byte[] RowHash { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }

    private AuditCheckpoint() { /* EF */ }

    public static AuditCheckpoint Create(Guid id, Guid tenantId, long seq, byte[] rowHash, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty) throw new ArgumentException("id must not be empty.", nameof(id));
        if (tenantId == Guid.Empty) throw new ArgumentException("tenantId must not be empty.", nameof(tenantId));
        ArgumentNullException.ThrowIfNull(rowHash);
        ArgumentOutOfRangeException.ThrowIfLessThan(seq, 1);
        if (rowHash.Length != 32)
            throw new ArgumentException("rowHash must be 32 bytes (SHA-256).", nameof(rowHash));

        return new AuditCheckpoint
        {
            Id = id,
            TenantId = tenantId,
            Seq = seq,
            RowHash = rowHash.ToArray(), // defensive copy — caller may reuse the buffer
            CreatedAt = createdAt,
        };
    }
}
