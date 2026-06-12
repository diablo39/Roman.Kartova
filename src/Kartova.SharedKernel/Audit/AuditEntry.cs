namespace Kartova.SharedKernel.Audit;

/// <summary>
/// What a caller records about a single audited action. The actor, tenant, timestamp,
/// sequence, and hash chain are NOT supplied here — the writer derives them from the
/// ambient request context (ADR-0090) and the existing chain.
///
/// <para><b>Data values are strings only</b> (GUIDs, enum names, "true"/"false", etc.).
/// This is deliberate: it keeps the canonical hash jsonb-stable (Postgres jsonb reformats
/// numbers like <c>1.0</c>→<c>1</c>; all-string values sidestep that — see the design
/// spec §5). Use <c>null</c> values for "absent".</para>
/// </summary>
public sealed record AuditEntry(
    string Action,
    string TargetType,
    string TargetId,
    IReadOnlyDictionary<string, string?>? Data = null);
