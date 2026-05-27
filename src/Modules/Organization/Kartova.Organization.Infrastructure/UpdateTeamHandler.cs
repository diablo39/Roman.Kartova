using Kartova.Organization.Application;
using Kartova.Organization.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

/// <summary>
/// Wolverine handler for <see cref="UpdateTeamCommand"/>. Returns
/// <see langword="null"/> when the team does not exist within the current
/// tenant scope — the calling endpoint delegate translates this to a 404.
/// Domain invariants (display name length etc.) are enforced by
/// <c>Team.Rename</c> and surface as <see cref="ArgumentException"/>, which
/// the shared <c>DomainValidationExceptionHandler</c> converts to RFC 7807 400.
/// </summary>
public sealed class UpdateTeamHandler
{
    public async Task<TeamResponse?> Handle(
        UpdateTeamCommand cmd,
        OrganizationDbContext db,
        CancellationToken ct)
    {
        var team = await db.Teams.FirstOrDefaultAsync(
            t => EF.Property<Guid>(t, "_id") == cmd.Id, ct);
        if (team is null) return null;

        team.Rename(cmd.DisplayName, cmd.Description);
        await db.SaveChangesAsync(ct);
        return new TeamResponse(team.Id.Value, team.DisplayName, team.Description, team.CreatedAt);
    }
}
