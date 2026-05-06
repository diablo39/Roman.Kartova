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
        return exception switch
        {
            InvalidSortFieldException sortEx => await WriteProblemAsync(
                httpContext, exception, ProblemTypes.InvalidSortField,
                "Invalid sort field", sortEx.Message,
                p => { p.Extensions["fieldName"] = sortEx.FieldName; p.Extensions["allowedFields"] = sortEx.AllowedFields; },
                cancellationToken),

            InvalidSortOrderException orderEx => await WriteProblemAsync(
                httpContext, exception, ProblemTypes.InvalidSortOrder,
                "Invalid sort order", orderEx.Message,
                p => p.Extensions["value"] = orderEx.Value,
                cancellationToken),

            InvalidCursorException cursorEx => await WriteProblemAsync(
                httpContext, exception, ProblemTypes.InvalidCursor,
                "Invalid cursor", cursorEx.Message,
                addExtensions: null,
                cancellationToken),

            InvalidLimitException limitEx => await WriteProblemAsync(
                httpContext, exception, ProblemTypes.InvalidLimit,
                "Invalid limit", limitEx.Message,
                p =>
                {
                    p.Extensions["limit"] = limitEx.Limit;
                    p.Extensions["rawLimit"] = limitEx.RawLimit;
                    p.Extensions["minLimit"] = limitEx.MinLimit;
                    p.Extensions["maxLimit"] = limitEx.MaxLimit;
                },
                cancellationToken),

            _ => false,
        };
    }

    private async ValueTask<bool> WriteProblemAsync(
        HttpContext ctx,
        Exception ex,
        string type,
        string title,
        string detail,
        Action<ProblemDetails>? addExtensions,
        CancellationToken ct)
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        var problem = new ProblemDetails
        {
            Type = type,
            Title = title,
            Status = StatusCodes.Status400BadRequest,
            Detail = detail,
            Instance = ctx.Request.Path,
        };
        addExtensions?.Invoke(problem);
        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = ctx,
            ProblemDetails = problem,
            Exception = ex,
        });
    }
}
