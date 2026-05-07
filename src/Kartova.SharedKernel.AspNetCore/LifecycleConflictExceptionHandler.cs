using Kartova.SharedKernel;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// Maps any module exception implementing <see cref="ILifecycleConflict"/> to
/// RFC 7807 <c>409 Conflict</c> with <c>type = </c><see cref="ProblemTypes.LifecycleConflict"/>.
/// Extension members on the response: <c>currentLifecycle</c>, <c>attemptedTransition</c>,
/// optional <c>sunsetDate</c>, optional <c>reason</c>.
/// </summary>
public sealed class LifecycleConflictExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;

    public LifecycleConflictExceptionHandler(IProblemDetailsService problemDetails)
    {
        _problemDetails = problemDetails;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        if (exception is not ILifecycleConflict conflict)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;

        var problem = new ProblemDetails
        {
            Type = ProblemTypes.LifecycleConflict,
            Title = "Lifecycle transition not allowed",
            Status = StatusCodes.Status409Conflict,
            Detail = exception.Message,
        };

        problem.Extensions["currentLifecycle"]    = conflict.CurrentLifecycleName;
        problem.Extensions["attemptedTransition"] = conflict.AttemptedTransition;
        if (conflict.SunsetDate.HasValue) problem.Extensions["sunsetDate"] = conflict.SunsetDate.Value;
        if (conflict.Reason is not null)  problem.Extensions["reason"]     = conflict.Reason;

        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
