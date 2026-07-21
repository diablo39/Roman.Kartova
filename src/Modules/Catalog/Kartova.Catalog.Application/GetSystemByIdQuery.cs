namespace Kartova.Catalog.Application;

/// <summary>Fetch one System by id within the current tenant scope (RLS-filtered).</summary>
public sealed record GetSystemByIdQuery(Guid Id);
