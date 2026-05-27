using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Catalog-side implementation of <see cref="IApplicationCountByTeamReader"/>.
/// Returns the number of applications currently assigned to a team within the
/// active tenant scope (filtered by RLS via the tenant-scoped DbContext).
/// Consumed by the Organization module's <c>DeleteTeamHandler</c> to enforce
/// the "team cannot be deleted while it owns applications" invariant.
/// </summary>
internal sealed class ApplicationCountByTeamReader(CatalogDbContext db) : IApplicationCountByTeamReader
{
    public Task<int> CountForTeamAsync(Guid teamId, CancellationToken ct)
        => db.Applications.CountAsync(a => a.TeamId == teamId, ct);
}
