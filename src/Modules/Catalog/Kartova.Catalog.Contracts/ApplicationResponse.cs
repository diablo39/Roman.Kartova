using System.Diagnostics.CodeAnalysis;

namespace Kartova.Catalog.Contracts;

[ExcludeFromCodeCoverage]
public sealed record ApplicationResponse(
    Guid Id,
    Guid TenantId,
    string Name,
    string Description,
    Guid OwnerUserId,
    DateTimeOffset CreatedAt);
