using Kartova.Catalog.Application;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Audit;
using DomainApplication = Kartova.Catalog.Domain.Application;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Builds the <see cref="AuditEntry"/> for the four lifecycle transitions, which all
/// share the <c>application.lifecycle_changed</c> action (design §4) distinguished by
/// <c>from</c>/<c>to</c>. <paramref name="from"/> is the pre-transition lifecycle —
/// each handler must read it BEFORE invoking the domain transition method.
/// </summary>
public static class CatalogAuditEntries
{
    public static AuditEntry LifecycleChanged(DomainApplication app, Lifecycle from) =>
        new(CatalogAuditActions.ApplicationLifecycleChanged,
            CatalogAuditTargetTypes.Application,
            app.Id.Value.ToString(),
            new Dictionary<string, string?>
            {
                ["from"] = from.ToString(),
                ["to"] = app.Lifecycle.ToString(),
                ["sunsetDate"] = app.SunsetDate?.ToString("O"),
            });
}
