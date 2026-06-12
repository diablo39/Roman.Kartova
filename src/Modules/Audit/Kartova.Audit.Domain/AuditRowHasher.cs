using System.Security.Cryptography;

namespace Kartova.Audit.Domain;

/// <summary>
/// SHA-256 over the canonical row encoding (ADR-0018 tamper-evidence). The first row in a
/// per-tenant chain references <see cref="GenesisHash"/> (32 zero bytes) as its predecessor.
/// </summary>
public static class AuditRowHasher
{
    /// <summary>Predecessor hash for the first (genesis) row in a tenant's chain.</summary>
    public static byte[] GenesisHash => new byte[32];

    public static byte[] ComputeRowHash(
        Guid tenantId,
        long seq,
        DateTimeOffset occurredAt,
        AuditActorType actorType,
        Guid? actorId,
        string action,
        string targetType,
        string targetId,
        IReadOnlyDictionary<string, string?>? data,
        byte[] prevHash)
    {
        var canonical = AuditCanonicalSerializer.Serialize(
            tenantId, seq, occurredAt, actorType, actorId, action, targetType, targetId, data, prevHash);
        return SHA256.HashData(canonical);
    }
}
