using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Pagination;

namespace Kartova.Catalog.Application;

/// <summary>
/// List applications visible to the current tenant (RLS-filtered). ADR-0095.
/// <para>
/// <paramref name="IncludeDecommissioned"/> opts out of ADR-0073's
/// "filtered out of default views" rule; default false. Slice 6 / spec §5.
/// </para>
/// <para>
/// <paramref name="OwnerUserId"/> — slice 9 / E2 (spec §6.5). Optional filter
/// that narrows the result set to applications whose <c>OwnerUserId</c> matches
/// the supplied guid. Existence is validated at the endpoint level via
/// <c>IUserDirectory</c> (which is tenant-scoped, so cross-tenant ids fall back
/// to "not found" and surface as 422 <c>invalid-owner</c>); the handler only
/// applies the predicate to the EF query.
/// </para>
/// </summary>
public sealed record ListApplicationsQuery(
    ApplicationSortField SortBy,
    SortOrder SortOrder,
    string? Cursor,
    int Limit,
    bool IncludeDecommissioned,
    Guid? OwnerUserId = null);
