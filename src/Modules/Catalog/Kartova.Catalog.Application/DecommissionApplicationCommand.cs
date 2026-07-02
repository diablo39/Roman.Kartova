namespace Kartova.Catalog.Application;

/// <summary>
/// Decommission an existing Application — Deprecated → Decommissioned transition (ADR-0073).
/// No <c>ExpectedVersion</c> field: lifecycle endpoints rely on the domain invariant
/// ("current state must be Deprecated" + "now &gt;= sunsetDate") rather than optimistic
/// locking (slice 5 spec §3 Decision #7).
///
/// <para>
/// <see cref="Kartova.Catalog.Domain.Application.Decommission"/> rejects non-Deprecated
/// sources with <c>InvalidLifecycleTransitionException</c>, and a "now &lt; sunsetDate"
/// attempt with the same exception type but with <c>reason="before-sunset-date"</c> +
/// the stored <c>sunsetDate</c> attached. Both are mapped to RFC 7807 409 by the shared
/// <c>LifecycleConflictExceptionHandler</c>; the <c>reason</c> + <c>sunsetDate</c>
/// extensions surface only on the before-sunset path.
/// </para>
///
/// <para>
/// <see cref="OverrideSunset"/> requests the sunset-date invariant be bypassed (early
/// decommission). It defaults to <see langword="false"/> so the pre-existing no-body
/// POST call site keeps compiling. This command layer only threads the flag to
/// <see cref="Kartova.Catalog.Domain.Application.Decommission"/>'s
/// <c>allowBeforeSunset</c> parameter and audits the bypass — it does NOT authorize the
/// override itself; that gate lives at the endpoint (this slice's design spec, wired separately).
/// </para>
///
/// <c>Id</c> is fully qualified to <see cref="Kartova.Catalog.Domain.ApplicationId"/>
/// because <c>System.ApplicationId</c> exists in the BCL — same trick
/// <see cref="EditApplicationCommand"/> and <see cref="DeprecateApplicationCommand"/> use.
/// </summary>
public sealed record DecommissionApplicationCommand(
    Kartova.Catalog.Domain.ApplicationId Id,
    bool OverrideSunset = false);
