using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Catalog-side implementation of <see cref="IApplicationOwnerReassigner"/>. Loads every
/// application owned by <c>fromUserId</c> within the active tenant scope (RLS-filtered via the
/// tenant-scoped DbContext), transfers each to <c>toUserId</c>, and persists in one
/// SaveChangesAsync. The save flushes within the ambient request transaction (ADR-0090) — the
/// Organization offboard handler deletes the KeyCloak identity AFTER this returns, so a KC failure
/// rolls the reassignment back with the rest of the request. Mirrors
/// <see cref="ApplicationCountByTeamReader"/> (slice-8 cross-module reader precedent).
/// </summary>
internal sealed class ApplicationOwnerReassigner(CatalogDbContext db) : IApplicationOwnerReassigner
{
    public async Task<int> ReassignOwnerAsync(Guid fromUserId, Guid toUserId, CancellationToken ct)
    {
        var apps = await db.Applications.Where(a => a.OwnerUserId == fromUserId).ToListAsync(ct);
        foreach (var app in apps)
        {
            app.ReassignOwner(toUserId);
        }

        await db.SaveChangesAsync(ct);
        return apps.Count;
    }
}
