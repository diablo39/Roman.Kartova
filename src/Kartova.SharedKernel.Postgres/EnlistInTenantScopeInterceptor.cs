using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Kartova.SharedKernel.Postgres;

/// <summary>
/// Defensive command-creation interceptor that ensures any DbContext command issued
/// inside an active tenant scope participates in the scope's
/// <see cref="INpgsqlTenantScope.Transaction"/>. The primary enlistment path is the
/// eager <c>UseTransaction</c> call performed by the DI decorator in
/// <see cref="AddModuleDbContextExtensions.AddModuleDbContext{TContext}"/>; this
/// interceptor exists as a safety net for any path that obtains a DbContext outside
/// that flow (e.g. a handler that constructs a context manually) so writes still
/// participate in the per-request atomic unit per ADR-0090.
///
/// Hook: EF Core 10 only ships the synchronous
/// <see cref="DbCommandInterceptor.CommandCreating"/> (no <c>CommandCreatingAsync</c>);
/// both sync and async EF execution paths flow through it.
/// </summary>
public sealed class EnlistInTenantScopeInterceptor : DbCommandInterceptor
{
    private readonly INpgsqlTenantScope _scope;

    public EnlistInTenantScopeInterceptor(INpgsqlTenantScope scope)
    {
        _scope = scope;
    }

    public override InterceptionResult<System.Data.Common.DbCommand> CommandCreating(
        CommandCorrelatedEventData eventData,
        InterceptionResult<System.Data.Common.DbCommand> result)
    {
        var dbContext = eventData.Context;
        if (dbContext is not null && _scope.IsActive && dbContext.Database.CurrentTransaction is null)
        {
            dbContext.Database.UseTransaction(_scope.Transaction);
        }
        return result;
    }
}
