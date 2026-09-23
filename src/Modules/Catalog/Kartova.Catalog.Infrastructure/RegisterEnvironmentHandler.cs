using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Audit;
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Direct-dispatch handler for <see cref="RegisterEnvironmentCommand"/> (ADR-0093).
/// Tenant id + created-by come from <see cref="ITenantContext"/>/<see cref="ICurrentUser"/>.
/// Audit row written in-transaction (fail-closed) before the response. Uniqueness (display name
/// per tenant) is enforced by the DB unique index + the delegate's pre-check/backstop.</summary>
public sealed class RegisterEnvironmentHandler
{
    private readonly TimeProvider _clock;

    public RegisterEnvironmentHandler(TimeProvider clock) => _clock = clock;

    public async Task<EnvironmentDetailResponse> Handle(
        RegisterEnvironmentCommand cmd,
        CatalogDbContext db,
        ITenantContext tenant,
        ICurrentUser user,
        IAuditWriter audit,
        CancellationToken ct)
    {
        var env = CatalogEnvironment.Create(
            cmd.DisplayName, cmd.Description, cmd.Type, cmd.Region, cmd.ResourceDetailsJson,
            user.UserId, tenant.Id, _clock);

        db.Environments.Add(env);
        await db.SaveChangesAsync(ct);

        await audit.AppendAsync(new AuditEntry(
            CatalogAuditActions.EnvironmentRegistered,
            CatalogAuditTargetTypes.Environment,
            env.Id.Value.ToString(),
            new Dictionary<string, string?>
            {
                ["displayName"] = env.DisplayName,
                ["type"] = env.Type.ToString(),
            }), ct);

        return new EnvironmentDetailResponse(
            env.Id.Value, env.TenantId.Value, env.DisplayName, env.Description, env.Type, env.Region,
            EnvironmentResourceDetails.FromJson(env.ResourceDetails), env.CreatedByUserId, env.CreatedAt,
            VersionEncoding.Encode(env.Xmin));
    }
}
