using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Audit;
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Direct-dispatch handler for <see cref="RegisterSystemCommand"/> (ADR-0093).
/// Tenant id + created-by come from <see cref="ITenantContext"/> / <see cref="ICurrentUser"/>;
/// the steward team id is validated by the delegate before dispatch. Audit row written
/// in-transaction (fail-closed).</summary>
public sealed class RegisterSystemHandler
{
    private readonly TimeProvider _clock;

    public RegisterSystemHandler(TimeProvider clock) => _clock = clock;

    public async Task<SystemResponse> Handle(
        RegisterSystemCommand cmd,
        CatalogDbContext db,
        ITenantContext tenant,
        ICurrentUser user,
        IAuditWriter audit,
        CancellationToken ct)
    {
        var system = CatalogSystem.Create(
            cmd.DisplayName, cmd.Description, user.UserId, cmd.TeamId, tenant.Id, _clock);

        db.Systems.Add(system);
        await db.SaveChangesAsync(ct);

        await audit.AppendAsync(new AuditEntry(
            CatalogAuditActions.SystemRegistered,
            CatalogAuditTargetTypes.System,
            system.Id.Value.ToString(),
            new Dictionary<string, string?>
            {
                ["displayName"] = system.DisplayName,
                ["teamId"] = system.TeamId.ToString(),
            }), ct);

        return system.ToResponse();
    }
}
