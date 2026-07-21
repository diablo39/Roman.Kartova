using Kartova.Catalog.Contracts;

namespace Kartova.Catalog.Application;

public static class SystemResponseExtensions
{
    public static SystemResponse ToResponse(this Kartova.Catalog.Domain.CatalogSystem system) =>
        new(
            system.Id.Value,
            system.TenantId.Value,
            system.DisplayName,
            system.Description,
            system.TeamId,
            system.CreatedByUserId,
            system.CreatedAt);
}
