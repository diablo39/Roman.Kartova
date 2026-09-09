using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.AspNetCore;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Handler for <see cref="GetVmByIdQuery"/>. RLS scopes the result set. Returns <c>null</c>
/// when the id is absent or resolves to a non-VM Infrastructure resource (ADR-0111 amendment).
/// </summary>
public sealed class GetVmByIdHandler
{
    public async Task<VmDetailResponse?> Handle(
        GetVmByIdQuery q, CatalogDbContext db, CancellationToken ct)
    {
        var vm = await db.Infrastructure
            .Where(x => EF.Property<Guid>(x, EfInfrastructureConfiguration.IdFieldName) == q.Id)
            .Where(x => x.Type == InfrastructureType.VirtualMachine)
            .SingleOrDefaultAsync(ct);

        if (vm is null)
            return null;

        var attrs = VmAttributes.FromJson(vm.Attributes).ToDto();
        return new VmDetailResponse(
            vm.Id.Value, vm.TenantId.Value, vm.DisplayName, vm.Description, vm.Provider,
            vm.TeamId, vm.SystemId, vm.CreatedByUserId, vm.CreatedAt,
            VersionEncoding.Encode(vm.Xmin), attrs);
    }
}
