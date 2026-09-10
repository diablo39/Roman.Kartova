namespace Kartova.Catalog.Application;

/// <summary>
/// Register a new VM-kind <see cref="Kartova.Catalog.Domain.InfrastructureResource"/> in the
/// current tenant. Tenant id + created-by come from request context (ADR-0090); <c>TeamId</c> is
/// the required owning team (ADR-0103), validated by the delegate before dispatch.
/// <see cref="Attributes"/> is the already-validated (<see cref="VmAttributes.Validate"/>)
/// app-layer representation of the opaque jsonb payload.
/// </summary>
public sealed record RegisterVmCommand(
    string DisplayName,
    string Description,
    Guid TeamId,
    string? Provider,
    VmAttributes Attributes);
