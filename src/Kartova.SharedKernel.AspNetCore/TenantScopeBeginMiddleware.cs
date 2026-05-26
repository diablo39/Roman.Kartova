using Kartova.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// Opens an <see cref="ITenantScope"/> for endpoints carrying <see cref="RequireTenantScopeMarker"/>
/// metadata. Runs AFTER <c>UseAuthentication</c>/<c>UseAuthorization</c> so the JWT-derived
/// <see cref="ITenantContext"/> is populated, and BEFORE endpoint dispatch so DI-injected
/// DbContexts (registered via <c>AddModuleDbContext</c>) resolve against an active scope.
///
/// Pairs with <see cref="TenantScopeCommitEndpointFilter"/> which commits between handler
/// return and <c>IResult.ExecuteAsync</c>. This middleware owns the handle's
/// <see cref="IAsyncDisposable.DisposeAsync"/> lifetime — the <c>finally</c> block runs after
/// the filter chain unwinds, so rollback fires automatically on any non-committed exit
/// (handler exception, commit failure, cancellation).
///
/// Pipeline order in <c>Program.cs</c>:
///   UseAuthentication → UseAuthorization → UseMiddleware&lt;TenantScopeBeginMiddleware&gt;
///   → endpoint dispatch (parameter binding, filter chain, handler, IResult.ExecuteAsync).
///
/// See ADR-0090 §Addendum (2026-04-28) for why this is split from the commit filter.
/// </summary>
public sealed class TenantScopeBeginMiddleware
{
    /// <summary>
    /// Key under which the active <see cref="IAsyncTenantScopeHandle"/> is stored in
    /// <see cref="HttpContext.Items"/> for retrieval by <see cref="TenantScopeCommitEndpointFilter"/>.
    /// Internal to this assembly; the only reader is the commit filter.
    /// </summary>
    internal const string HandleKey = "Kartova.TenantScope.Handle";

    private readonly RequestDelegate _next;

    public TenantScopeBeginMiddleware(RequestDelegate next)
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
            await Results.Problem(
                type: ProblemTypes.MissingTenantClaim,
                title: "JWT is missing the tenant_id claim",
                statusCode: StatusCodes.Status401Unauthorized).ExecuteAsync(context);
            return;
        }

        var scope = context.RequestServices.GetRequiredService<ITenantScope>();
        var ct = context.RequestAborted;

        IAsyncTenantScopeHandle handle;
        try
        {
            handle = await scope.BeginAsync(tenantContext.Id, ct);
        }
        catch (TenantScopeBeginException)
        {
            await Results.Problem(
                type: ProblemTypes.ServiceUnavailable,
                title: "Database is currently unavailable",
                statusCode: StatusCodes.Status503ServiceUnavailable).ExecuteAsync(context);
            return;
        }

        // Hand off to the commit filter via Items; middleware retains DisposeAsync ownership
        // so rollback fires on any non-committed exit (handler exception, commit failure, cancel,
        // OR membership-population failure).
        context.Items[HandleKey] = handle;
        try
        {
            // Populate team memberships AFTER BeginAsync so the request-scoped DbContext
            // resolves against a connection where SET LOCAL app.current_tenant_id has executed
            // — the team_members RLS policy cannot evaluate otherwise. Resolved from
            // RequestServices (not the root provider) so the reader's DbContext shares the
            // same DI scope as the connection the membership query will run on.
            // Falls back silently if the reader isn't registered (e.g., Organization module
            // not loaded) — downstream code sees an empty memberships collection.
            // Population runs inside the try block so a reader exception triggers the
            // finally's DisposeAsync, releasing the NpgsqlConnection + transaction.
            var sub = context.User.FindFirst("sub")?.Value;
            if (Guid.TryParse(sub, out var userId))
            {
                var reader = context.RequestServices.GetService<ITeamMembershipReader>();
                if (reader is not null)
                {
                    var memberships = await reader.GetForUserAsync(userId, ct);
                    tenantContext.PopulateTeamMemberships(memberships);
                }
            }

            await _next(context);   // parameter binding + filter chain + handler + IResult.ExecuteAsync
        }
        finally
        {
            await handle.DisposeAsync();
        }
    }
}
