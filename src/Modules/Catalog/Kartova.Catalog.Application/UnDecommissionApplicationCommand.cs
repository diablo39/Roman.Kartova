namespace Kartova.Catalog.Application;

/// <summary>
/// UnDecommission an existing Application — reverse lifecycle transition
/// (Decommissioned → Deprecated, ADR-0073). Takes a new sunset date.
/// OrgAdmin only (CatalogApplicationsLifecycleReverse).
/// <see cref="Kartova.Catalog.Domain.Application.UnDecommission"/> rejects non-Decommissioned
/// sources with <c>InvalidLifecycleTransitionException</c>, which the shared
/// <c>LifecycleConflictExceptionHandler</c> maps to RFC 7807 409. A past sunsetDate
/// throws <c>ArgumentException</c> which <c>DomainValidationExceptionHandler</c> maps to 400.
///
/// <c>Id</c> is fully qualified to <see cref="Kartova.Catalog.Domain.ApplicationId"/>
/// because <c>System.ApplicationId</c> exists in the BCL — same trick
/// <see cref="DecommissionApplicationCommand"/> uses.
/// </summary>
public sealed record UnDecommissionApplicationCommand(
    Kartova.Catalog.Domain.ApplicationId Id,
    DateTimeOffset SunsetDate);
