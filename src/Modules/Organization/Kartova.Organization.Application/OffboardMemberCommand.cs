namespace Kartova.Organization.Application;

/// <summary>
/// The member being offboarded (hard-deleted). A distinct type from
/// <see cref="OffboardActingUserId"/> so the two ids cannot be transposed at a call site —
/// offboard is destructive, and a silent swap would delete the wrong user while still passing
/// the self-guard. Swapping the arguments is now a compile error.
/// </summary>
public readonly record struct OffboardTargetUserId(Guid Value);

/// <summary>The OrgAdmin performing the offboard; drives the self-offboard guard.</summary>
public readonly record struct OffboardActingUserId(Guid Value);

public sealed record OffboardMemberCommand(OffboardTargetUserId Target, OffboardActingUserId Actor);

/// <summary>
/// Mutually-exclusive terminal outcomes of an offboard command. Modeled as an enum (not a
/// boolean-flag record) per ADR-0104: the operation returns no success payload, so an enum makes
/// illegal states unrepresentable and the endpoint switch exhaustive.
/// </summary>
public enum OffboardMemberOutcome
{
    Offboarded,
    NotFound,
    CannotOffboardSelf,
    LastOrgAdmin,
}
