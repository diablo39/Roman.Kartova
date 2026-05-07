using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// Maps EF Core <see cref="DbUpdateConcurrencyException"/> (raised when the
/// supplied row-version <c>OriginalValue</c> doesn't match the database's
/// current value) to RFC 7807 <c>412 Precondition Failed</c> with
/// <c>type = </c><see cref="ProblemTypes.ConcurrencyConflict"/>. The originating
/// handler is expected to stash the current row version on
/// <c>Exception.Data["currentVersion"]</c> while the tenant connection is still
/// alive — by the time this handler runs, <c>TenantScopeBeginMiddleware</c> has
/// disposed the connection, so a fresh database read is no longer possible.
/// </summary>
public sealed class ConcurrencyConflictExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;

    public ConcurrencyConflictExceptionHandler(IProblemDetailsService problemDetails)
    {
        _problemDetails = problemDetails;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        if (exception is not DbUpdateConcurrencyException dbEx)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status412PreconditionFailed;

        var problem = new ProblemDetails
        {
            Type = ProblemTypes.ConcurrencyConflict,
            Title = "Concurrency conflict",
            Status = StatusCodes.Status412PreconditionFailed,
            Detail = "The resource was modified by another request. Reload and reapply.",
        };

        if (dbEx.Data["currentVersion"] is uint preCaptured)
        {
            problem.Extensions["currentVersion"] = VersionEncoding.Encode(preCaptured);
        }

        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
