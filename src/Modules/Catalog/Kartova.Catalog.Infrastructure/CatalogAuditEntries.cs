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
///
/// <para>
/// <paramref name="overrodeSunset"/> + <paramref name="bypassedSunset"/> record a
/// sunset-override bypass (slice 5 spec §5.2, Decommission only). They are additive:
/// omitted by every caller except the override path, so the <c>overrodeSunset</c> /
/// <c>bypassedSunsetDate</c> keys are present in the data bag only when a bypass
/// actually occurred — normal entries are byte-for-byte unchanged.
/// </para>
/// </summary>
public static class CatalogAuditEntries
{
    public static AuditEntry LifecycleChanged(
        DomainApplication app, Lifecycle from,
        bool overrodeSunset = false, DateTimeOffset? bypassedSunset = null)
    {
        var data = new Dictionary<string, string?>
        {
            ["from"] = from.ToString(),
            ["to"] = app.Lifecycle.ToString(),
            ["sunsetDate"] = app.SunsetDate?.ToString("O"),
        };
        if (overrodeSunset)
        {
            data["overrodeSunset"] = "true";
            data["bypassedSunsetDate"] = bypassedSunset?.ToString("O");
        }
        return new(CatalogAuditActions.ApplicationLifecycleChanged,
            CatalogAuditTargetTypes.Application,
            app.Id.Value.ToString(),
            data);
    }

    /// <summary>
    /// Builds the <see cref="AuditEntry"/> for a successor set/clear while
    /// Deprecated (ADR-0110 §5.3, <c>application.successor_changed</c>).
    /// <paramref name="from"/> is the pre-change successor id — the handler
    /// must read it BEFORE invoking <c>Application.SetSuccessor</c>. Guids
    /// stored as strings for jsonb-stability (like B3's override keys).
    /// </summary>
    public static AuditEntry SuccessorChanged(DomainApplication app, Guid? from)
    {
        var data = new Dictionary<string, string?>
        {
            ["from"] = from?.ToString(),
            ["to"] = app.SuccessorApplicationId?.ToString(),
        };
        return new(CatalogAuditActions.ApplicationSuccessorChanged,
            CatalogAuditTargetTypes.Application,
            app.Id.Value.ToString(),
            data);
    }
}
