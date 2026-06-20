namespace Kartova.Catalog.Application;

/// <summary>Fetch one Service by id within the current tenant scope (RLS-filtered).</summary>
public sealed record GetServiceByIdQuery(Guid Id);
