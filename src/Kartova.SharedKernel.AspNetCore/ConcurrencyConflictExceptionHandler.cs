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
/// <c>type = </c><see cref="ProblemTypes.ConcurrencyConflict"/>.
///
/// The current version is captured from the entry's <c>DatabaseValues</c>
/// when available so the client can retry against the latest state.
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

        // Best-effort: pull the database's current version into the extensions
        // dictionary so the client can resync without a separate GET.
        var entry = dbEx.Entries.FirstOrDefault();
        if (entry is not null)
        {
            var dbValues = await entry.GetDatabaseValuesAsync(ct);
            if (dbValues is not null && dbValues["Version"] is uint currentVersion)
            {
                problem.Extensions["currentVersion"] = VersionEncoding.Encode(currentVersion);
            }
        }

        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
