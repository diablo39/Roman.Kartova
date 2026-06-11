using Kartova.Organization.Domain;
using Kartova.SharedKernel.AspNetCore;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Kartova.Organization.Infrastructure;

/// <summary>
/// Maps an uncaught <see cref="OneEmailPerTenantViolationException"/> (the ADR-0100 invariant breach
/// that surfaces from the users-projection upsert on the session-bootstrap hot path) to a typed
/// RFC 7807 response. Without this handler the exception falls through to a generic, untyped 500 —
/// and its message embeds the tenant id and email, which would leak into the response body if
/// developer details were ever enabled in a shared environment. This handler emits a STABLE
/// <c>type</c> with a generic, PII-free detail; the rich diagnostic content stays server-log-only
/// (<c>UserProjectionUpdater</c> already logs it at Error level before rethrowing).
///
/// <para>
/// Status is 500: the condition is a server-side data-integrity breach the caller cannot remedy by
/// changing the request (it requires ops to reconcile the realm config / data), so a 5xx is the
/// honest signal — but typed, so a client can still recognise it rather than treating it as an
/// opaque server bug.
/// </para>
/// </summary>
public sealed class OneEmailPerTenantExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        if (exception is not OneEmailPerTenantViolationException)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        var problem = new ProblemDetails
        {
            Type = ProblemTypes.OneEmailPerTenant,
            Title = "Account data conflict",
            Status = StatusCodes.Status500InternalServerError,
            Detail = "Your account could not be initialised due to a data-integrity conflict. "
                   + "This has been logged for an administrator to resolve.",
        };

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
