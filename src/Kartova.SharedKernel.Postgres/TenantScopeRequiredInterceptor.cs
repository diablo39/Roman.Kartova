using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Kartova.SharedKernel.Postgres;

/// <summary>
/// Fail-fast SaveChanges interceptor: if a tenant-scoped DbContext tries to persist changes
/// and the ambient ITenantScope is not active, throw. Catches "new endpoint added without
/// the tenant-scope filter" during first integration test run.
/// </summary>
public sealed class TenantScopeRequiredInterceptor : SaveChangesInterceptor
{
    private readonly ITenantScope _scope;

    public TenantScopeRequiredInterceptor(ITenantScope scope)
    {
        _scope = scope;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        AssertScopeActive();
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        AssertScopeActive();
        return ValueTask.FromResult(result);
    }

    private void AssertScopeActive()
    {
        if (!_scope.IsActive)
        {
            throw new InvalidOperationException(
                "Attempted to SaveChanges on a tenant-scoped DbContext without an active ITenantScope. "
                + "Either the endpoint is missing TenantScopeEndpointFilter / RequireTenantScope(), "
                + "or the handler is running outside a transport adapter. See ADR-0090.");
        }
    }
}
