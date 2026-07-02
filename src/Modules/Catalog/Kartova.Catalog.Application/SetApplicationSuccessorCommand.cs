namespace Kartova.Catalog.Application;

/// <summary>
/// Sets or clears the successor of a Deprecated Application (ADR-0110).
/// <see cref="Kartova.Catalog.Domain.Application.SetSuccessor"/> rejects a
/// non-Deprecated source with <c>InvalidLifecycleTransitionException</c> (409)
/// and a self-reference with <c>ArgumentException</c> (400) — this command
/// layer does not duplicate either guard.
///
/// <c>Id</c> is fully qualified to <see cref="Kartova.Catalog.Domain.ApplicationId"/>
/// because <c>System.ApplicationId</c> exists in the BCL — same trick
/// <see cref="EditApplicationCommand"/> and <see cref="DeprecateApplicationCommand"/> use.
/// </summary>
public sealed record SetApplicationSuccessorCommand(
    Kartova.Catalog.Domain.ApplicationId Id,
    Guid? SuccessorApplicationId);
