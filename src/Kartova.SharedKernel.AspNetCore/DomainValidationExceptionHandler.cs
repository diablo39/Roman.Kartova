using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// Maps <see cref="ArgumentException"/> (thrown by domain aggregate factories
/// when invariants fail) to RFC 7807 <c>400 Bad Request</c> with
/// <c>type = </c><see cref="ProblemTypes.ValidationFailed"/>.
///
/// Centralizes the mapping so write endpoints don't copy-paste a try/catch.
/// Resolves slice-3 spec follow-up §13.3.
///
/// Registered via <c>AddExceptionHandler&lt;DomainValidationExceptionHandler&gt;()</c>
/// before <c>app.UseExceptionHandler()</c>. Returns <c>false</c> for non-matching
/// exceptions so the default <see cref="IExceptionHandler"/> chain (and
/// <c>UseExceptionHandler</c> + <c>AddProblemDetails</c> per ADR-0091) handles them
/// as 500.
/// </summary>
public sealed class DomainValidationExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;

    public DomainValidationExceptionHandler(IProblemDetailsService problemDetails)
    {
        _problemDetails = problemDetails;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not ArgumentException argEx)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;

        var problem = new ProblemDetails
        {
            Type = ProblemTypes.ValidationFailed,
            Title = "Invalid request",
            Status = StatusCodes.Status400BadRequest,
            Detail = argEx.Message,
        };

        // E-02.F-01.S-06: field-level errors. When the aggregate factory tagged the
        // ArgumentException with a ParamName, surface it under `errors` so the SPA's
        // applyProblemDetailsToForm helper can attach the message to the right input.
        // Detail keeps the legacy single-message shape so non-form consumers (CLI,
        // agents) don't break.
        if (!string.IsNullOrEmpty(argEx.ParamName))
        {
            // Strip the framework-appended "(Parameter 'X')" suffix from the per-field
            // message so the form shows the readable invariant text only.
            var suffix = $" (Parameter '{argEx.ParamName}')";
            var fieldMessage = argEx.Message.EndsWith(suffix, StringComparison.Ordinal)
                ? argEx.Message[..^suffix.Length]
                : argEx.Message;

            problem.Extensions["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [argEx.ParamName] = [fieldMessage],
            };
        }

        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
