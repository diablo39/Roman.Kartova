using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
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
    /// handler dispatch to preserve the HTTP request scope's
    /// <c>ITenantScope</c> (see comment on <see cref="RegisterApplicationAsync"/>).
    /// RLS auto-filters cross-tenant rows (ADR-0090).
    /// </summary>
    internal static async Task<IResult> ListApplicationsAsync(
        ListApplicationsHandler handler,
        CatalogDbContext db,
        CancellationToken ct)
    {
        var rows = await handler.Handle(new ListApplicationsQuery(), db, ct);
        return Results.Ok(rows);
    }
}
