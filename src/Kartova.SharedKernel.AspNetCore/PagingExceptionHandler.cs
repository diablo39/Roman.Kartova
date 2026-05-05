using Kartova.SharedKernel.Pagination;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// Maps pagination/sort exceptions to RFC 7807 400 responses per ADR-0091 + ADR-0095.
/// Registered in <c>Program.cs</c> alongside <c>DomainValidationExceptionHandler</c>.
/// Uses <see cref="IProblemDetailsService"/> so that the <c>AddProblemDetails</c>
/// customisation in <c>Program.cs</c> (which injects <c>traceId</c>) is applied to
/// every paging error response — consistent with the rest of the API.
/// </summary>
public sealed class PagingExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;

    public PagingExceptionHandler(IProblemDetailsService problemDetails)
    {
        _problemDetails = problemDetails;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        switch (exception)
        {
            case InvalidSortFieldException sortEx:
            {
                var problem = new ProblemDetails
                {
                    Type = ProblemTypes.InvalidSortField,
                    Title = "Invalid sort field",
                    Status = StatusCodes.Status400BadRequest,
                    Detail = sortEx.Message,
                    Instance = httpContext.Request.Path,
                };
                problem.Extensions["fieldName"] = sortEx.FieldName;
                problem.Extensions["allowedFields"] = sortEx.AllowedFields;

                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = httpContext,
                    ProblemDetails = problem,
                    Exception = exception,
                });
            }

            case InvalidCursorException cursorEx:
            {
                var problem = new ProblemDetails
                {
                    Type = ProblemTypes.InvalidCursor,
                    Title = "Invalid cursor",
                    Status = StatusCodes.Status400BadRequest,
                    Detail = cursorEx.Message,
                    Instance = httpContext.Request.Path,
                };

                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = httpContext,
                    ProblemDetails = problem,
                    Exception = exception,
                });
            }

            default:
                return false;
        }
    }
}
