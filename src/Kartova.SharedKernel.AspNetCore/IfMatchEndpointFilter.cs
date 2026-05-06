using Microsoft.AspNetCore.Http;

namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// Reads the <c>If-Match</c> request header on idempotent edit endpoints (PUT)
/// and stores the decoded version (<c>uint</c>) in <c>HttpContext.Items</c>
/// under the key <see cref="ExpectedVersionKey"/>. Endpoint delegates read it
/// from there and pass to the command handler. Missing or malformed header →
/// <see cref="PreconditionRequiredException"/> → 428.
/// </summary>
public sealed class IfMatchEndpointFilter : IEndpointFilter
{
    public const string ExpectedVersionKey = "expected-version";

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var headers = ctx.HttpContext.Request.Headers;
        if (!headers.TryGetValue("If-Match", out var values) || values.Count == 0)
        {
            throw new PreconditionRequiredException(
                "If-Match header is required for this endpoint.");
        }

        var raw = values.ToString().Trim('"');
        if (!VersionEncoding.TryDecode(raw, out var expected))
        {
            throw new PreconditionRequiredException(
                "If-Match header value is not a valid version token.");
        }

        ctx.HttpContext.Items[ExpectedVersionKey] = expected;
        return await next(ctx);
    }
}
