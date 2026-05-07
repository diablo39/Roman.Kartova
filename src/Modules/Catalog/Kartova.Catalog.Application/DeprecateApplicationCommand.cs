namespace Kartova.Catalog.Application;

/// <summary>
/// Deprecate an existing Application — Active → Deprecated transition (ADR-0073).
/// No <c>ExpectedVersion</c> field: lifecycle endpoints rely on the domain invariant
/// ("current state must be Active") rather than optimistic locking (slice 5 spec §3
/// Decision #7). <see cref="Kartova.Catalog.Domain.Application.Deprecate"/> rejects
/// non-Active sources with <c>InvalidLifecycleTransitionException</c>, which the
/// shared <c>LifecycleConflictExceptionHandler</c> maps to RFC 7807 409.
///
/// <c>Id</c> is fully qualified to <see cref="Kartova.Catalog.Domain.ApplicationId"/>
/// because <c>System.ApplicationId</c> exists in the BCL — same trick
/// <see cref="EditApplicationCommand"/> uses.
/// </summary>
public sealed record DeprecateApplicationCommand(
    Kartova.Catalog.Domain.ApplicationId Id,
    DateTimeOffset SunsetDate);
