using Kartova.Organization.Application;
using Kartova.Organization.Domain;
using Kartova.SharedKernel.Audit;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

/// <summary>
/// Wolverine handler for <see cref="RemoveTeamMemberCommand"/>. Returns a
/// <see cref="RemoveTeamMemberOutcome"/> distinguishing success (<c>Removed</c>),
/// missing team (<c>TeamNotFound</c>), and missing membership row
/// (<c>MemberNotFound</c>). The endpoint delegate maps these to 204 / 404 / 404.
/// </summary>
public sealed class RemoveTeamMemberHandler(IAuditWriter audit)
{
    public async Task<RemoveTeamMemberOutcome> Handle(
        RemoveTeamMemberCommand cmd,
        OrganizationDbContext db,
        CancellationToken ct)
    {
        var team = await db.Teams.FirstOrDefaultAsync(TeamSortSpecs.IdEquals(cmd.TeamId), ct);
        if (team is null) return RemoveTeamMemberOutcome.TeamNotFound;

        var teamId = new TeamId(cmd.TeamId);
        var membership = await db.TeamMembers
            .FirstOrDefaultAsync(m => m.TeamId == teamId && m.UserId == cmd.UserId, ct);
        if (membership is null) return RemoveTeamMemberOutcome.MemberNotFound;

        db.TeamMembers.Remove(membership);
        await db.SaveChangesAsync(ct);
        await audit.AppendAsync(new AuditEntry(
            OrganizationAuditActions.TeamMemberRemoved, AuditTargetTypes.Team, cmd.TeamId.ToString(),
            new Dictionary<string, string?> { ["userId"] = cmd.UserId.ToString() }), ct);
        return RemoveTeamMemberOutcome.Removed;
    }
}
