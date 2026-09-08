using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Audit;
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Direct-dispatch handler for <see cref="RegisterVmCommand"/> (ADR-0093). Tenant id +
/// created-by come from <see cref="ITenantContext"/> / <see cref="ICurrentUser"/>; the owning
/// team id is validated by the delegate before dispatch. Audit row is written in-transaction
/// (fail-closed) before the response is returned. Mirrors <see cref="RegisterServiceHandler"/>.
/// </summary>
public sealed class RegisterVmHandler
{
    private readonly TimeProvider _clock;

    public RegisterVmHandler(TimeProvider clock) => _clock = clock;

    public async Task<VmDetailResponse> Handle(
        RegisterVmCommand cmd,
        CatalogDbContext db,
        ITenantContext tenant,
        ICurrentUser user,
        IAuditWriter audit,
        CancellationToken ct)
    {
        var vm = InfrastructureResource.Create(
            cmd.DisplayName, cmd.Description, InfrastructureType.VirtualMachine, cmd.Attributes.ToJson(),
            user.UserId, cmd.TeamId, tenant.Id, _clock);

        db.Infrastructure.Add(vm);
        await db.SaveChangesAsync(ct);

        await audit.AppendAsync(new AuditEntry(
            CatalogAuditActions.InfrastructureRegistered,
            CatalogAuditTargetTypes.Infrastructure,
            vm.Id.Value.ToString(),
            new Dictionary<string, string?>
            {
                ["displayName"] = vm.DisplayName,
                ["teamId"] = vm.TeamId.ToString(),
                ["type"] = vm.Type.ToString(),
            }), ct);

        var attrs = cmd.Attributes.ToDto();
        return new VmDetailResponse(
            vm.Id.Value, vm.TenantId.Value, vm.DisplayName, vm.Description,
            vm.TeamId, vm.SystemId, vm.CreatedByUserId, vm.CreatedAt, attrs);
    }
}
