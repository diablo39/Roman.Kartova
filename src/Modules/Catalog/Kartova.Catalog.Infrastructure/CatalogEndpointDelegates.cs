using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Kartova.SharedKernel.Pagination;
using Kartova.SharedKernel.Postgres.Pagination;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
// ApplicationId is aliased rather than imported via `using Kartova.Catalog.Domain`
// because that would clash with `System.ApplicationId` in the BCL — same trick
// ApplicationSortSpecs uses for `DomainApplication`.
using ApplicationId = Kartova.Catalog.Domain.ApplicationId;

namespace Kartova.Catalog.Infrastructure;

internal static class CatalogEndpointDelegates
{
    /// <summary>
    /// Synchronous in-process handler dispatch — invoked directly rather than
    /// via <c>IMessageBus.InvokeAsync</c>. Wolverine's bus opens its own DI
    /// scope which would not see the HTTP request's <c>ITenantScope</c> begun
    /// by <c>TenantScopeBeginMiddleware</c> (ADR-0090, formalized in ADR-0093).
    /// </summary>
    internal static async Task<IResult> RegisterApplicationAsync(
        [FromBody] RegisterApplicationRequest request,
        RegisterApplicationHandler handler,
        CatalogDbContext db,
        ITenantContext tenant,
        ICurrentUser user,
        CancellationToken ct)
    {
        var response = await handler.Handle(
            new RegisterApplicationCommand(request.Name, request.DisplayName, request.Description),
            db, tenant, user, ct);

        return Results.Created($"/api/v1/catalog/applications/{response.Id}", response);
    }

    internal static async Task<IResult> GetApplicationByIdAsync(
        Guid id,
        GetApplicationByIdHandler handler,
        CatalogDbContext db,
        CancellationToken ct)
    {
        var resp = await handler.Handle(new GetApplicationByIdQuery(id), db, ct);
        if (resp is null) return EndpointResultExtensions.ApplicationNotFound();
        // Only the single-resource GET emits ETag; list rows carry `version` in
        // the body but no per-row ETag.
        return Results.Ok(resp).WithEtag(resp.Version);
    }

    /// <summary>
    /// <c>sortBy</c> and <c>sortOrder</c> are accepted as raw strings and parsed with
    /// <c>Enum.TryParse(ignoreCase: true)</c> so that the wire contract
    /// (<c>?sortBy=createdAt&amp;sortOrder=asc</c>, camelCase per ADR-0095) and the
    /// C# enum member names both bind. <c>limit</c> stays <c>string?</c> so non-integer
    /// inputs route through <c>InvalidLimitException</c> instead of the framework's
    /// generic parse-error 400.
    /// </summary>
    internal static async Task<IResult> ListApplicationsAsync(
        [FromQuery] string? sortBy,
        [FromQuery] string? sortOrder,
        [FromQuery] string? cursor,
        [FromQuery] string? limit,
        ListApplicationsHandler handler,
        CatalogDbContext db,
        CancellationToken ct)
    {
        // Enum.TryParse alone accepts numeric strings ("999", "-1") and binds them to
        // an undefined enum value. Enum.IsDefined rejects those before they reach the
        // sort spec / order branch.
        ApplicationSortField? parsedSortBy = null;
        if (sortBy is not null)
        {
            if (!Enum.TryParse<ApplicationSortField>(sortBy, ignoreCase: true, out var sf)
                || !Enum.IsDefined(sf))
            {
                throw new InvalidSortFieldException(sortBy, ApplicationSortSpecs.AllowedFieldNames);
            }
            parsedSortBy = sf;
        }

        SortOrder? parsedSortOrder = null;
        if (sortOrder is not null)
        {
            if (!Enum.TryParse<SortOrder>(sortOrder, ignoreCase: true, out var so)
                || !Enum.IsDefined(so))
            {
                throw new InvalidSortOrderException(sortOrder);
            }
            parsedSortOrder = so;
        }

        int effectiveLimit;
        if (limit is null)
        {
            effectiveLimit = QueryablePagingExtensions.DefaultLimit;
        }
        else if (!int.TryParse(limit, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out effectiveLimit))
        {
            throw new InvalidLimitException(
                limit,
                QueryablePagingExtensions.MinLimit,
                QueryablePagingExtensions.MaxLimit);
        }

        var query = new ListApplicationsQuery(
            SortBy: parsedSortBy ?? ApplicationSortField.CreatedAt,
            SortOrder: parsedSortOrder ?? SortOrder.Desc,
            Cursor: cursor,
            Limit: effectiveLimit);

        var page = await handler.Handle(query, db, ct);
        return Results.Ok(page);
    }

    internal static async Task<IResult> EditApplicationAsync(
        Guid id,
        [FromBody] EditApplicationRequest request,
        EditApplicationHandler handler,
        CatalogDbContext db,
        HttpContext http,
        CancellationToken ct)
    {
        var expected = (uint)http.Items[IfMatchEndpointFilter.ExpectedVersionKey]!;

        var resp = await handler.Handle(
            new EditApplicationCommand(new ApplicationId(id), request.DisplayName, request.Description, expected),
            db, ct);

        if (resp is null) return EndpointResultExtensions.ApplicationNotFound();
        return Results.Ok(resp).WithEtag(resp.Version);
    }

    internal static async Task<IResult> DeprecateApplicationAsync(
        Guid id,
        [FromBody] DeprecateApplicationRequest request,
        DeprecateApplicationHandler handler,
        CatalogDbContext db,
        CancellationToken ct)
    {
        var resp = await handler.Handle(
            new DeprecateApplicationCommand(new ApplicationId(id), request.SunsetDate),
            db, ct);

        if (resp is null) return EndpointResultExtensions.ApplicationNotFound();
        return Results.Ok(resp);
    }

    internal static async Task<IResult> DecommissionApplicationAsync(
        Guid id,
        DecommissionApplicationHandler handler,
        CatalogDbContext db,
        CancellationToken ct)
    {
        var resp = await handler.Handle(
            new DecommissionApplicationCommand(new ApplicationId(id)), db, ct);

        if (resp is null) return EndpointResultExtensions.ApplicationNotFound();
        return Results.Ok(resp);
    }
}
