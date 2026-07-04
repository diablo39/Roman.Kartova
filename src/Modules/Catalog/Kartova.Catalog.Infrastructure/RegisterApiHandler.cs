using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Audit;
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Direct-dispatch handler for <see cref="RegisterApiCommand"/> (ADR-0093).
/// Tenant id + created-by come from <see cref="ITenantContext"/> / <see cref="ICurrentUser"/>;
/// the owning team id is validated by the delegate before dispatch. Audit row written
/// in-transaction (fail-closed).</summary>
public sealed class RegisterApiHandler
{
    private readonly TimeProvider _clock;

    public RegisterApiHandler(TimeProvider clock) => _clock = clock;

    public async Task<ApiResponse> Handle(
        RegisterApiCommand cmd,
        CatalogDbContext db,
        ITenantContext tenant,
        ICurrentUser user,
        IAuditWriter audit,
        CancellationToken ct)
    {
        var api = Api.Create(
            cmd.DisplayName, cmd.Description, cmd.Style, cmd.Version, cmd.SpecUrl,
            user.UserId, cmd.TeamId, tenant.Id, _clock);

        db.Apis.Add(api);
        await db.SaveChangesAsync(ct);

        await audit.AppendAsync(new AuditEntry(
            CatalogAuditActions.ApiRegistered,
            CatalogAuditTargetTypes.Api,
            api.Id.Value.ToString(),
            new Dictionary<string, string?>
            {
                ["displayName"] = api.DisplayName,
                ["style"] = api.Style.ToString(),
                ["version"] = api.Version,
                ["teamId"] = api.TeamId.ToString(),
            }), ct);

        return api.ToResponse();
    }
}
