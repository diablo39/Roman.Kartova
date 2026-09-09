using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Audit;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Direct-dispatch handler for <see cref="EditVmCommand"/>. Returns <c>null</c> when no
/// VM-kind row is visible in the current tenant scope (RLS auto-filters cross-tenant
/// rows — handler does not need an explicit tenant id). Concurrency: sets
/// <c>OriginalValue(Xmin)</c> to the supplied <c>ExpectedVersion</c> so EF's UPDATE
/// includes <c>WHERE xmin = :expected</c>; mismatch raises
/// <see cref="DbUpdateConcurrencyException"/> → 412. Mirrors <see cref="EditApplicationHandler"/>,
/// reading <c>dbValues["Xmin"]</c> (not <c>"Version"</c>) — Infrastructure's concurrency
/// property is <see cref="InfrastructureResource.Xmin"/>.
/// </summary>
public sealed class EditVmHandler
{
    public async Task<VmDetailResponse?> Handle(
        EditVmCommand cmd, CatalogDbContext db, IAuditWriter audit, CancellationToken ct)
    {
        var vm = await db.Infrastructure
            .Where(x => EF.Property<Guid>(x, EfInfrastructureConfiguration.IdFieldName) == cmd.Id.Value)
            .Where(x => x.Type == InfrastructureType.VirtualMachine)
            .SingleOrDefaultAsync(ct);
        if (vm is null) return null;

        db.Entry(vm).Property(x => x.Xmin).OriginalValue = cmd.ExpectedVersion;

        vm.Edit(cmd.DisplayName, cmd.Description, cmd.Provider, cmd.Attributes.ToJson());
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Capture the current row version while the tenant connection is still
            // alive — TenantScopeBeginMiddleware rolls back and disposes the
            // connection before ConcurrencyConflictExceptionHandler runs, so a fresh
            // GetDatabaseValuesAsync would fail there. Stashing on Exception.Data is
            // the handoff path (mirrors EditApplicationHandler.TryCaptureCurrentVersionAsync).
            // Shared with DeleteVmHandler (Task 6 DRY extraction) — see InfrastructureConcurrency.
            await InfrastructureConcurrency.TryCaptureCurrentXminAsync(ex, ct);
            throw;
        }

        await audit.AppendAsync(new AuditEntry(
            CatalogAuditActions.InfrastructureEdited,
            CatalogAuditTargetTypes.Infrastructure,
            vm.Id.Value.ToString(),
            new Dictionary<string, string?>
            {
                ["displayName"] = vm.DisplayName,
                ["provider"] = vm.Provider,
            }), ct);

        var attrs = VmAttributes.FromJson(vm.Attributes).ToDto();
        return new VmDetailResponse(
            vm.Id.Value, vm.TenantId.Value, vm.DisplayName, vm.Description, vm.Provider,
            vm.TeamId, vm.SystemId, vm.CreatedByUserId, vm.CreatedAt,
            VersionEncoding.Encode(vm.Xmin), attrs);
    }
}
