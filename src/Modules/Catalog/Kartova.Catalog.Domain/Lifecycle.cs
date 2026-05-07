namespace Kartova.Catalog.Domain;

/// <summary>
/// Application lifecycle states per ADR-0073. Linear forward progression
/// (Active → Deprecated → Decommissioned). Backward transitions require Org
/// Admin (deferred to RBAC slice — spec §13.2).
/// <para>
/// Numeric values are load-bearing because they are persisted to the
/// <c>lifecycle smallint</c> column on <c>catalog.applications</c> — reordering
/// or renumbering would silently corrupt rows already on disk. Pinned by
/// <see cref="LifecycleEnumRules"/> arch tests.
/// </para>
/// </summary>
public enum Lifecycle
{
    Active = 1,
    Deprecated = 2,
    Decommissioned = 3,
}
