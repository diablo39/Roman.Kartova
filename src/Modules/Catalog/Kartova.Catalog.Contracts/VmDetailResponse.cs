using System.Diagnostics.CodeAnalysis;

namespace Kartova.Catalog.Contracts;

/// <summary>Same shape as <see cref="VmListItemResponse"/> in slice 1.</summary>
[ExcludeFromCodeCoverage]
public sealed record VmDetailResponse(
    Guid Id,
    Guid TenantId,
    string DisplayName,
    string Description,
    string? Provider,
    Guid TeamId,
    Guid? SystemId,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt,
    VmAttributesDto Attributes);
