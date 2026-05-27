using Kartova.Organization.Application;
using Kartova.Organization.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

/// <summary>
/// Wolverine handler for <see cref="UpdateTeamMemberCommand"/>. Returns an
/// <see cref="UpdateTeamMemberResult"/> distinguishing success (<c>Updated</c>),
/// missing team (<c>TeamNotFound</c>), and missing membership row
/// (<c>MemberNotFound</c>). The endpoint delegate maps these to 204 / 404 / 404.
/// </summary>
public sealed class UpdateTeamMemberHandler
{
    public async Task<UpdateTeamMemberResult> Handle(
        UpdateTeamMemberCommand cmd,
        OrganizationDbContext db,
        CancellationToken ct)
    {
        var team = await db.Teams.FirstOrDefaultAsync(TeamSortSpecs.IdEquals(cmd.TeamId), ct);
        if (team is null) return new UpdateTeamMemberResult(false, true, false);

        var teamId = new TeamId(cmd.TeamId);
        var membership = await db.TeamMembers
            .FirstOrDefaultAsync(m => m.TeamId == teamId && m.UserId == cmd.UserId, ct);
        if (membership is null) return new UpdateTeamMemberResult(false, false, true);

        membership.ChangeRole(cmd.NewRole);
        await db.SaveChangesAsync(ct);
        return new UpdateTeamMemberResult(true, false, false);
    }
}
