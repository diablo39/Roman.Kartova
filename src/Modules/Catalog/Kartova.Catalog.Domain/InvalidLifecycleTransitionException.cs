namespace Kartova.Catalog.Domain;

/// <summary>
/// Thrown by Application.EditMetadata / Deprecate / Decommission when an
/// ADR-0073 lifecycle invariant is violated (transition not allowed from the
/// current state, or "decommission before sunset_date" without admin override).
/// Mapped to RFC 7807 409 Conflict by LifecycleConflictExceptionHandler.
/// </summary>
public sealed class InvalidLifecycleTransitionException : InvalidOperationException
{
    public Lifecycle CurrentLifecycle { get; }
    public string AttemptedTransition { get; }
    public DateTimeOffset? SunsetDate { get; }
    public string? Reason { get; }

    public InvalidLifecycleTransitionException(
        Lifecycle current,
        string attempted,
        DateTimeOffset? sunsetDate = null,
        string? reason = null)
        : base(BuildMessage(current, attempted, reason))
    {
        CurrentLifecycle = current;
        AttemptedTransition = attempted;
        SunsetDate = sunsetDate;
        Reason = reason;
    }

    private static string BuildMessage(Lifecycle current, string attempted, string? reason)
        => reason is null
            ? $"Cannot {attempted.ToLowerInvariant()} application currently in state {current}."
            : $"Cannot {attempted.ToLowerInvariant()} application currently in state {current} ({reason}).";
}
