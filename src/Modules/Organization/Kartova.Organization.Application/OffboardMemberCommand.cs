namespace Kartova.Organization.Application;

public sealed record OffboardMemberCommand(Guid UserId, Guid ActingUserId);

public sealed record OffboardMemberResult(
    bool Offboarded, bool NotFound, bool CannotOffboardSelf, bool LastOrgAdmin)
{
    public static OffboardMemberResult Success => new(true, false, false, false);
    public static OffboardMemberResult NotFoundResult => new(false, true, false, false);
    public static OffboardMemberResult SelfResult => new(false, false, true, false);
    public static OffboardMemberResult LastOrgAdminResult => new(false, false, false, true);
}
