using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.AspNetCore;

namespace Kartova.Catalog.Application;

public static class ServiceResponseExtensions
{
    public static ServiceResponse ToResponse(this Kartova.Catalog.Domain.Service svc) =>
        new(
            svc.Id.Value,
            svc.TenantId.Value,
            svc.DisplayName,
            svc.Description,
            svc.TeamId,
            svc.CreatedByUserId,
            svc.CreatedAt,
            svc.Health,
            svc.Endpoints.Select(e => new ServiceEndpointDto(e.Url, e.Protocol)).ToList(),
            VersionEncoding.Encode(svc.Version));
}
