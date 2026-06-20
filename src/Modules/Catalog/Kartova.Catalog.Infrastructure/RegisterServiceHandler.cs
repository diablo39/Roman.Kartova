using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Audit;
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Direct-dispatch handler for <see cref="RegisterServiceCommand"/> (ADR-0093).
/// Tenant id + created-by come from <see cref="ITenantContext"/> / <see cref="ICurrentUser"/>;
/// the owning team id is validated by the delegate before dispatch. Audit row is written
/// in-transaction (fail-closed) before the response is returned.
/// </summary>
public sealed class RegisterServiceHandler
{
    private readonly TimeProvider _clock;

    public RegisterServiceHandler(TimeProvider clock) => _clock = clock;

    public async Task<ServiceResponse> Handle(
        RegisterServiceCommand cmd,
        CatalogDbContext db,
        ITenantContext tenant,
        ICurrentUser user,
        IAuditWriter audit,
        CancellationToken ct)
    {
        var endpoints = cmd.Endpoints.Select(e => new ServiceEndpoint(e.Url, e.Protocol));
        var svc = Service.Create(
            cmd.DisplayName, cmd.Description, user.UserId, cmd.TeamId, endpoints, tenant.Id, _clock);

        db.Services.Add(svc);
        await db.SaveChangesAsync(ct);

        await audit.AppendAsync(new AuditEntry(
            CatalogAuditActions.ServiceRegistered,
            CatalogAuditTargetTypes.Service,
            svc.Id.Value.ToString(),
            new Dictionary<string, string?>
            {
                ["displayName"] = svc.DisplayName,
                ["teamId"] = svc.TeamId.ToString(),
                ["endpointCount"] = svc.Endpoints.Count.ToString(),
            }), ct);

        return svc.ToResponse();
    }
}
