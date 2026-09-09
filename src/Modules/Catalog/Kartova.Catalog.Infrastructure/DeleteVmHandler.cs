using Kartova.Catalog.Application;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Direct-dispatch handler for <see cref="DeleteVmCommand"/> — hard delete (ADR-0111
/// amendment, slice 2a Task 6). Returns <see langword="false"/> when no VM-kind row is visible
/// in the current tenant scope (RLS auto-filters cross-tenant rows, so an unknown id and a
/// cross-tenant id both surface as "not found" here — mirrors <see cref="EditVmHandler"/>).
/// Concurrency: sets <c>OriginalValue(Xmin)</c> to the supplied <c>ExpectedVersion</c> so EF's
/// generated <c>DELETE ... WHERE xmin = :expected</c> raises
/// <see cref="DbUpdateConcurrencyException"/> on a stale version (mapped to 412 upstream, never
/// 409). VM has no lifecycle and no relationship edges exist yet in slice 2a, so this is a plain
/// <c>db.Infrastructure.Remove</c> — no soft-delete, no cascading cleanup.
/// </summary>
public sealed class DeleteVmHandler
{
    public async Task<bool> Handle(
        DeleteVmCommand cmd, CatalogDbContext db, IAuditWriter audit, ILogger<DeleteVmHandler> logger, CancellationToken ct)
    {
        var vm = await db.Infrastructure
            .SingleOrDefaultAsync(VmSortSpecs.IdEquals(cmd.Id.Value), ct);
        if (vm is null) return false;

        db.Entry(vm).Property(x => x.Xmin).OriginalValue = cmd.ExpectedVersion;
        db.Infrastructure.Remove(vm);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Shared with EditVmHandler (Task 6 DRY extraction) — see InfrastructureConcurrency.
            await InfrastructureConcurrency.TryCaptureCurrentXminAsync(ex, cmd.Id.Value, logger, ct);
            throw;
        }

        await audit.AppendAsync(new AuditEntry(
            CatalogAuditActions.InfrastructureDeleted,
            CatalogAuditTargetTypes.Infrastructure,
            vm.Id.Value.ToString(),
            new Dictionary<string, string?>
            {
                ["displayName"] = vm.DisplayName,
                ["provider"] = vm.Provider,
            }), ct);

        return true;
    }
}
