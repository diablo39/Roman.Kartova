using Microsoft.AspNetCore.Http;

namespace Kartova.Catalog.Infrastructure;

internal static class EndpointResultExtensions
{
    /// <summary>
    /// Wraps an existing <see cref="IResult"/> so that the response also emits
    /// an RFC 7232 quoted <c>ETag</c> header carrying <paramref name="version"/>.
    /// Reused by GET-by-id and PUT response paths so clients can capture it for
    /// a future <c>If-Match</c> request. Header writing happens before the inner
    /// result executes (i.e. before the JSON body is written), which is the
    /// supported order in ASP.NET Core minimal-API result execution.
    /// </summary>
    internal static IResult WithEtag(this IResult inner, string version) =>
        new EtagWrappedResult(inner, version);

    private sealed class EtagWrappedResult(IResult inner, string version) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers["ETag"] = $"\"{version}\"";
            await inner.ExecuteAsync(httpContext);
        }
    }
}
