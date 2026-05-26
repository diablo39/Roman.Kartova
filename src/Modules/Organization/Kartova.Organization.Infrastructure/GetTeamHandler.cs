using Kartova.Organization.Application;
using Kartova.Organization.Contracts;
using Kartova.Organization.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

/// <summary>
/// Wolverine handler for <see cref="GetTeamQuery"/>. Returns the team's
/// detail view (members + assigned application ids) or <see langword="null"/>
/// when the team does not exist within the current tenant scope (RLS,
/// ADR-0090). The endpoint delegate translates the null to 404.
///
/// The application id list is supplied by <see cref="IApplicationIdsByTeamReader"/>,
/// a cross-module port implemented by Catalog. This keeps the Organization
/// module from referencing Catalog directly.
/// </summary>
public sealed class GetTeamHandler
{
    private readonly IApplicationIdsByTeamReader _appIdsReader;

    public GetTeamHandler(IApplicationIdsByTeamReader appIdsReader)
        => _appIdsReader = appIdsReader;

    public async Task<TeamDetailResponse?> Handle(
        GetTeamQuery q,
        OrganizationDbContext db,
        CancellationToken ct)
    {
        var team = await db.Teams.FirstOrDefaultAsync(TeamSortSpecs.IdEquals(q.Id), ct);
        if (team is null) return null;

        var members = await db.TeamMembers
            .Where(m => m.TeamId == new TeamId(q.Id))
            .OrderBy(m => m.AddedAt)
            .Select(m => new TeamMemberResponse(m.UserId, m.Role.ToString(), m.AddedAt))
            .ToListAsync(ct);

        var appIds = await _appIdsReader.GetIdsByTeamAsync(q.Id, ct);

        return new TeamDetailResponse(
            team.Id.Value,
            team.DisplayName,
            team.Description,
            team.CreatedAt,
            members,
            appIds);
    }
}
