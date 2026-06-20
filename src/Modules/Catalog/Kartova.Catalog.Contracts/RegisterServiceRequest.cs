using System.Diagnostics.CodeAnalysis;

namespace Kartova.Catalog.Contracts;

[ExcludeFromCodeCoverage]
public sealed record RegisterServiceRequest(
    string DisplayName,
    string Description,
    Guid TeamId,
    IReadOnlyList<ServiceEndpointDto> Endpoints);
