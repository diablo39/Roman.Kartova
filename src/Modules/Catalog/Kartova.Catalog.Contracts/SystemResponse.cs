using System.Diagnostics.CodeAnalysis;
using Kartova.SharedKernel;

namespace Kartova.Catalog.Contracts;

/// <summary>API response for a single catalog System entity (design §4.3). <see cref="CreatedBy"/>
/// is enriched by the read handlers via <c>IUserDirectory</c> (mirrors ApiResponse); write-path
/// handlers leave it null. <see cref="TeamId"/> is the steward team — member Application/Service
/// components keep their own independent team ownership (see <c>CatalogSystem</c>).</summary>
[ExcludeFromCodeCoverage]
public sealed record SystemResponse(
    Guid Id,
    Guid TenantId,
    string DisplayName,
    string? Description,
    Guid TeamId,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt)
{
    public UserDisplayInfo? CreatedBy { get; init; }
}
