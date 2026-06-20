using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Application;

/// <summary>
/// Register a new <see cref="Kartova.Catalog.Domain.Service"/> in the current tenant.
/// Tenant id + created-by come from request context (ADR-0090); <c>TeamId</c> is the
/// required owning team (ADR-0103), validated by the delegate before dispatch.
/// </summary>
public sealed record RegisterServiceCommand(
    string DisplayName,
    string Description,
    Guid TeamId,
    IReadOnlyList<ServiceEndpointInput> Endpoints);

/// <summary>Transport-agnostic endpoint input for the register command.</summary>
public sealed record ServiceEndpointInput(string Url, Protocol Protocol);
