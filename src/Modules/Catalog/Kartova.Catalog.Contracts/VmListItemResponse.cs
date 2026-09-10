using System.Diagnostics.CodeAnalysis;

namespace Kartova.Catalog.Contracts;

[ExcludeFromCodeCoverage]
public sealed record VmListItemResponse(
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
