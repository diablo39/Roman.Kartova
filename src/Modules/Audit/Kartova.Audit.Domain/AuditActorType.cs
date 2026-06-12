namespace Kartova.Audit.Domain;

/// <summary>
/// Who performed an audited action. <see cref="User"/> is the only value written in
/// Phase 1 (all wired callers are authenticated HTTP requests). <see cref="System"/>
/// (background jobs) and <see cref="ServiceAccount"/> (ADR-0009) exist so the schema
/// is ready; the writer begins emitting them in Phase 2 when such callers appear.
/// </summary>
public enum AuditActorType
{
    User = 1,
    System = 2,
    ServiceAccount = 3,
}
