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

    /// <summary>True when a spec document is stored for this API (ADR-0112). Computed by the read
    /// handlers via EXISTS — the document itself is never carried on this response.</summary>
    public bool HasSpec { get; init; }
}
