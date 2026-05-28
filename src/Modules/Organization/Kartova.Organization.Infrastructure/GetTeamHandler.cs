using Kartova.Organization.Application;
using Kartova.Organization.Contracts;
using Kartova.Organization.Domain;
using Kartova.SharedKernel.Identity;
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
///
/// Slice 9 / E3 (ADR-0098): each <see cref="TeamMemberResponse"/> row is
/// enriched with the member's display name + email via the
/// <see cref="IUserDirectory"/> port. The lookup is batched
/// (<see cref="IUserDirectory.GetManyAsync"/>) over the distinct user ids on
/// the team so it costs one extra round trip regardless of membership size.
/// Members whose user row is not visible in the current tenant (deleted user,
/// projection lag) surface with empty-string DisplayName + Email per the spec
/// §6.6 non-nullable contract — mirrors the SessionStartHandler fallback.
/// </summary>
public sealed class GetTeamHandler(
    IApplicationsByTeamReader appsReader,
    IUserDirectory directory)
{
    public async Task<TeamDetailResponse?> Handle(
        GetTeamQuery q,
        OrganizationDbContext db,
        CancellationToken ct)
    {
        var team = await db.Teams.FirstOrDefaultAsync(TeamSortSpecs.IdEquals(q.Id), ct);
        if (team is null) return null;

        // Project to a lightweight tuple first so we can batch-resolve display
        // info in a single round trip via IUserDirectory. Sorting on AddedAt is
        // preserved.
        var rawMembers = await db.TeamMembers
            .Where(m => m.TeamId == new TeamId(q.Id))
            .OrderBy(m => m.AddedAt)
            .Select(m => new { m.UserId, m.Role, m.AddedAt })
            .ToListAsync(ct);

        // HashSet de-duplicates in one allocation so a member id that somehow
        // appears twice (shouldn't — PK guarantees uniqueness) costs only one
        // entry in the lookup payload. Mirrors E1's ListApplicationsHandler.
        var memberIds = new HashSet<Guid>(rawMembers.Select(m => m.UserId));
        var directoryEntries = await directory.GetManyAsync(memberIds, ct);

        var members = rawMembers
            .Select(m =>
            {
                var hasInfo = directoryEntries.TryGetValue(m.UserId, out var info);
                return new TeamMemberResponse(
                    m.UserId,
                    m.Role.ToString(),
                    m.AddedAt,
                    hasInfo ? info!.DisplayName : "",
                    hasInfo ? info!.Email : "");
            })
            .ToList();

        var apps = await appsReader.GetByTeamAsync(q.Id, ct);
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
