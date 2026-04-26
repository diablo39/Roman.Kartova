using Kartova.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// Endpoint metadata marker applied by <c>RequireTenantScope()</c>. The
/// <see cref="TenantScopeMiddleware"/> inspects the active endpoint for this
/// marker to decide whether to open an <see cref="ITenantScope"/> around the
/// request. See ADR-0090.
/// </summary>
public sealed class RequireTenantScopeMarker
{
    public static readonly RequireTenantScopeMarker Instance = new();
}

/// <summary>
/// Middleware that wraps tenant-scoped endpoints in an <see cref="ITenantScope"/>
/// lifetime. Runs after <c>UseRouting()</c> + <c>UseAuthorization()</c> so the
/// endpoint's <see cref="RequireTenantScopeMarker"/> metadata is visible, and
/// BEFORE endpoint dispatch so <see cref="ITenantScope.BeginAsync"/> completes
/// before the handler's parameter binding resolves any tenant-scoped DbContext.
/// Commits before the response body is flushed; rolls back on exception.
/// </summary>
public sealed class TenantScopeMiddleware
{
    private readonly RequestDelegate _next;

    public TenantScopeMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        var needsScope = endpoint?.Metadata.GetMetadata<RequireTenantScopeMarker>() is not null;

        if (!needsScope)
        {
            await _next(context);
            return;
        }

        var tenantContext = context.RequestServices.GetRequiredService<ITenantContext>();
        if (!tenantContext.IsTenantScoped)
        {
            var problem = Results.Problem(
                type: ProblemTypes.MissingTenantClaim,
                title: "JWT is missing the tenant_id claim",
                statusCode: StatusCodes.Status401Unauthorized);
            await problem.ExecuteAsync(context);
            return;
        }

        var scope = context.RequestServices.GetRequiredService<ITenantScope>();
        var ct = context.RequestAborted;
        await using var handle = await scope.BeginAsync(tenantContext.Id, ct);
        await _next(context);
        await handle.CommitAsync(ct);
    }
}
