using System.Text.Json;
using Kartova.SharedKernel.Pagination;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// Maps pagination/sort exceptions to RFC 7807 400 responses per ADR-0091 + ADR-0095.
/// Registered in <c>Program.cs</c> alongside <c>DomainValidationExceptionHandler</c>.
/// </summary>
public sealed class PagingExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        switch (exception)
        {
            case InvalidSortFieldException sortEx:
                await WriteProblemAsync(
                    httpContext,
                    type: ProblemTypes.InvalidSortField,
                    title: "Invalid sort field",
                    detail: sortEx.Message,
                    extensions: new Dictionary<string, object?>
                    {
                        ["fieldName"] = sortEx.FieldName,
                        ["allowedFields"] = sortEx.AllowedFields,
                    },
                    cancellationToken);
                return true;

            case InvalidCursorException cursorEx:
                await WriteProblemAsync(
                    httpContext,
                    type: ProblemTypes.InvalidCursor,
                    title: "Invalid cursor",
                    detail: cursorEx.Message,
                    extensions: null,
                    cancellationToken);
                return true;

            default:
                return false;
        }
    }

    private static async Task WriteProblemAsync(
        HttpContext ctx,
        string type,
        string title,
        string detail,
        IDictionary<string, object?>? extensions,
        CancellationToken ct)
    {
        var problem = new ProblemDetails
        {
            Type = type,
            Title = title,
            Status = StatusCodes.Status400BadRequest,
            Detail = detail,
            Instance = ctx.Request.Path,
        };
        if (extensions is not null)
        {
            foreach (var (k, v) in extensions) problem.Extensions[k] = v;
        }

        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        ctx.Response.ContentType = "application/problem+json";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(problem);
        await ctx.Response.Body.WriteAsync(bytes, ct);
    }
}
