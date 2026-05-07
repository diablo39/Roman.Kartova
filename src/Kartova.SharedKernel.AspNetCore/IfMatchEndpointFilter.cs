using Microsoft.AspNetCore.Http;

namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// Reads the <c>If-Match</c> request header on idempotent edit endpoints (PUT)
/// and stores the decoded version (<c>uint</c>) in <c>HttpContext.Items</c>
/// under the key <see cref="ExpectedVersionKey"/>. Endpoint delegates read it
/// from there and pass to the command handler.
/// <para>
/// Per RFC 7232, <c>If-Match</c> can carry strong ETags (<c>"v"</c>), weak
/// ETags (<c>W/"v"</c>), comma-separated lists (<c>"v1", "v2"</c>), or a
/// wildcard (<c>*</c>). This endpoint accepts only the strong-ETag form (with
/// or without the surrounding quotes — the latter for curl ergonomics) and
/// rejects everything else with a wrong-cause-specific message via
/// <see cref="PreconditionRequiredException"/> → 428. CDN/proxy weak-ETag
/// downgrades surface as a clear "weak ETags not supported" error instead of
/// a misleading "not a valid version token".
/// </para>
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

        // StringValues.ToString() joins multi-value headers with commas — the
        // comma check below rejects both syntactic forms (single header with
        // a list value, or repeated If-Match headers).
        var raw = values.ToString().Trim();
        if (raw.Length == 0)
        {
            throw new PreconditionRequiredException(
                "If-Match header is required for this endpoint.");
        }

        if (raw == "*")
        {
            throw new PreconditionRequiredException(
                "If-Match: * (wildcard) is not supported on this endpoint. " +
                "Supply the strong ETag from a prior GET.");
        }

        if (raw.StartsWith("W/", StringComparison.Ordinal))
        {
            throw new PreconditionRequiredException(
                "Weak ETags (W/\"...\") are not supported on this endpoint. " +
                "Supply the strong ETag from a prior GET.");
        }

        if (raw.Contains(','))
        {
            throw new PreconditionRequiredException(
                "If-Match list (multiple ETags) is not supported on this endpoint. " +
                "Supply a single strong ETag.");
        }

        // Strict "v" or bare v parsing. Mismatched/embedded quotes are explicit
        // grammar errors, not "valid base64 with junk".
        string token;
        if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
        {
            token = raw[1..^1];
        }
        else if (raw.Contains('"'))
        {
            throw new PreconditionRequiredException(
                "If-Match header value is not a valid version token.");
        }
        else
        {
            token = raw;
        }

        if (!VersionEncoding.TryDecode(token, out var expected))
        {
            throw new PreconditionRequiredException(
                "If-Match header value is not a valid version token.");
        }

        ctx.HttpContext.Items[ExpectedVersionKey] = expected;
        return await next(ctx);
    }
}
