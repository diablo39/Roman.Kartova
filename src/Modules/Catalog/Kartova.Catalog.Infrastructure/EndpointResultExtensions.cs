using Kartova.SharedKernel.AspNetCore;
using Microsoft.AspNetCore.Http;

namespace Kartova.Catalog.Infrastructure;

internal static class EndpointResultExtensions
{
    /// <summary>
    /// Wraps an existing <see cref="IResult"/> so that the response also emits
    /// an RFC 7232 quoted <c>ETag</c> header carrying <paramref name="version"/>.
    /// Reused by GET-by-id and PUT response paths so clients can capture it for
    /// a future <c>If-Match</c> request.
    /// </summary>
    internal static IResult WithEtag(this IResult inner, string version) =>
        new EtagWrappedResult(inner, version);

    /// <summary>
    /// RFC 7807 404 envelope shared by every endpoint that returns a nullable
    /// <c>ApplicationResponse?</c>. RLS hides cross-tenant rows so unknown id
    /// and cross-tenant id surface identically (intentional, ADR-0090).
    /// </summary>
    internal static IResult ApplicationNotFound() => Results.Problem(
        type: ProblemTypes.ResourceNotFound,
        title: "Application not found",
        detail: "No application with that id is visible in the current tenant.",
        statusCode: StatusCodes.Status404NotFound);

    private sealed class EtagWrappedResult(IResult inner, string version) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers["ETag"] = $"\"{version}\"";
            await inner.ExecuteAsync(httpContext);
        }
    }
}
