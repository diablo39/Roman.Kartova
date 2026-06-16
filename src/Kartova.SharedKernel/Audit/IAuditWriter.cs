namespace Kartova.SharedKernel.Audit;

/// <summary>
/// Appends one tamper-evident audit row inside the caller's current tenant transaction
/// (ADR-0018 + ADR-0090). Synchronous and fail-closed: if the append throws, the caller's
/// transaction rolls back, so a business mutation can never commit without its audit row.
/// Implemented by the Audit module.
/// </summary>
public interface IAuditWriter
{
    Task AppendAsync(AuditEntry entry, CancellationToken ct);
}
