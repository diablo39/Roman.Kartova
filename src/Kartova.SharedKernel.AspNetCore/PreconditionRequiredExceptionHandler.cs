using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// Maps <see cref="PreconditionRequiredException"/> (thrown by the
/// <see cref="IfMatchEndpointFilter"/> when the header is missing or malformed)
/// to RFC 7807 <c>428 Precondition Required</c> with
/// <c>type = </c><see cref="ProblemTypes.PreconditionRequired"/>.
/// </summary>
public sealed class PreconditionRequiredExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;

    public PreconditionRequiredExceptionHandler(IProblemDetailsService problemDetails)
    {
        _problemDetails = problemDetails;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        if (exception is not PreconditionRequiredException pre)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status428PreconditionRequired;

        var problem = new ProblemDetails
        {
            Type = ProblemTypes.PreconditionRequired,
            Title = "Precondition required",
            Status = StatusCodes.Status428PreconditionRequired,
            Detail = pre.Message,
        };

        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
