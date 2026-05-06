using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Kartova.SharedKernel.Pagination;
using Kartova.SharedKernel.Postgres.Pagination;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Kartova.Catalog.Infrastructure;

internal static class CatalogEndpointDelegates
{
    /// <summary>
    /// Synchronous in-process handler dispatch — invoked directly rather than
    /// via <c>IMessageBus.InvokeAsync</c>. Wolverine's bus opens its own DI
    /// scope for handler dispatch which would not see the HTTP request's
    /// <c>ITenantScope</c> begun by <c>TenantScopeBeginMiddleware</c> (ADR-0090,
    /// formalized in ADR-0093). Direct dispatch keeps the handler resolved from
    /// the HTTP request scope where the tenant scope is active. The handler
    /// class itself stays transport-agnostic so an async Kafka path can still
    /// resolve it later.
    ///
    /// Domain factory invariants (Application.Create) throw ArgumentException
    /// for empty/over-length name and empty description. The mapping to RFC 7807
    /// 400 lives in <c>DomainValidationExceptionHandler</c> per slice-3 spec §13.3
    /// — endpoints stay free of validation try/catch boilerplate.
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

    /// <summary>
    /// GET single Application by id. Direct synchronous handler dispatch to
    /// preserve the HTTP request scope's <c>ITenantScope</c> (see comment on
    /// <see cref="RegisterApplicationAsync"/>). Null result maps to RFC 7807
    /// 404 — RLS hides cross-tenant rows so unknown id and cross-tenant id
    /// surface identically (intentional, ADR-0090).
    /// </summary>
    internal static async Task<IResult> GetApplicationByIdAsync(
        Guid id,
        GetApplicationByIdHandler handler,
        CatalogDbContext db,
        CancellationToken ct)
    {
        var resp = await handler.Handle(new GetApplicationByIdQuery(id), db, ct);
        if (resp is null)
        {
            return Results.Problem(
                type: ProblemTypes.ResourceNotFound,
                title: "Application not found",
                detail: "No application with that id is visible in the current tenant.",
                statusCode: StatusCodes.Status404NotFound);
        }
        return Results.Ok(resp);
    }

    /// <summary>
    /// GET list of Applications visible in current tenant. Direct synchronous
    /// handler dispatch to preserve the HTTP request scope's <c>ITenantScope</c>
    /// (see comment on <see cref="RegisterApplicationAsync"/>). RLS auto-filters
    /// cross-tenant rows (ADR-0090). Cursor-paginated per ADR-0095.
    ///
    /// <para>
    /// <c>sortBy</c> and <c>sortOrder</c> are accepted as raw strings and parsed with
    /// <c>Enum.TryParse(ignoreCase: true)</c> so that the wire contract
    /// (<c>?sortBy=createdAt&amp;sortOrder=asc</c>, camelCase per ADR-0095) and the
    /// C# enum member names (<c>CreatedAt</c>, <c>Asc</c>) are both accepted. The JSON
    /// serializer emits camelCase names (via <see cref="System.Text.Json.Serialization.JsonStringEnumConverter"/>
    /// registered in <c>Program.cs</c>), so the OpenAPI document and generated
    /// TypeScript client will send camelCase values.
    /// </para>
    /// </summary>
    internal static async Task<IResult> ListApplicationsAsync(
        [FromQuery] string? sortBy,
        [FromQuery] string? sortOrder,
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        ListApplicationsHandler handler,
        CatalogDbContext db,
        CancellationToken ct)
    {
        // Case-insensitive parse: accepts both "createdAt" (wire contract) and "CreatedAt".
        // Unknown strings → InvalidSortFieldException / InvalidSortOrderException → RFC 7807 400
        // (PagingExceptionHandler). ADR-0095 §4.3.
        //
        // Enum.TryParse alone accepts numeric strings ("999", "-1") and binds them to
        // an undefined enum value. Enum.IsDefined rejects those before they reach the
        // sort spec / order branch (otherwise an undefined SortOrder would silently
        // fall through to the desc branch in QueryablePagingExtensions).
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

        var effectiveLimit = limit ?? QueryablePagingExtensions.DefaultLimit;

        var query = new ListApplicationsQuery(
            SortBy: parsedSortBy ?? ApplicationSortField.CreatedAt,
            SortOrder: parsedSortOrder ?? SortOrder.Desc,
            Cursor: cursor,
            Limit: effectiveLimit);

        var page = await handler.Handle(query, db, ct);
        return Results.Ok(page);
    }
}
