namespace Kartova.SharedKernel;

/// <summary>
/// Marker interface for module exceptions that signal a lifecycle invariant
/// violation (e.g. transition not allowed from current state, or "decommission
/// before sunset_date" without admin override). Implementing exceptions are
/// mapped to RFC 7807 <c>409 Conflict</c> by
/// <c>LifecycleConflictExceptionHandler</c> in <c>Kartova.SharedKernel.AspNetCore</c>.
/// Defined here so the handler can match by typed contract instead of reflection
/// without coupling SharedKernel.AspNetCore to any specific module's domain.
/// </summary>
public interface ILifecycleConflict
{
    /// <summary>
    /// Wire-shape lowercase camelCase name of the current lifecycle state
    /// (e.g. <c>"active"</c>, <c>"deprecated"</c>, <c>"decommissioned"</c>).
    /// MUST match the casing the corresponding enum field uses on the wire
    /// via <c>JsonStringEnumConverter(JsonNamingPolicy.CamelCase)</c> (ADR-0095) so
    /// clients can compare <c>application.lifecycle === problem.currentLifecycle</c>.
    /// </summary>
    string CurrentLifecycleName { get; }

    /// <summary>Name of the rejected transition (e.g. "Deprecate", "Decommission", "EditMetadata").</summary>
    string AttemptedTransition { get; }

    /// <summary>Sunset date attached to the conflict, when relevant.</summary>
    DateTimeOffset? SunsetDate { get; }

    /// <summary>Discriminator for sub-cases (e.g. <c>"before-sunset-date"</c>), when relevant.</summary>
    string? Reason { get; }
}
