using Kartova.Catalog.Contracts;

namespace Kartova.Catalog.Application;

public static class ApiResponseExtensions
{
    public static ApiResponse ToResponse(this Kartova.Catalog.Domain.Api api) =>
        new(
            api.Id.Value,
            api.TenantId.Value,
            api.DisplayName,
            api.Description,
            api.Style,
            api.Version,
            api.SpecUrl,
            api.TeamId,
            api.CreatedByUserId,
            api.CreatedAt);
}
