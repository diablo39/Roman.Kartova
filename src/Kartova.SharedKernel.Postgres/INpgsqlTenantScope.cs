using Kartova.SharedKernel.Multitenancy;
using Npgsql;

namespace Kartova.SharedKernel.Postgres;

/// <summary>
/// Postgres-specific extension of <see cref="ITenantScope"/> that exposes the
/// already-open connection and active transaction so module DbContexts (and the
/// optional EnlistInTenantScopeInterceptor) can share them (ADR-0090 §3.1).
/// Consumed only inside <see cref="Kartova.SharedKernel.Postgres"/> and by tests
/// that drive the mechanism directly. Application code depends on
/// <see cref="ITenantScope"/>.
/// </summary>
public interface INpgsqlTenantScope : ITenantScope
{
    NpgsqlConnection Connection { get; }
    NpgsqlTransaction Transaction { get; }
}
