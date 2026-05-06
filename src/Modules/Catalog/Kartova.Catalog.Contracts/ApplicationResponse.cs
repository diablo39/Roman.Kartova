using System.Diagnostics.CodeAnalysis;
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Contracts;

[ExcludeFromCodeCoverage]
public sealed record ApplicationResponse(
    Guid Id,
    Guid TenantId,
    string Name,
    string DisplayName,
    string Description,
    Guid OwnerUserId,
    DateTimeOffset CreatedAt,
    Lifecycle Lifecycle,
    DateTimeOffset? SunsetDate,
    string Version);
