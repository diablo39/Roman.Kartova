using System.Diagnostics.CodeAnalysis;

namespace Kartova.SharedKernel.Multitenancy;

/// <summary>
/// Thrown by <see cref="ITenantScope.BeginAsync"/> when the underlying storage cannot
/// open a connection or begin a transaction (database unavailable, pool exhausted,
/// network failure). Transport adapters (HTTP filter, Wolverine middleware) catch this
/// type to map to their respective transport-level "service unavailable" semantics —
/// e.g. HTTP 503 + RFC 7807 problem-details per ADR-0091, or Wolverine retry/DLQ.
///
/// The inner exception carries the storage-specific diagnostic detail (e.g. NpgsqlException).
/// See ADR-0090 §Error handling.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class TenantScopeBeginException : Exception
{
    public TenantScopeBeginException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
