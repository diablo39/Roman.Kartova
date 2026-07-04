namespace Kartova.Catalog.Application;

/// <summary>Fetch one Api by id within the current tenant scope (RLS-filtered).</summary>
public sealed record GetApiByIdQuery(Guid Id);
