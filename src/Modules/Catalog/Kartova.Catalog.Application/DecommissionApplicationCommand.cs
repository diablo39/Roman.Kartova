namespace Kartova.Catalog.Application;

/// <summary>
/// Decommission an existing Application — Deprecated → Decommissioned transition (ADR-0073).
/// No <c>ExpectedVersion</c> field: lifecycle endpoints rely on the domain invariant
/// ("current state must be Deprecated" + "now &gt;= sunsetDate") rather than optimistic
/// locking (slice 5 spec §3 Decision #7). No request DTO either — the POST body is empty;
/// all the information needed lives on the entity itself (current lifecycle, stored sunset
/// date) and the injected <c>TimeProvider</c>.
///
/// <para>
/// <see cref="Kartova.Catalog.Domain.Application.Decommission"/> rejects non-Deprecated
/// sources with <c>InvalidLifecycleTransitionException</c>, and a "now &lt; sunsetDate"
/// attempt with the same exception type but with <c>reason="before-sunset-date"</c> +
/// the stored <c>sunsetDate</c> attached. Both are mapped to RFC 7807 409 by the shared
/// <c>LifecycleConflictExceptionHandler</c>; the <c>reason</c> + <c>sunsetDate</c>
/// extensions surface only on the before-sunset path. Admin override deferred to the
/// RBAC slice (slice 5 spec §13.2).
/// </para>
///
/// <c>Id</c> is fully qualified to <see cref="Kartova.Catalog.Domain.ApplicationId"/>
/// because <c>System.ApplicationId</c> exists in the BCL — same trick
/// <see cref="EditApplicationCommand"/> and <see cref="DeprecateApplicationCommand"/> use.
/// </summary>
public sealed record DecommissionApplicationCommand(Kartova.Catalog.Domain.ApplicationId Id);
