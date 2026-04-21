namespace Kartova.SharedKernel;

/// <summary>
/// Base type for domain events. Concrete events are sealed records.
/// </summary>
/// <remarks>
/// Enforced by architecture tests (ADR-0083).
/// </remarks>
public abstract record DomainEvent(DateTimeOffset OccurredAt)
{
    protected DomainEvent() : this(DateTimeOffset.UtcNow) { }
}
