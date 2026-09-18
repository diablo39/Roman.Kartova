using System.Diagnostics.CodeAnalysis;
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Contracts;

[ExcludeFromCodeCoverage]
public sealed record EnvironmentDetailResponse(
    Guid Id,
    Guid TenantId,
    string DisplayName,
    string Description,
    EnvironmentType Type,
    string? Region,
    string? Cluster,
    IReadOnlyDictionary<string, string> ResourceDetails,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt,
    string Version);
