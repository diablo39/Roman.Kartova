using Kartova.Organization.Application;
using Kartova.Organization.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

/// <summary>
/// Wolverine handler for <see cref="RemoveTeamMemberCommand"/>. Returns a
/// <see cref="RemoveTeamMemberResult"/> distinguishing success (<c>Removed</c>),
/// missing team (<c>TeamNotFound</c>), and missing membership row
/// (<c>MemberNotFound</c>). The endpoint delegate maps these to 204 / 404 / 404.
/// </summary>
public sealed class RemoveTeamMemberHandler
{
    public async Task<RemoveTeamMemberResult> Handle(
        RemoveTeamMemberCommand cmd,
        OrganizationDbContext db,
        CancellationToken ct)
    {
        var team = await db.Teams.FirstOrDefaultAsync(TeamSortSpecs.IdEquals(cmd.TeamId), ct);
        if (team is null) return new RemoveTeamMemberResult(false, true, false);

        var teamId = new TeamId(cmd.TeamId);
        var membership = await db.TeamMembers
            .FirstOrDefaultAsync(m => m.TeamId == teamId && m.UserId == cmd.UserId, ct);
        if (membership is null) return new RemoveTeamMemberResult(false, false, true);

        db.TeamMembers.Remove(membership);
        await db.SaveChangesAsync(ct);
        return new RemoveTeamMemberResult(true, false, false);
    }
}
