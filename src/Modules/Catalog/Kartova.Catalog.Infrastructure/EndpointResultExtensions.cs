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
    /// RFC 7807 404 envelope for a catalog resource that returns a nullable
    /// response. RLS hides cross-tenant rows so unknown id and cross-tenant id
    /// surface identically (intentional, ADR-0090). One helper per entity keeps
    /// the human-readable strings entity-specific while sharing the envelope.
    /// </summary>
    internal static IResult ApplicationNotFound() =>
        ResourceNotFound("Application", "No application with that id is visible in the current tenant.");

    /// <inheritdoc cref="ApplicationNotFound"/>
    internal static IResult ServiceNotFound() =>
        ResourceNotFound("Service", "No service with that id is visible in the current tenant.");

    /// <inheritdoc cref="ApplicationNotFound"/>
    internal static IResult ApiNotFound() =>
        ResourceNotFound("API", "No API with that id is visible in the current tenant.");

    /// <inheritdoc cref="ApplicationNotFound"/>
    internal static IResult SystemNotFound() =>
        ResourceNotFound("System", "No system with that id is visible in the current tenant.");

    private static IResult ResourceNotFound(string entity, string detail) => Results.Problem(
        type: ProblemTypes.ResourceNotFound,
        title: $"{entity} not found",
        detail: detail,
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
