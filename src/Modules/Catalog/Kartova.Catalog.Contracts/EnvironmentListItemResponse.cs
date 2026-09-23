using System.Diagnostics.CodeAnalysis;
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Contracts;

[ExcludeFromCodeCoverage]
public sealed record EnvironmentListItemResponse(
    Guid Id,
    Guid TenantId,
    string DisplayName,
    string Description,
    EnvironmentType Type,
    string? Region,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt);
