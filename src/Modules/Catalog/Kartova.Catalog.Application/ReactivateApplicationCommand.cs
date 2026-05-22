namespace Kartova.Catalog.Application;

/// <summary>
/// Reactivate an existing Application — reverse lifecycle transition
/// (Deprecated/Decommissioned → Active, ADR-0073). Empty body: no request DTO.
/// OrgAdmin only (CatalogApplicationsLifecycleReverse).
/// <see cref="Kartova.Catalog.Domain.Application.Reactivate"/> rejects non-Deprecated/
/// Decommissioned sources with <c>InvalidLifecycleTransitionException</c>, which the
/// shared <c>LifecycleConflictExceptionHandler</c> maps to RFC 7807 409.
///
/// <c>Id</c> is fully qualified to <see cref="Kartova.Catalog.Domain.ApplicationId"/>
/// because <c>System.ApplicationId</c> exists in the BCL — same trick
/// <see cref="DecommissionApplicationCommand"/> uses.
/// </summary>
public sealed record ReactivateApplicationCommand(Kartova.Catalog.Domain.ApplicationId Id);
