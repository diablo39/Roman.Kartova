using Kartova.SharedKernel.Multitenancy;
using Npgsql;

namespace Kartova.SharedKernel.Postgres;

/// <summary>
/// Postgres-specific extension of <see cref="ITenantScope"/> that exposes the
/// already-open connection so module DbContexts can share it (ADR-0090).
/// Consumed only inside <see cref="Kartova.SharedKernel.Postgres"/> — application
/// code should depend on <see cref="ITenantScope"/>.
/// </summary>
public interface INpgsqlTenantScope : ITenantScope
{
    NpgsqlConnection Connection { get; }
}
