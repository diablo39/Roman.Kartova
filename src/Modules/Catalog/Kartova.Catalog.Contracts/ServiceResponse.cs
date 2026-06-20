using System.Diagnostics.CodeAnalysis;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel;

namespace Kartova.Catalog.Contracts;

/// <summary>API response for a single catalog service. <see cref="CreatedBy"/> is
/// enriched by the read handlers via <c>IUserDirectory</c> (mirrors
/// <c>ApplicationResponse</c>); write-path handlers leave it null.</summary>
[ExcludeFromCodeCoverage]
public sealed record ServiceResponse(
    Guid Id,
    Guid TenantId,
    string DisplayName,
    string Description,
    Guid TeamId,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt,
    HealthStatus Health,
    IReadOnlyList<ServiceEndpointDto> Endpoints,
    string Version)
{
    public UserDisplayInfo? CreatedBy { get; init; }
}
