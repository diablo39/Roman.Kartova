using System.Diagnostics.CodeAnalysis;

namespace Kartova.Catalog.Contracts;

[ExcludeFromCodeCoverage]
public sealed record InfrastructureListItemResponse(
    Guid Id,
    Guid TenantId,
    string DisplayName,
    string Description,
    string Type,
    string? Provider,
    Guid TeamId,
    Guid? SystemId,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt);
