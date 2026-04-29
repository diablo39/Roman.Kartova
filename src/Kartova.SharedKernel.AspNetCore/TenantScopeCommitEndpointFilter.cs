using Kartova.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Http;

namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// Commits the active <see cref="ITenantScope"/> transaction BETWEEN handler return and
/// <see cref="IResult.ExecuteAsync"/>, preserving ADR-0090's durability promise: if commit
/// fails, the exception bubbles to <c>UseExceptionHandler</c> and surfaces as 500 +
/// RFC 7807 problem-details — the client never sees a partial body for a transaction
/// that failed to commit. Streaming responses (<c>Results.Stream</c>, SSE,
/// <c>IAsyncEnumerable&lt;T&gt;</c>) are also durability-correct because the IResult is
/// returned but not yet executed when commit runs.
///
/// Pairs with <see cref="TenantScopeBeginMiddleware"/> which opens the scope and stashes
/// the handle in <c>HttpContext.Items[HandleKey]</c>. Missing key indicates a wiring bug
/// (filter attached without the begin-middleware in the request pipeline) and surfaces
/// immediately as <see cref="InvalidOperationException"/> rather than silently committing
/// nothing.
/// </summary>
public sealed class TenantScopeCommitEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext ctx,
        EndpointFilterDelegate next)
    {
        var result = await next(ctx);   // handler returns IResult — NOT yet executed

        if (!ctx.HttpContext.Items.TryGetValue(TenantScopeBeginMiddleware.HandleKey, out var obj)
            || obj is not IAsyncTenantScopeHandle handle)
        {
            throw new InvalidOperationException(
                "TenantScopeCommitEndpointFilter ran without an active scope handle. " +
                "TenantScopeBeginMiddleware must be wired in the request pipeline " +
                "(app.UseMiddleware<TenantScopeBeginMiddleware>() before endpoint dispatch).");
        }

        await handle.CommitAsync(ctx.HttpContext.RequestAborted);
        return result;   // ASP.NET runs IResult.ExecuteAsync AFTER commit succeeds
    }
}
