using Kartova.Organization.Application;
using Kartova.Organization.Contracts;
using Kartova.Organization.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

/// <summary>
/// Wolverine handler for <see cref="GetTeamQuery"/>. Returns the team's
/// detail view (members + assigned application summaries) or <see langword="null"/>
/// when the team does not exist within the current tenant scope (RLS,
/// ADR-0090). The endpoint delegate translates the null to 404.
///
/// The application list is supplied by <see cref="IApplicationsByTeamReader"/>,
/// a cross-module port implemented by Catalog. This keeps the Organization
/// module from referencing Catalog directly. The cross-module port shape
/// <see cref="ApplicationByTeamSummary"/> is mapped 1:1 onto the wire-shape
/// <see cref="TeamApplicationSummary"/>; the projection makes the layering
/// explicit even though the fields happen to match.
/// </summary>
public sealed class GetTeamHandler
{
    private readonly IApplicationsByTeamReader _appsReader;

    public GetTeamHandler(IApplicationsByTeamReader appsReader)
        => _appsReader = appsReader;

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

        var apps = await _appsReader.GetByTeamAsync(q.Id, ct);
        var summaries = apps
            .Select(a => new TeamApplicationSummary(a.Id, a.DisplayName, a.Lifecycle))
            .ToList();

        return new TeamDetailResponse(
            team.Id.Value,
            team.DisplayName,
            team.Description,
            team.CreatedAt,
            members,
            summaries);
    }
}
