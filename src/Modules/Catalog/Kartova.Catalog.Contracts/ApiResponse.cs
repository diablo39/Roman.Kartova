using System.Diagnostics.CodeAnalysis;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel;

namespace Kartova.Catalog.Contracts;

/// <summary>API response for a single catalog API entity. <see cref="CreatedBy"/> is
/// enriched by the read handlers via <c>IUserDirectory</c> (mirrors ServiceResponse);
/// write-path handlers leave it null. No concurrency-token field this slice (no edit
/// endpoint) — <see cref="Version"/> is the API version string, not the xmin token.</summary>
[ExcludeFromCodeCoverage]
public sealed record ApiResponse(
    Guid Id,
    Guid TenantId,
    string DisplayName,
    string Description,
    ApiStyle Style,
    string Version,
    string? SpecUrl,
    Guid TeamId,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt)
{
    public UserDisplayInfo? CreatedBy { get; init; }
}
