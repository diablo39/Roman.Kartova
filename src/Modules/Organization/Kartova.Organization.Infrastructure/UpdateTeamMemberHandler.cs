using Kartova.Organization.Application;
using Kartova.Organization.Domain;
using Kartova.SharedKernel.Audit;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

/// <summary>
/// Wolverine handler for <see cref="UpdateTeamMemberCommand"/>. Returns an
/// <see cref="UpdateTeamMemberOutcome"/> distinguishing success (<c>Updated</c>),
/// missing team (<c>TeamNotFound</c>), and missing membership row
/// (<c>MemberNotFound</c>). The endpoint delegate maps these to 204 / 404 / 404.
/// </summary>
public sealed class UpdateTeamMemberHandler(IAuditWriter audit)
{
    public async Task<UpdateTeamMemberOutcome> Handle(
        UpdateTeamMemberCommand cmd,
        OrganizationDbContext db,
        CancellationToken ct)
    {
        var team = await db.Teams.FirstOrDefaultAsync(TeamSortSpecs.IdEquals(cmd.TeamId), ct);
        if (team is null) return UpdateTeamMemberOutcome.TeamNotFound;

        var teamId = new TeamId(cmd.TeamId);
        var membership = await db.TeamMembers
            .FirstOrDefaultAsync(m => m.TeamId == teamId && m.UserId == cmd.UserId, ct);
        if (membership is null) return UpdateTeamMemberOutcome.MemberNotFound;

        var oldRole = membership.Role;
        membership.ChangeRole(cmd.NewRole);
        await db.SaveChangesAsync(ct);
        await audit.AppendAsync(new AuditEntry(
            OrganizationAuditActions.TeamMemberRoleChanged, AuditTargetTypes.Team, cmd.TeamId.ToString(),
            new Dictionary<string, string?>
            {
                ["userId"] = cmd.UserId.ToString(),
                ["oldRole"] = oldRole.ToString(),
                ["newRole"] = cmd.NewRole.ToString(),
            }), ct);
        return UpdateTeamMemberOutcome.Updated;
    }
}
