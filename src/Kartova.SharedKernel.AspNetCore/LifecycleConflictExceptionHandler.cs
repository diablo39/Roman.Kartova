using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// Maps any module's <c>InvalidLifecycleTransitionException</c> (matched by
/// type name to avoid SharedKernel → module coupling) to RFC 7807
/// <c>409 Conflict</c> with <c>type = </c><see cref="ProblemTypes.LifecycleConflict"/>.
///
/// Extensions populated from the exception's public properties:
/// <list type="bullet">
///   <item><c>currentLifecycle</c> — string name of the current state.</item>
///   <item><c>attemptedTransition</c> — name of the rejected transition.</item>
///   <item><c>sunsetDate</c> — present when the exception carries one (deprecate/decommission paths).</item>
///   <item><c>reason</c> — present when set (e.g. <c>before-sunset-date</c>).</item>
/// </list>
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
        if (exception.GetType().Name != "InvalidLifecycleTransitionException")
        {
            return false;
        }

        var t = exception.GetType();
        var current = t.GetProperty("CurrentLifecycle")?.GetValue(exception)?.ToString();
        var attempted = t.GetProperty("AttemptedTransition")?.GetValue(exception)?.ToString();
        var sunset = t.GetProperty("SunsetDate")?.GetValue(exception) as DateTimeOffset?;
        var reason = t.GetProperty("Reason")?.GetValue(exception)?.ToString();

        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;

        var problem = new ProblemDetails
        {
            Type = ProblemTypes.LifecycleConflict,
            Title = "Lifecycle transition not allowed",
            Status = StatusCodes.Status409Conflict,
            Detail = exception.Message,
        };

        if (current is not null)   problem.Extensions["currentLifecycle"]   = current;
        if (attempted is not null) problem.Extensions["attemptedTransition"] = attempted;
        if (sunset.HasValue)       problem.Extensions["sunsetDate"]          = sunset.Value;
        if (reason is not null)    problem.Extensions["reason"]              = reason;

        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
