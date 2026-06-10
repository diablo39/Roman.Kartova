using Kartova.Catalog.Application;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Direct-dispatch handler for <see cref="AssignApplicationTeamCommand"/>. Returns
/// <see cref="AssignApplicationTeamResult.NotFound"/> when no row is visible in
/// the current tenant scope (RLS-hidden or deleted between the delegate's
/// pre-load and the handler reload — defensive 404, same pattern as
/// <c>UpdateTeamAsync</c>). The target team (required, ADR-0103) must exist in the
/// active tenant — mismatch returns <see cref="AssignApplicationTeamResult.InvalidTeam"/>
/// → 422 (spec §6.4). TOCTOU between the existence check and the assignment is
/// accepted as best-effort for slice 8, same as <c>DeleteTeamHandler</c>'s
/// count-check (Task 14). <see cref="Kartova.Catalog.Domain.Application.AssignTeam"/>
/// throws <c>InvalidLifecycleTransitionException</c> on Decommissioned apps; the
/// shared <c>LifecycleConflictExceptionHandler</c> maps it to 409, so the handler
/// does not catch it.
/// </summary>
public sealed class AssignApplicationTeamHandler(IOrganizationTeamExistenceChecker teamChecker)
{
    public async Task<AssignApplicationTeamResult> Handle(
        AssignApplicationTeamCommand cmd,
        CatalogDbContext db,
        CancellationToken ct)
    {
        var app = await db.Applications
            .FirstOrDefaultAsync(ApplicationSortSpecs.IdEquals(cmd.Id), ct);
        if (app is null) return AssignApplicationTeamResult.NotFound;

        var exists = await teamChecker.ExistsAsync(cmd.TeamId, ct);
        if (!exists) return AssignApplicationTeamResult.InvalidTeam;

        app.AssignTeam(cmd.TeamId);
        await db.SaveChangesAsync(ct);
        return AssignApplicationTeamResult.Success(app);
    }
}
