namespace Kartova.Catalog.Application;

/// <summary>Read the whole tenant's Org → Team → System → Component hierarchy (E-03.F-03.S-02).
/// No parameters — the tree is the entire RLS-scoped catalog, capped by the handler's node cap.</summary>
public sealed record GetCatalogHierarchyQuery();
