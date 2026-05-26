using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Catalog-side implementation of <see cref="IApplicationIdsByTeamReader"/>.
/// Projects the shadow PK via <see cref="EF.Property{TProperty}"/> against the
/// canonical <see cref="EfApplicationConfiguration.IdFieldName"/> constant, so
/// the magic string lives in exactly one place. The tenant-scoped DbContext
/// applies RLS, so the result is naturally bounded to the active tenant.
/// </summary>
internal sealed class ApplicationIdsByTeamReader(CatalogDbContext db) : IApplicationIdsByTeamReader
{
    public async Task<IReadOnlyList<Guid>> GetIdsByTeamAsync(Guid teamId, CancellationToken ct)
        => await db.Applications
            .Where(a => a.TeamId == teamId)
            .Select(a => EF.Property<Guid>(a, EfApplicationConfiguration.IdFieldName))
            .ToListAsync(ct);
}
