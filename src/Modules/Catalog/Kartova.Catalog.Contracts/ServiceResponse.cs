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

    // A2 (E-03.F-03.S-01): the System this component is PartOf, resolved from the
    // relationship edge by the LIST handler's batched per-page enrichment — there is no
    // system_id column. Both null ⇒ unassigned. Write-path handlers leave both null for
    // the same reason they leave CreatedBy null: no extra lookup on the write path.
    public Guid? SystemId { get; init; }
    public string? SystemDisplayName { get; init; }
}
