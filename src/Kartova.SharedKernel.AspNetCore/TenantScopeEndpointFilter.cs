using Kartova.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// Wraps tenant-scoped endpoints in an <see cref="ITenantScope"/> lifetime.
/// Commits before ASP.NET writes the response body — commit failures surface as 500.
/// Rolls back on any exception and on un-committed dispose.
/// See ADR-0090.
/// </summary>
public sealed class TenantScopeEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var ct = context.HttpContext.RequestAborted;
        var tenantContext = context.HttpContext.RequestServices.GetRequiredService<ITenantContext>();
        var scope = context.HttpContext.RequestServices.GetRequiredService<ITenantScope>();

        if (!tenantContext.IsTenantScoped)
        {
            return Results.Problem(
                type: ProblemTypes.MissingTenantClaim,
                title: "JWT is missing the tenant_id claim",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        await using var handle = await scope.BeginAsync(tenantContext.Id, ct);
        var result = await next(context);
        await handle.CommitAsync(ct);
        return result;
    }
}
