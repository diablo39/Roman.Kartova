using Kartova.Organization.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

internal sealed class OrganizationTeamMembershipReader(OrganizationDbContext db) : ITeamMembershipReader
{
    public async Task<IReadOnlyList<TeamMembershipInfo>> GetForUserAsync(Guid userId, CancellationToken ct)
    {
        if (userId == Guid.Empty) return Array.Empty<TeamMembershipInfo>();

        var rows = await db.TeamMembers
            .Where(m => m.UserId == userId)
            .Select(m => new TeamMembershipInfo(m.TeamId.Value, (TeamRoleKind)(byte)m.Role))
            .ToListAsync(ct);

        return rows;
    }
}
