using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Pagination;

namespace Kartova.Catalog.Application;

/// <summary>
/// List applications visible to the current tenant (RLS-filtered). ADR-0095.
/// <para>
/// <paramref name="Lifecycle"/> — ADR-0107 multi-select filter. Empty ⇒ ADR-0073
/// default view (hide Decommissioned); non-empty ⇒ matches the selected states
/// exactly (<c>= ANY(@p)</c> via Npgsql).
/// Encoded into the cursor <c>f</c>-map only when non-empty (sorted comma-joined
/// enum names) so a mid-pagination change trips <c>CursorFilterMismatchException</c>.
/// </para>
/// <para>
/// <paramref name="TeamId"/> — ADR-0107 multi-select team filter. Non-empty ⇒
/// rows whose <c>TeamId</c> is in the supplied set (<c>= ANY(@p)</c> via Npgsql). Encoded
/// into the cursor <c>f</c>-map only when non-empty (sorted comma-joined Guid "D"
/// strings).
/// </para>
/// <para>
/// <paramref name="DisplayNameContains"/> — ADR-0107 substring filter. Applied
/// before pagination so a hidden row never becomes a cursor boundary. ILIKE with
/// backslash escape; encoded into the cursor f-map so a mid-pagination change
/// trips CursorFilterMismatchException.
/// </para>
/// <para>
/// <paramref name="CreatedByUserId"/> — slice 9 / E2 (spec §6.5), reframed as
/// "created by" in slice 10 / ADR-0103. Optional filter that narrows the result
/// set to applications whose <c>CreatedByUserId</c> matches the supplied guid.
/// Existence is validated at the endpoint level via <c>IUserDirectory</c> (which
/// is tenant-scoped, so cross-tenant ids fall back to "not found" and surface as
/// 422 <c>invalid-created-by</c>); the handler only applies the predicate to the
/// EF query.
/// </para>
/// </summary>
public sealed record ListApplicationsQuery(
    ApplicationSortField SortBy,
    SortOrder SortOrder,
    string? Cursor,
    int Limit,
    Lifecycle[] Lifecycle,
    Guid[] TeamId,
    string? DisplayNameContains = null,
    Guid? CreatedByUserId = null);
