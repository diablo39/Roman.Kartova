namespace Kartova.Catalog.Application;

/// <summary>
/// Query for fetching a single Application by id within the current tenant scope.
/// RLS filters cross-tenant rows automatically (ADR-0001 / ADR-0090).
/// </summary>
public sealed record GetApplicationByIdQuery(Guid Id);
