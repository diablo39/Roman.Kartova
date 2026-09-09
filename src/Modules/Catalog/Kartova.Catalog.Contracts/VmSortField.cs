namespace Kartova.Catalog.Contracts;

/// <summary>VM-tier sort allowlist (ADR-0095). Typed columns (displayName/createdAt/provider)
/// + JSONB attributes served by partial btree-expression indexes (ADR-0115 slice 2a).</summary>
public enum VmSortField
{
    DisplayName,
    CreatedAt,
    Provider,
    PowerState,
    Os,
    Vcpu,
    MemoryGb,
    Hostname,
    Region,
}
