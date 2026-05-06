namespace Kartova.Catalog.Domain;

/// <summary>
/// Application lifecycle states per ADR-0073. Linear forward progression
/// (Active → Deprecated → Decommissioned). Backward transitions require Org
/// Admin (deferred to RBAC slice — spec §13.2). Numeric values are
/// load-bearing — reordering breaks Application.Decommission's monotonic
/// comparisons. Pinned by LifecycleEnumRules arch tests.
/// </summary>
public enum Lifecycle
{
    Active = 1,
    Deprecated = 2,
    Decommissioned = 3,
}
