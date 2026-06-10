namespace Kartova.Organization.Application;

public sealed record ChangeMemberRoleCommand(Guid UserId, string Role);

public sealed record ChangeMemberRoleResult(bool Changed, bool NotFound, bool InvalidRole, bool LastOrgAdmin)
{
    public static ChangeMemberRoleResult Success => new(true, false, false, false);
    public static ChangeMemberRoleResult NotFoundResult => new(false, true, false, false);
    public static ChangeMemberRoleResult InvalidRoleResult => new(false, false, true, false);
    public static ChangeMemberRoleResult LastOrgAdminResult => new(false, false, false, true);
}
