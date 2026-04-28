using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Kartova.SharedKernel.Postgres;

/// <summary>
/// ADR-0090 implementation. Scoped DI; one per request.
/// </summary>
public sealed class TenantScope : INpgsqlTenantScope
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<TenantScope> _logger;
    private NpgsqlConnection? _connection;
    private NpgsqlTransaction? _transaction;
    private bool _committed;

    public TenantScope(NpgsqlDataSource dataSource, ILogger<TenantScope> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    public bool IsActive => _connection is not null && _transaction is not null;

    public NpgsqlConnection Connection =>
        _connection ?? throw new InvalidOperationException(
            "TenantScope is not active. BeginAsync must be called by the transport adapter before any DbContext is used.");

    public NpgsqlTransaction Transaction =>
        _transaction ?? throw new InvalidOperationException(
            "TenantScope has no active transaction.");

    public async Task<IAsyncTenantScopeHandle> BeginAsync(Kartova.SharedKernel.Multitenancy.TenantId id, CancellationToken ct)
    {
        if (_connection is not null)
        {
            throw new InvalidOperationException("TenantScope already begun for this request.");
        }

        try
        {
            _connection = await _dataSource.OpenConnectionAsync(ct);
            _transaction = await _connection.BeginTransactionAsync(ct);

            await using var cmd = _connection.CreateCommand();
            cmd.Transaction = _transaction;
            // set_config(name, value, is_local) is used instead of `SET LOCAL` because
            // PostgreSQL's SET statement does not accept bound parameters.
            cmd.CommandText = "SELECT set_config('app.current_tenant_id', $1, true)";
            cmd.Parameters.AddWithValue(id.Value.ToString());
            await cmd.ExecuteNonQueryAsync(ct);

            return new Handle(this);
        }
        catch (NpgsqlException npg)
        {
            // Leave the scope in a clean uninitialized state so the caller can surface the
            // underlying error instead of a confusing "already begun" on retry.
            if (_transaction is not null)
            {
                try { await _transaction.DisposeAsync(); } catch { /* best-effort */ }
                _transaction = null;
            }
            if (_connection is not null)
            {
                try { await _connection.DisposeAsync(); } catch { /* best-effort */ }
                _connection = null;
            }
            throw new TenantScopeBeginException(
                "Failed to begin tenant scope: database unavailable or connection failure.",
                npg);
        }
        catch
        {
            // Leave the scope in a clean uninitialized state so the caller can surface the
            // underlying error instead of a confusing "already begun" on retry.
            if (_transaction is not null)
            {
                try { await _transaction.DisposeAsync(); } catch { /* best-effort */ }
                _transaction = null;
            }
            if (_connection is not null)
            {
                try { await _connection.DisposeAsync(); } catch { /* best-effort */ }
                _connection = null;
            }
            throw;
        }
    }

    private async Task CommitAsync(CancellationToken ct)
    {
        if (_transaction is null)
        {
            throw new InvalidOperationException("Cannot commit — scope not active.");
        }
        await _transaction.CommitAsync(ct);
        _committed = true;
    }

    private async ValueTask DisposeAsyncCore()
    {
        if (_transaction is not null)
        {
            if (!_committed)
            {
                try
                {
                    await _transaction.RollbackAsync();
                }
                catch (NpgsqlException ex)
                {
                    _logger.LogWarning(ex, "TenantScope rollback failed; connection may be broken.");
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(ex, "TenantScope rollback observed an invalid transaction state.");
                }
            }
            await _transaction.DisposeAsync();
            _transaction = null;
        }
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }

    private sealed class Handle : IAsyncTenantScopeHandle
    {
        private readonly TenantScope _scope;
        private bool _disposed;

        public Handle(TenantScope scope) => _scope = scope;

        public Task CommitAsync(CancellationToken ct) => _scope.CommitAsync(ct);

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            await _scope.DisposeAsyncCore();
        }
    }
}
