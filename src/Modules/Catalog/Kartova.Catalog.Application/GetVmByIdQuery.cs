namespace Kartova.Catalog.Application;

/// <summary>Fetch one VM-kind Infrastructure resource by id within the current tenant scope
/// (RLS-filtered). Resolves to <c>null</c> when absent or not a VM.</summary>
public sealed record GetVmByIdQuery(Guid Id);
