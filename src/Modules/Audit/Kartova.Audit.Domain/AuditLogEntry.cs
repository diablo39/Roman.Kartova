namespace Kartova.Audit.Domain;

/// <summary>
/// One append-only audit row (ADR-0018). Immutable after creation: <see cref="Create"/>
/// computes <see cref="RowHash"/> from all hashable fields + <see cref="PrevHash"/>, and the
/// database REVOKEs UPDATE/DELETE so the row can never change. <see cref="ActorDisplay"/> is a
/// denormalized snapshot so an offboarded actor (ADR-0102 hard delete) is still named.
/// </summary>
public sealed class AuditLogEntry
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public long Seq { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public AuditActorType ActorType { get; private set; }
    public Guid? ActorId { get; private set; }
    public string? ActorDisplay { get; private set; }
    public string Action { get; private set; } = null!;
    public string TargetType { get; private set; } = null!;
    public string TargetId { get; private set; } = null!;
    public IReadOnlyDictionary<string, string?>? Data { get; private set; }
    public byte[] PrevHash { get; private set; } = null!;
    public byte[] RowHash { get; private set; } = null!;

    private AuditLogEntry() { /* EF */ }

    public static AuditLogEntry Create(
        Guid id,
        Guid tenantId,
        long seq,
        DateTimeOffset occurredAt,
        AuditActorType actorType,
        Guid? actorId,
        string? actorDisplay,
        string action,
        string targetType,
        string targetId,
        IReadOnlyDictionary<string, string?>? data,
        byte[] prevHash)
    {
        if (id == Guid.Empty) throw new ArgumentException("id must not be empty.", nameof(id));
        if (tenantId == Guid.Empty) throw new ArgumentException("tenantId must not be empty.", nameof(tenantId));
        ArgumentNullException.ThrowIfNull(prevHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetType);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        ArgumentOutOfRangeException.ThrowIfLessThan(seq, 1);
        if (prevHash.Length != 32)
            throw new ArgumentException("prevHash must be 32 bytes (SHA-256).", nameof(prevHash));
        if (!Enum.IsDefined(actorType))
            throw new ArgumentOutOfRangeException(nameof(actorType), actorType, "Unknown actor type.");
        if (actorType == AuditActorType.User && (actorId is null || actorId == Guid.Empty))
            throw new ArgumentException("A User actor requires a non-empty actorId.", nameof(actorId));

        return new AuditLogEntry
        {
            Id = id,
            TenantId = tenantId,
            Seq = seq,
            OccurredAt = occurredAt,
            ActorType = actorType,
            ActorId = actorId,
            ActorDisplay = actorDisplay,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Data = data,
            PrevHash = prevHash.ToArray(), // defensive copy — caller may reuse the buffer
            RowHash = AuditRowHasher.ComputeRowHash(
                tenantId, seq, occurredAt, actorType, actorId, action, targetType, targetId, data, prevHash),
        };
    }
}
