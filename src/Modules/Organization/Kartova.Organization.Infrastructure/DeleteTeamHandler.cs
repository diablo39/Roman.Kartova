using Kartova.Organization.Application;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

/// <summary>
/// Wolverine handler for <see cref="DeleteTeamCommand"/>. Returns a
/// <see cref="DeleteTeamResult"/> that distinguishes between success
/// (<c>Deleted</c>), missing team (<c>NotFound</c>), and a blocked delete due
/// to applications still assigned to the team
/// (<c>ApplicationsAssigned &gt; 0</c>). The endpoint delegate translates these
/// to 204 / 404 / 409 respectively.
///
/// The application count is supplied by <see cref="IApplicationCountByTeamReader"/>,
/// a cross-module port implemented by Catalog. This keeps the Organization
/// module from referencing Catalog directly.
/// </summary>
public sealed class DeleteTeamHandler
{
    private readonly IApplicationCountByTeamReader _appCountReader;

    public DeleteTeamHandler(IApplicationCountByTeamReader appCountReader)
        => _appCountReader = appCountReader;

    public async Task<DeleteTeamResult> Handle(
        DeleteTeamCommand cmd,
        OrganizationDbContext db,
        CancellationToken ct)
    {
        var team = await db.Teams.FirstOrDefaultAsync(
            t => EF.Property<Guid>(t, "_id") == cmd.Id, ct);
        if (team is null) return new DeleteTeamResult(false, true, null);

        var appCount = await _appCountReader.CountForTeamAsync(team.Id.Value, ct);
        if (appCount > 0) return new DeleteTeamResult(false, false, appCount);

        db.Teams.Remove(team);
        await db.SaveChangesAsync(ct);
        return new DeleteTeamResult(true, false, null);
    }
}
