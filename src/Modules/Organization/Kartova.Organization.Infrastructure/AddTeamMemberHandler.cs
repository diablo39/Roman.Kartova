using Kartova.Organization.Application;
using Kartova.Organization.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

/// <summary>
/// Wolverine handler for <see cref="AddTeamMemberCommand"/>. Returns an
/// <see cref="AddTeamMemberResult"/> distinguishing success (<c>Added</c>),
/// missing team (<c>TeamNotFound</c>), and an existing membership row
/// (<c>AlreadyMember</c>). The endpoint delegate maps these to 201 / 404 / 409.
/// </summary>
public sealed class AddTeamMemberHandler
{
    private readonly TimeProvider _clock;

    public AddTeamMemberHandler(TimeProvider clock) => _clock = clock;

    public async Task<AddTeamMemberResult> Handle(
        AddTeamMemberCommand cmd,
        OrganizationDbContext db,
        CancellationToken ct)
    {
        var team = await db.Teams.FirstOrDefaultAsync(TeamSortSpecs.IdEquals(cmd.TeamId), ct);
        if (team is null) return new AddTeamMemberResult(false, true, false, null);

        var teamId = new TeamId(cmd.TeamId);
        var existing = await db.TeamMembers
            .FirstOrDefaultAsync(m => m.TeamId == teamId && m.UserId == cmd.UserId, ct);
        if (existing is not null) return new AddTeamMemberResult(false, false, true, null);

        var membership = TeamMembership.Create(teamId, cmd.UserId, cmd.Role, _clock);
        db.TeamMembers.Add(membership);
        await db.SaveChangesAsync(ct);
        // Surface the canonical AddedAt the aggregate set — the endpoint mirrors
        // this to the wire so clients see the value the DB persisted, not a
        // separately-clocked wall-clock snapshot taken in the endpoint delegate
        // (slice-boundary review fix).
        return new AddTeamMemberResult(true, false, false, membership.AddedAt);
    }
}
