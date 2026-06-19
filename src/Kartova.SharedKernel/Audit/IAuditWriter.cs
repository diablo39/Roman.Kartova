using Kartova.SharedKernel.Multitenancy;

namespace Kartova.SharedKernel.Audit;

/// <summary>
/// Appends one tamper-evident audit row inside the caller's current tenant transaction
/// (ADR-0018 + ADR-0090). Synchronous and fail-closed: if the append throws, the caller's
/// transaction rolls back, so a business mutation can never commit without its audit row.
/// Implemented by the Audit module.
/// </summary>
public interface IAuditWriter
{
    /// <summary>Appends a row attributed to the current authenticated <c>User</c> (from <c>ICurrentUser</c>).</summary>
    Task AppendAsync(AuditEntry entry, CancellationToken ct);

    /// <summary>
    /// Appends a row attributed to the <c>System</c> actor (background jobs with no HTTP principal):
    /// <c>actor_type=System</c>, <c>actor_id=NULL</c>, <c>actor_display="System"</c>. The tenant is
    /// passed explicitly because background callers run outside the request <c>ITenantContext</c>;
    /// the caller must already hold an open <c>ITenantScope</c> for <paramref name="tenant"/>
    /// (the writer's row still rides that transaction and the RLS <c>WITH CHECK</c>).
    /// </summary>
    Task AppendSystemAsync(TenantId tenant, AuditEntry entry, CancellationToken ct);
}
