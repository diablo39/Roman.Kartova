using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.AspNetCore;

namespace Kartova.Catalog.Application;

public static class ApplicationResponseExtensions
{
    // Domain Application — fully-qualified to avoid clash with the enclosing
    // `Kartova.Catalog.Application` namespace (which is also a project name).
    public static ApplicationResponse ToResponse(this Kartova.Catalog.Domain.Application app) =>
        new(
            app.Id.Value,
            app.TenantId.Value,
            app.Name,
            app.DisplayName,
            app.Description,
            app.OwnerUserId,
            app.CreatedAt,
            app.Lifecycle,
            app.SunsetDate,
            VersionEncoding.Encode(app.Version));
}
