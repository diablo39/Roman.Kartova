namespace Kartova.Organization.Application;

/// <summary>
/// Audit action taxonomy for Organization-module mutations (spec §4). Action
/// strings are the stable contract written to <c>audit_log.action</c>; do not
/// rename without a migration of historical rows.
/// </summary>
public static class OrganizationAuditActions
{
    public const string MemberRoleChanged = "member.role_changed";
    public const string MemberOffboarded = "member.offboarded";
    public const string TeamCreated = "team.created";
    public const string TeamUpdated = "team.updated";
    public const string TeamDeleted = "team.deleted";
    public const string TeamMemberAdded = "team.member_added";
    public const string TeamMemberRemoved = "team.member_removed";
    public const string TeamMemberRoleChanged = "team.member_role_changed";
    public const string InvitationCreated = "invitation.created";
    public const string OrgProfileUpdated = "org.profile_updated";
}

/// <summary>Audit <c>target_type</c> literals (spec §4).</summary>
public static class AuditTargetTypes
{
    public const string User = "User";
    public const string Team = "Team";
    public const string Invitation = "Invitation";
    public const string Organization = "Organization";
}
