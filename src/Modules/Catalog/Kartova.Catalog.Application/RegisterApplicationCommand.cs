namespace Kartova.Catalog.Application;

/// <summary>
/// Command to register a new <see cref="Kartova.Catalog.Domain.Application"/> in
/// the catalog of the current tenant. Tenant id and owner user id are sourced
/// from the request context (ITenantScope, ICurrentUser) by the handler — never
/// from the payload — per ADR-0090.
/// </summary>
public sealed record RegisterApplicationCommand(string Name, string Description);
