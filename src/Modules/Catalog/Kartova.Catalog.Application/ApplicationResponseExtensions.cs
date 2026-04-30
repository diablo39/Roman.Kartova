using Kartova.Catalog.Contracts;

namespace Kartova.Catalog.Application;

public static class ApplicationResponseExtensions
{
    // Domain Application — fully-qualified to avoid clash with the enclosing
    // `Kartova.Catalog.Application` namespace (which is also a project name).
    public static ApplicationResponse ToResponse(this Kartova.Catalog.Domain.Application app) =>
        new(app.Id.Value, app.TenantId.Value, app.Name, app.Description, app.OwnerUserId, app.CreatedAt);
}
