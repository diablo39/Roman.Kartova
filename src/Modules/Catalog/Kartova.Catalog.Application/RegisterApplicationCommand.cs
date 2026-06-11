namespace Kartova.Catalog.Application;

/// <summary>
/// Command to register a new <see cref="Kartova.Catalog.Domain.Application"/> in
/// the catalog of the current tenant. Tenant id and created-by user id are sourced
/// from the request context (ITenantScope, ICurrentUser) by the handler — never
/// from the payload — per ADR-0090. <c>TeamId</c> (the required owning team,
/// ADR-0103) comes from the payload and is validated to exist in the tenant.
/// </summary>
public sealed record RegisterApplicationCommand(string DisplayName, string Description, Guid TeamId);
