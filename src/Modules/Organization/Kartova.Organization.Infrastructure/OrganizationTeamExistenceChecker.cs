using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

/// <summary>
/// Cross-module implementation of <see cref="IOrganizationTeamExistenceChecker"/>.
/// Uses <see cref="TeamSortSpecs.IdEquals"/> so the predicate over the private
/// <c>_id</c> shadow property stays centralized in <see cref="TeamSortSpecs"/>.
/// </summary>
internal sealed class OrganizationTeamExistenceChecker(OrganizationDbContext db)
    : IOrganizationTeamExistenceChecker
{
    public Task<bool> ExistsAsync(Guid teamId, CancellationToken ct)
        => db.Teams.AnyAsync(TeamSortSpecs.IdEquals(teamId), ct);
}
