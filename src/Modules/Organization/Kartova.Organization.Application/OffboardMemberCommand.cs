namespace Kartova.Organization.Application;

public sealed record OffboardMemberCommand(Guid UserId, Guid SuccessorUserId, Guid ActingUserId);

public sealed record OffboardMemberResult(
    bool Offboarded, bool NotFound, bool CannotOffboardSelf, bool LastOrgAdmin, bool InvalidSuccessor, int AppsReassigned)
{
    public static OffboardMemberResult Success(int apps) => new(true, false, false, false, false, apps);
    public static OffboardMemberResult NotFoundResult => new(false, true, false, false, false, 0);
    public static OffboardMemberResult SelfResult => new(false, false, true, false, false, 0);
    public static OffboardMemberResult LastOrgAdminResult => new(false, false, false, true, false, 0);
    public static OffboardMemberResult InvalidSuccessorResult => new(false, false, false, false, true, 0);
}
