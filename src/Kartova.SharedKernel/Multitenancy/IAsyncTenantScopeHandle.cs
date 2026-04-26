namespace Kartova.SharedKernel.Multitenancy;

/// <summary>
/// Handle returned by <see cref="ITenantScope.BeginAsync"/>.
/// <see cref="CommitAsync"/> must be called to persist work;
/// <see cref="IAsyncDisposable.DisposeAsync"/> rolls back if commit wasn't reached.
/// </summary>
public interface IAsyncTenantScopeHandle : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct);
}
