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

public sealed record OffboardMemberResult(
    bool Offboarded, bool NotFound, bool CannotOffboardSelf, bool LastOrgAdmin)
{
    public static OffboardMemberResult Success => new(true, false, false, false);
    public static OffboardMemberResult NotFoundResult => new(false, true, false, false);
    public static OffboardMemberResult SelfResult => new(false, false, true, false);
    public static OffboardMemberResult LastOrgAdminResult => new(false, false, false, true);
}
